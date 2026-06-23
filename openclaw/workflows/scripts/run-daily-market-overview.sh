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
VALIDATOR_SCRIPT="/home/node/.openclaw/workflows/scripts/validate-technical-report.js"
TECHNICAL_REPORT_SCHEMA="/home/node/.openclaw/schemas/technical-report.schema.json"

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
TECHNICAL_REPORT_PATH="${LOG_DIR}/technical-report.json"
TECHNICAL_REPORT_VALIDATION_ERRORS="${LOG_DIR}/technical-report-validation-errors.txt"

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
  node - "$TECH_RAW" "$TECHNICAL_REPORT_PATH" <<'NODE'
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
  node "$VALIDATOR_SCRIPT" \
    "$TECHNICAL_REPORT_PATH" \
    "$SYMBOL" \
    "$BACKEND_ANALYSIS_MODE" \
    "$TECHNICAL_REPORT_VALIDATION_ERRORS" \
    "$TECHNICAL_REPORT_SCHEMA"
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
  --session-key "agent:tech-analysis-agent:${RUN_ID}" \
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
cp "$TECHNICAL_REPORT_PATH" "$CHIEF_WORKSPACE/input/technical_report.json"

log "running chief-market-synthesizer"
set +e
node "$OPENCLAW_BIN" agent \
  --agent chief-market-synthesizer \
  --session-key "agent:chief-market-synthesizer:${RUN_ID}" \
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
