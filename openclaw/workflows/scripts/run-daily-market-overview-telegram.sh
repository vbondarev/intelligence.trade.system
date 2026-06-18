#!/usr/bin/env sh
set -eu

SYMBOL="${1:-BTCUSDT}"
RUN_ID="${2:-}"
LOCK_PATH="${3:-}"
ANALYSIS_MODE="${ANALYSIS_MODE:-intraday}"

LOG_ROOT="/home/node/.openclaw/logs/daily-market"
BASE_SCRIPT="/home/node/.openclaw/workflows/scripts/run-daily-market-overview.sh"
CONFIG_FILE="/home/node/.openclaw/openclaw.json"
TELEGRAM_TARGETS_FILE="/home/node/.openclaw/workflows/config/telegram-targets.json"
MAX_ATTEMPTS="${MAX_ATTEMPTS:-3}"

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

DAY_DIR="$(date -u +%Y-%m-%d)"
LOG_DIR="${LOG_ROOT}/${DAY_DIR}/${RUN_ID}"

mkdir -p "$LOG_DIR"

RUN_LOG="${LOG_DIR}/run.log"
ERROR_LOG="${LOG_DIR}/error.log"

log() {
  printf '%s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" >> "$RUN_LOG"
}

err() {
  printf '%s ERROR %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" >> "$ERROR_LOG"
}

cleanup_lock() {
  if [ -n "$LOCK_PATH" ] && [ -f "$LOCK_PATH" ]; then
    rm -f "$LOCK_PATH"
  fi
}

get_telegram_token() {
  node - "$CONFIG_FILE" <<'NODE'
const fs = require("fs");
const path = process.argv[2];
const config = JSON.parse(fs.readFileSync(path, "utf8"));

const token = config?.channels?.telegram?.botToken;

if (!token) {
  console.error("Missing channels.telegram.botToken in openclaw.json");
  process.exit(2);
}

process.stdout.write(String(token));
NODE
}

get_telegram_chat_id() {
  node - "$TELEGRAM_TARGETS_FILE" <<'NODE'
const fs = require("fs");
const path = process.argv[2];
const config = JSON.parse(fs.readFileSync(path, "utf8"));

const chatId = config?.btcDailyCheck?.chatId;

if (!chatId) {
  console.error("Missing btcDailyCheck.chatId in telegram-targets.json");
  process.exit(2);
}

process.stdout.write(String(chatId));
NODE
}

get_telegram_thread_id() {
  node - "$TELEGRAM_TARGETS_FILE" <<'NODE'
const fs = require("fs");
const path = process.argv[2];
const config = JSON.parse(fs.readFileSync(path, "utf8"));

const threadId = config?.btcDailyCheck?.messageThreadId;

if (threadId === undefined || threadId === null || threadId === "") {
  process.exit(0);
}

process.stdout.write(String(threadId));
NODE
}

send_text_file() {
  FILE_PATH="$1"

  TOKEN="$(get_telegram_token)"
  CHAT_ID="$(get_telegram_chat_id)"
  THREAD_ID="$(get_telegram_thread_id)"

  TELEGRAM_TOKEN="$TOKEN" \
  TELEGRAM_CHAT_ID="$CHAT_ID" \
  TELEGRAM_THREAD_ID="$THREAD_ID" \
  TELEGRAM_FILE="$FILE_PATH" \
  node <<'NODE'
const fs = require("fs");

async function main() {
  const token = process.env.TELEGRAM_TOKEN;
  const chatId = process.env.TELEGRAM_CHAT_ID;
  const threadId = process.env.TELEGRAM_THREAD_ID;
  const filePath = process.env.TELEGRAM_FILE;

  if (!token) {
    throw new Error("TELEGRAM_TOKEN is empty");
  }

  if (!chatId) {
    throw new Error("TELEGRAM_CHAT_ID is empty");
  }

  const text = fs.readFileSync(filePath, "utf8");
  const chunks = [];

  for (let i = 0; i < text.length; i += 3900) {
    chunks.push(text.slice(i, i + 3900));
  }

  for (const chunk of chunks) {
    const payload = {
      chat_id: chatId,
      text: chunk,
      disable_web_page_preview: true
    };

    if (threadId) {
      payload.message_thread_id = Number(threadId);
    }

    const response = await fetch(`https://api.telegram.org/bot${token}/sendMessage`, {
      method: "POST",
      headers: {
        "content-type": "application/json; charset=utf-8"
      },
      body: JSON.stringify(payload)
    });

    if (!response.ok) {
      const body = await response.text();
      throw new Error(`Telegram sendMessage failed: ${response.status} ${body}`);
    }
  }
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
NODE
}

log "wrapper started: run_id=${RUN_ID} symbol=${SYMBOL} mode=${ANALYSIS_MODE} backend_mode=${BACKEND_ANALYSIS_MODE} log_dir=${LOG_DIR} lock_path=${LOCK_PATH}"

attempt=1
while [ "$attempt" -le "$MAX_ATTEMPTS" ]; do
  log "attempt ${attempt} started mode=${ANALYSIS_MODE}"

  set +e
  ANALYSIS_MODE="$ANALYSIS_MODE" ATTEMPT="$attempt" "$BASE_SCRIPT" "$SYMBOL" "$RUN_ID" "$LOG_DIR" > "/tmp/${RUN_ID}-attempt-${attempt}-stdout.txt" 2> "/tmp/${RUN_ID}-attempt-${attempt}-stderr.txt"
  EXIT_CODE="$?"
  set -e

  if [ "$EXIT_CODE" -eq 0 ] && [ -s "$LOG_DIR/final-post.md" ]; then
    log "attempt ${attempt} succeeded"

    set +e
    send_text_file "$LOG_DIR/final-post.md" >> "$RUN_LOG" 2>> "$ERROR_LOG"
    SEND_EXIT="$?"
    set -e

    if [ "$SEND_EXIT" -ne 0 ]; then
      err "telegram send failed: exit_code=${SEND_EXIT}"
      cleanup_lock
      exit 1
    fi

    log "telegram final post sent"
    rm -f "/tmp/${RUN_ID}-attempt-${attempt}-stdout.txt" "/tmp/${RUN_ID}-attempt-${attempt}-stderr.txt"
    cleanup_lock
    exit 0
  fi

  STDERR_TEXT="$(cat "/tmp/${RUN_ID}-attempt-${attempt}-stderr.txt" 2>/dev/null || true)"
  err "attempt ${attempt} failed: exit_code=${EXIT_CODE} ${STDERR_TEXT}"

  mkdir -p "$LOG_DIR/debug"
  cp "/tmp/${RUN_ID}-attempt-${attempt}-stdout.txt" "$LOG_DIR/debug/attempt-${attempt}-stdout.txt" 2>/dev/null || true
  cp "/tmp/${RUN_ID}-attempt-${attempt}-stderr.txt" "$LOG_DIR/debug/attempt-${attempt}-stderr.txt" 2>/dev/null || true

  attempt=$((attempt + 1))
  sleep 3
done

FAIL_FILE="/tmp/${RUN_ID}-workflow-failed-message.txt"

{
  printf 'Workflow failed for %s\n' "$SYMBOL"
  printf 'Mode: %s\n' "$ANALYSIS_MODE"
  printf 'Run ID: %s\n' "$RUN_ID"
  printf 'Log dir: %s\n' "$LOG_DIR"
} > "$FAIL_FILE"

send_text_file "$FAIL_FILE" >> "$RUN_LOG" 2>> "$ERROR_LOG" || true
rm -f "$FAIL_FILE"

cleanup_lock
exit 1
