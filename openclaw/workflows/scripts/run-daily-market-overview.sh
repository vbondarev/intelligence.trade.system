#!/usr/bin/env sh
set -eu

SYMBOL="${1:-BTCUSDT}"
RUN_ID="${2:-}"
LOG_DIR="${3:-}"
ATTEMPT="${ATTEMPT:-1}"
DEBUG_DAILY_MARKET="${DEBUG_DAILY_MARKET:-0}"
ANALYSIS_MODE="${ANALYSIS_MODE:-intraday}"

OPENCLAW_BIN="/app/dist/index.js"
BACKEND_BASE_URL="http://intelligence-trade-api:8080"
CHIEF_WORKSPACE="/home/node/.openclaw/workspaces/chief-market-synthesizer"
LOG_ROOT="/home/node/.openclaw/logs/daily-market"

normalize_analysis_mode() {
  case "$ANALYSIS_MODE" in
    intraday)
      BACKEND_ANALYSIS_MODE="Intraday"
      ;;
    swing)
      BACKEND_ANALYSIS_MODE="Swing"
      ;;
    portfolio)
      BACKEND_ANALYSIS_MODE="Portfolio"
      ;;
    *)
      printf '%s\n' "Unsupported analysis mode: ${ANALYSIS_MODE}. Supported modes: intraday, swing, portfolio." >&2
      exit 2
      ;;
  esac
}

make_suffix() {
  if [ -r /dev/urandom ]; then
    od -An -N3 -tx1 /dev/urandom | tr -d ' \n'
  else
    date -u +%s
  fi
}

normalize_analysis_mode

if [ -z "$RUN_ID" ]; then
  RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)-${ANALYSIS_MODE}-${SYMBOL}-$(make_suffix)"
fi

if [ -z "$LOG_DIR" ]; then
  DAY_DIR="$(date -u +%Y-%m-%d)"
  LOG_DIR="${LOG_ROOT}/${DAY_DIR}/${RUN_ID}"
fi

mkdir -p "$LOG_DIR"

RUN_LOG="${LOG_DIR}/run.log"
ERROR_LOG="${LOG_DIR}/error.log"
DEBUG_DIR="${LOG_DIR}/debug"

TECH_RAW="/tmp/${RUN_ID}-attempt-${ATTEMPT}-tech-agent-run.json"
CHIEF_RAW="/tmp/${RUN_ID}-attempt-${ATTEMPT}-chief-agent-run.json"
TECH_STDERR="/tmp/${RUN_ID}-attempt-${ATTEMPT}-tech-agent-stderr.log"
CHIEF_STDERR="/tmp/${RUN_ID}-attempt-${ATTEMPT}-chief-agent-stderr.log"

log() {
  printf '%s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" >> "$RUN_LOG"
}

err() {
  printf '%s ERROR %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" >> "$ERROR_LOG"
}

save_debug() {
  mkdir -p "$DEBUG_DIR"

  [ -f "$TECH_RAW" ] && cp "$TECH_RAW" "$DEBUG_DIR/attempt-${ATTEMPT}-tech-agent-run.json"
  [ -f "$CHIEF_RAW" ] && cp "$CHIEF_RAW" "$DEBUG_DIR/attempt-${ATTEMPT}-chief-agent-run.json"
  [ -f "$TECH_STDERR" ] && cp "$TECH_STDERR" "$DEBUG_DIR/attempt-${ATTEMPT}-tech-agent-stderr.log"
  [ -f "$CHIEF_STDERR" ] && cp "$CHIEF_STDERR" "$DEBUG_DIR/attempt-${ATTEMPT}-chief-agent-stderr.log"
}

cleanup_tmp() {
  if [ "$DEBUG_DAILY_MARKET" = "1" ]; then
    save_debug
  fi

  rm -f "$TECH_RAW" "$CHIEF_RAW" "$TECH_STDERR" "$CHIEF_STDERR"
}

fail() {
  err "$*"
  save_debug
  cleanup_tmp || true
  printf '%s\n' "$*" >&2
  exit 1
}

write_meta() {
  RUN_ID="$RUN_ID" SYMBOL="$SYMBOL" LOG_DIR="$LOG_DIR" ATTEMPT="$ATTEMPT" ANALYSIS_MODE="$ANALYSIS_MODE" BACKEND_ANALYSIS_MODE="$BACKEND_ANALYSIS_MODE" node <<'NODE'
const fs = require("fs");

const metaPath = `${process.env.LOG_DIR}/meta.json`;
let existing = {};
if (fs.existsSync(metaPath)) {
  try { existing = JSON.parse(fs.readFileSync(metaPath, "utf8")); } catch {}
}

const now = new Date().toISOString();
const attempts = Array.isArray(existing.attempts) ? existing.attempts : [];

attempts.push({
  attempt: Number(process.env.ATTEMPT || "1"),
  startedAtUtc: now
});

const meta = {
  runId: process.env.RUN_ID,
  symbol: process.env.SYMBOL,
  analysisMode: process.env.ANALYSIS_MODE,
  backendAnalysisMode: process.env.BACKEND_ANALYSIS_MODE,
  logDir: process.env.LOG_DIR,
  updatedAtUtc: now,
  attempts
};

fs.writeFileSync(metaPath, JSON.stringify(meta, null, 2) + "\n");
NODE
}

