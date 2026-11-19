#!/usr/bin/env bash
# Проверяет public scheme contract через frontend proxy запущенного Compose stack:
# - local frontend (PUBLIC_SCHEME=http) строит redirect_uri от http://localhost:8082;
# - frontend за внешним TLS terminator (PUBLIC_SCHEME=https, plain HTTP до container)
#   строит redirect_uri от https://<public host>;
# - X-Forwarded-Proto от client не влияет на redirect_uri ни в одном режиме.
set -euo pipefail

compose=(docker compose -f backend/compose.yaml)
local_origin="http://localhost:8082"
identity_origin="http://localhost:8081"
tls_container="intelligence-trade-web-tls-smoke"
tls_port="18082"
public_host="trade.example.com"

cleanup() {
  docker rm -f "$tls_container" >/dev/null 2>&1 || true
}

trap cleanup EXIT

verify_login_response() {
  python3 -c '
import sys
from email.parser import Parser
from urllib.parse import parse_qs, urlsplit

description, stage, origin, callback = sys.argv[1:]
status_line, _, fields = sys.stdin.read().partition("\n")
headers = Parser().parsestr(fields)
status = status_line.split()[1:2]
status_display = status[0] if status else "не определён"

def check(condition, field):
    if not condition:
        sys.exit(f"{description} ({stage}): {field}, HTTP {status_display}.")

check(status == ["302"], "ожидался redirect")
locations = headers.get_all("Location", [])
check(len(locations) == 1, "ожидался один Location")
try:
    location = urlsplit(locations[0])
    expected_origin = urlsplit(origin)
except ValueError:
    sys.exit(f"{description} ({stage}): некорректный адрес редиректа.")
check((location.scheme, location.netloc) == (expected_origin.scheme, expected_origin.netloc),
      "неожиданный origin Identity")
query = parse_qs(location.query, keep_blank_values=True)

if stage == "prepare":
    check(location.path == "/account/web-login", "неожиданный путь подготовки входа")
    check(query.get("returnUrl") == ["/app"], "неожиданный returnUrl")
    check("redirect_uri" not in query, "OIDC challenge создан до входа")
    names = [cookie.split("=", 1)[0].lower() for cookie in headers.get_all("Set-Cookie", [])]
    check(not any("correlation" in name or "nonce" in name for name in names),
          "OIDC cookies созданы до входа")
else:
    check(location.path == "/connect/authorize", "неожиданный путь OIDC challenge")
    check(query.get("prompt") == ["none"], "ожидался prompt=none")
    check(query.get("redirect_uri") == [callback], "неожиданный redirect_uri")
' "$@"
}

expect_redirect_uri() {
  local description="$1"
  local expected="$2"
  local url="$3"
  shift 3
  local headers
  headers="$(curl --silent --show-error -D - -o /dev/null "$@" "$url/bff/auth/login?returnUrl=/app")"
  verify_login_response "$description" prepare "$identity_origin" "$expected" <<< "$headers"
  headers="$(curl --silent --show-error -D - -o /dev/null "$@" "$url/bff/auth/login/continue?returnUrl=/app")"
  verify_login_response "$description" challenge "$identity_origin" "$expected" <<< "$headers"
  echo "ok: $description → $expected"
}

expect_redirect_uri "local PUBLIC_SCHEME=http" \
  "$local_origin/signin-oidc" "$local_origin"
expect_redirect_uri "local: client X-Forwarded-Proto игнорируется" \
  "$local_origin/signin-oidc" "$local_origin" -H "X-Forwarded-Proto: https"

# Второй экземпляр той же frontend service с production-like scheme: TLS завершается перед
# container, поэтому он получает plain HTTP, а public scheme задаёт только deployment setting.
cleanup
"${compose[@]}" run -d --no-deps --name "$tls_container" \
  --publish "127.0.0.1:$tls_port:8080" \
  -e PUBLIC_SCHEME=https \
  frontend >/dev/null

tls_url="http://127.0.0.1:$tls_port"
ready=false
for _ in {1..30}; do
  if curl --silent --fail --output /dev/null -H "Host: $public_host" "$tls_url/bff/auth/session"; then
    ready=true
    break
  fi
  sleep 2
done
if [[ "$ready" != true ]]; then
  echo "TLS-terminated frontend не стал доступен." >&2
  docker logs "$tls_container" >&2 || true
  exit 1
fi

expect_redirect_uri "TLS termination PUBLIC_SCHEME=https" \
  "https://$public_host/signin-oidc" "$tls_url" -H "Host: $public_host"
expect_redirect_uri "TLS termination: client X-Forwarded-Proto игнорируется" \
  "https://$public_host/signin-oidc" "$tls_url" -H "Host: $public_host" -H "X-Forwarded-Proto: http"