save_backend_snapshot() {
  BACKEND_URL="${BACKEND_BASE_URL}/api/market-analysis/${SYMBOL}/llm-payload?exchange=Bybit&category=Linear&mode=${BACKEND_ANALYSIS_MODE}&includePortfolio=false&includeAggregatedContext=false"

  {
    printf 'RUN_ID=%s\n' "$RUN_ID"
    printf 'SYMBOL=%s\n' "$SYMBOL"
    printf 'ANALYSIS_MODE=%s\n' "$ANALYSIS_MODE"
    printf 'BACKEND_ANALYSIS_MODE=%s\n' "$BACKEND_ANALYSIS_MODE"
    printf 'URL=%s\n' "$BACKEND_URL"
    printf 'HEADER=X-Correlation-Id: %s\n' "$RUN_ID"
    printf 'CREATED_AT_UTC=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  } > "$LOG_DIR/backend-request.txt"

  set +e
  curl -sS \
    -H "X-Correlation-Id: $RUN_ID" \
    "$BACKEND_URL" \
    -o "$LOG_DIR/backend-payload.json" \
    -w "%{http_code}" \
    > "/tmp/${RUN_ID}-backend-status.txt" \
    2> "/tmp/${RUN_ID}-backend-curl.log"
  CURL_EXIT="$?"
  set -e

  BACKEND_STATUS="$(cat "/tmp/${RUN_ID}-backend-status.txt" 2>/dev/null || true)"
  BACKEND_CURL_LOG="$(cat "/tmp/${RUN_ID}-backend-curl.log" 2>/dev/null || true)"

  rm -f "/tmp/${RUN_ID}-backend-status.txt" "/tmp/${RUN_ID}-backend-curl.log"

  if [ "$CURL_EXIT" -ne 0 ]; then
    err "backend snapshot failed: curl_exit=${CURL_EXIT} ${BACKEND_CURL_LOG}"
  else
    log "backend snapshot saved: http_status=${BACKEND_STATUS} mode=${BACKEND_ANALYSIS_MODE}"
  fi
}

extract_technical_report() {
  node - "$TECH_RAW" "$LOG_DIR/technical-report.json" <<'NODE'
const fs = require("fs");

const inputPath = process.argv[2];
const outputPath = process.argv[3];

function stripAnsi(value) {
  return value.replace(/\x1B\[[0-?]*[ -/]*[@-~]/g, "");
}

function parseLooseJson(raw) {
  const clean = stripAnsi(raw).trim();
  try {
    return JSON.parse(clean);
  } catch {
    const first = clean.indexOf("{");
    const last = clean.lastIndexOf("}");
    if (first < 0 || last < first) {
      throw new Error("No JSON object found");
    }
    return JSON.parse(clean.slice(first, last + 1));
  }
}

const outer = parseLooseJson(fs.readFileSync(inputPath, "utf8"));
const text = outer?.result?.payloads?.[0]?.text;

if (!text || typeof text !== "string") {
  throw new Error("tech-analysis-agent did not return payloads[0].text");
}

const report = parseLooseJson(text);
fs.writeFileSync(outputPath, JSON.stringify(report, null, 2) + "\n");
NODE
}

validate_technical_report() {
  node - "$LOG_DIR/technical-report.json" "$SYMBOL" "$BACKEND_ANALYSIS_MODE" "$LOG_DIR/technical-report-validation-errors.txt" <<'NODE'
const fs = require("fs");

const reportPath = process.argv[2];
const expectedSymbol = process.argv[3];
const expectedMode = process.argv[4];
const errorsPath = process.argv[5];

const statusValues = new Set(["ok", "partial", "error", "no_data"]);
const analysisModes = new Set(["Intraday", "Swing", "Portfolio"]);
const confidenceValues = new Set(["high", "medium", "low"]);
const biasValues = new Set(["bullish", "bearish", "neutral", "mixed", "unknown"]);
const entryQualityValues = new Set(["good", "medium", "poor", "no_trade", "unknown"]);
const scenarioStatusValues = new Set(["available", "not_available", "wait"]);
const priorityValues = new Set(["long", "short", "neutral", "wait", "no_trade", "unknown"]);

const errors = [];

function isObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function hasOwn(object, key) {
  return Object.prototype.hasOwnProperty.call(object, key);
}

function requireObject(parent, key, path) {
  if (!isObject(parent?.[key])) {
    errors.push(`${path}.${key} must be an object`);
    return {};
  }
  return parent[key];
}

function requireArray(parent, key, path) {
  if (!Array.isArray(parent?.[key])) {
    errors.push(`${path}.${key} must be an array`);
    return [];
  }
  return parent[key];
}

function requireString(parent, key, path) {
  if (typeof parent?.[key] !== "string" || parent[key].trim() === "") {
    errors.push(`${path}.${key} must be a non-empty string`);
    return "";
  }
  return parent[key];
}

function requireBoolean(parent, key, path) {
  if (typeof parent?.[key] !== "boolean") {
    errors.push(`${path}.${key} must be a boolean`);
  }
}

function requireEnum(parent, key, values, path) {
  const value = requireString(parent, key, path);
  if (value && !values.has(value)) {
    errors.push(`${path}.${key} has unsupported value: ${value}`);
  }
  return value;
}

function validateScenario(parent, key) {
  const scenario = requireObject(parent, key, "scenarios");
  requireEnum(scenario, "status", scenarioStatusValues, `scenarios.${key}`);
  if (scenario.condition !== null && scenario.condition !== undefined && typeof scenario.condition !== "string") {
    errors.push(`scenarios.${key}.condition must be a string or null`);
  }
  if (scenario.invalidation !== null && scenario.invalidation !== undefined && typeof scenario.invalidation !== "string") {
    errors.push(`scenarios.${key}.invalidation must be a string or null`);
  }
  requireArray(scenario, "targets", `scenarios.${key}`);
}

let report;
try {
  report = JSON.parse(fs.readFileSync(reportPath, "utf8"));
} catch (error) {
  fs.writeFileSync(errorsPath, `technical_report is not valid JSON: ${error.message}\n`);
  console.error(`technical_report is not valid JSON: ${error.message}`);
  process.exit(1);
}

if (!isObject(report)) {
  errors.push("technical_report root must be an object");
} else {
  const requiredTopLevel = [
    "status",
    "symbol",
    "exchange",
    "category",
    "analysis_mode",
    "generated_at_utc",
    "source",
    "data_quality",
    "market",
    "timeframes",
    "technical_summary",
    "key_metrics",
    "levels",
    "scenarios",
    "risk",
    "conclusion"
  ];

  for (const key of requiredTopLevel) {
    if (!hasOwn(report, key)) {
      errors.push(`missing top-level field: ${key}`);
    }
  }

  const status = requireEnum(report, "status", statusValues, "root");
  const symbol = requireString(report, "symbol", "root");
  requireString(report, "exchange", "root");
  requireString(report, "category", "root");
  const analysisMode = requireEnum(report, "analysis_mode", analysisModes, "root");

  if (symbol && symbol !== expectedSymbol) {
    errors.push(`root.symbol must match requested symbol ${expectedSymbol}, got ${symbol}`);
  }

  if (report.exchange && report.exchange !== "Bybit") {
    errors.push(`root.exchange must be Bybit, got ${report.exchange}`);
  }

  if (report.category && report.category !== "Linear") {
    errors.push(`root.category must be Linear, got ${report.category}`);
  }

  if (analysisMode && analysisMode !== expectedMode) {
    errors.push(`root.analysis_mode must match backend mode ${expectedMode}, got ${analysisMode}`);
  }

  if (report.generated_at_utc !== null && typeof report.generated_at_utc !== "string") {
    errors.push("root.generated_at_utc must be a string or null");
  }

  const source = requireObject(report, "source", "root");
  if (source.backend_url !== null && source.backend_url !== undefined && typeof source.backend_url !== "string") {
    errors.push("source.backend_url must be a string or null");
  }
  if (source.payload_timestamp_utc !== null && source.payload_timestamp_utc !== undefined && typeof source.payload_timestamp_utc !== "string") {
    errors.push("source.payload_timestamp_utc must be a string or null");
  }

  const dataQuality = requireObject(report, "data_quality", "root");
  requireBoolean(dataQuality, "is_stale", "data_quality");
  requireBoolean(dataQuality, "is_partial", "data_quality");
  requireEnum(dataQuality, "confidence", confidenceValues, "data_quality");
  requireArray(dataQuality, "warnings", "data_quality");

  const market = requireObject(report, "market", "root");
  requireString(market, "base_asset", "market");

  const timeframes = requireObject(report, "timeframes", "root");
  requireArray(timeframes, "primary", "timeframes");
  requireArray(timeframes, "context", "timeframes");
  requireArray(timeframes, "items", "timeframes");

  const technicalSummary = requireObject(report, "technical_summary", "root");
  requireEnum(technicalSummary, "bias", biasValues, "technical_summary");
  requireEnum(technicalSummary, "entry_quality", entryQualityValues, "technical_summary");
  requireString(technicalSummary, "summary", "technical_summary");

  requireObject(report, "key_metrics", "root");

  const levels = requireObject(report, "levels", "root");
  requireArray(levels, "support", "levels");
  requireArray(levels, "resistance", "levels");

  const scenarios = requireObject(report, "scenarios", "root");
  validateScenario(scenarios, "long");
  validateScenario(scenarios, "short");

  const risk = requireObject(report, "risk", "root");
  requireString(risk, "summary", "risk");
  requireArray(risk, "items", "risk");

  const conclusion = requireObject(report, "conclusion", "root");
  requireEnum(conclusion, "priority", priorityValues, "conclusion");
  requireString(conclusion, "text", "conclusion");

  if (status === "ok" && dataQuality.confidence === "low") {
    errors.push("status ok should not have low confidence; use partial when confidence is low");
  }
}

if (errors.length > 0) {
  const text = errors.map((error) => `- ${error}`).join("\n") + "\n";
  fs.writeFileSync(errorsPath, text);
  console.error(text);
  process.exit(1);
}

fs.writeFileSync(errorsPath, "OK\n");
NODE
}

extract_final_post() {
  node - "$CHIEF_RAW" "$LOG_DIR/final-post.md" <<'NODE'
const fs = require("fs");

const inputPath = process.argv[2];
const outputPath = process.argv[3];

function stripAnsi(value) {
  return value.replace(/\x1B\[[0-?]*[ -/]*[@-~]/g, "");
}

function parseLooseJson(raw) {
  const clean = stripAnsi(raw).trim();
  try {
    return JSON.parse(clean);
  } catch {
    const first = clean.indexOf("{");
    const last = clean.lastIndexOf("}");
    if (first < 0 || last < first) {
      throw new Error("No JSON object found");
    }
    return JSON.parse(clean.slice(first, last + 1));
  }
}

const outer = parseLooseJson(fs.readFileSync(inputPath, "utf8"));
let text = outer?.result?.payloads?.[0]?.text;

if (!text || typeof text !== "string") {
  throw new Error("chief-market-synthesizer did not return payloads[0].text");
}

text = text.trim();
text = text.replace(/^```[a-zA-Z]*\s*/g, "").replace(/\s*```$/g, "").trim();

fs.writeFileSync(outputPath, text + "\n");
NODE
}

write_meta
log "started: run_id=${RUN_ID} symbol=${SYMBOL} mode=${ANALYSIS_MODE} backend_mode=${BACKEND_ANALYSIS_MODE} attempt=${ATTEMPT}"

save_backend_snapshot

log "running tech-analysis-agent"
set +e
node "$OPENCLAW_BIN" agent \
  --agent tech-analysis-agent \
  --json \
  --message "Generate technical_report JSON for ${SYMBOL} using backend endpoint from AGENTS.md with analysis mode ${BACKEND_ANALYSIS_MODE}. Return ONLY raw JSON. No markdown. No explanations." \
  > "$TECH_RAW" \
  2> "$TECH_STDERR"
TECH_EXIT="$?"
set -e

if [ "$TECH_EXIT" -ne 0 ]; then
  fail "tech-analysis-agent failed: exit_code=${TECH_EXIT}"
fi

log "extracting technical report"
extract_technical_report || fail "failed to extract technical report"

log "validating technical report schema"
validate_technical_report || fail "technical report schema validation failed"

mkdir -p "$CHIEF_WORKSPACE/input"
cp "$LOG_DIR/technical-report.json" "$CHIEF_WORKSPACE/input/technical_report.json"

log "running chief-market-synthesizer"
set +e
node "$OPENCLAW_BIN" agent \
  --agent chief-market-synthesizer \
  --json \
  --message "Use input/technical_report.json and generate the final Telegram post according to AGENTS.md, SOUL.md and templates/daily-market-overview.md. Return only plain final post text." \
  > "$CHIEF_RAW" \
  2> "$CHIEF_STDERR"
CHIEF_EXIT="$?"
set -e

if [ "$CHIEF_EXIT" -ne 0 ]; then
  fail "chief-market-synthesizer failed: exit_code=${CHIEF_EXIT}"
fi

log "extracting final post"
extract_final_post || fail "failed to extract final post"

log "completed successfully"
cleanup_tmp

cat "$LOG_DIR/final-post.md"
