#!/usr/bin/env bash
# Проверяет public scheme contract через frontend proxy запущенного Compose stack:
# - local frontend (PUBLIC_SCHEME=http) строит redirect_uri от http://localhost:8082;
# - frontend за внешним TLS terminator (PUBLIC_SCHEME=https, plain HTTP до container)
#   строит redirect_uri от https://<public host>;
# - X-Forwarded-Proto от client не влияет на redirect_uri ни в одном режиме.
set -euo pipefail

compose=(docker compose -f backend/compose.yaml)
local_origin="http://localhost:8082"
tls_container="intelligence-trade-web-tls-smoke"
tls_port="18082"
public_host="trade.example.com"

cleanup() {
  docker rm -f "$tls_container" >/dev/null 2>&1 || true
}

trap cleanup EXIT

login_redirect_uri() {
  local url="$1"
  shift
  local headers
  headers="$(curl --silent --show-error --max-redirs 0 -D - -o /dev/null "$@" "$url/bff/auth/login?returnUrl=/app")"
  local location
  location="$(awk 'BEGIN { IGNORECASE = 1 } /^Location:/ {
    sub(/\r$/, "")
    sub(/^[^:]+:[[:space:]]*/, "")
    print
    exit
  }' <<< "$headers")"
  [[ -n "$location" ]] || {
    echo "BFF login не вернул redirect на Identity: $url" >&2
    printf '%s\n' "$headers" >&2
    return 1
  }
  python3 -c 'from urllib.parse import parse_qs, urlsplit; import sys; print(parse_qs(urlsplit(sys.argv[1]).query)["redirect_uri"][0])' "$location"
}

expect_redirect_uri() {
  local description="$1"
  local expected="$2"
  shift 2
  local actual
  actual="$(login_redirect_uri "$@")"
  if [[ "$actual" != "$expected" ]]; then
    echo "$description: redirect_uri=$actual, ожидался $expected." >&2
    exit 1
  fi
  echo "ok: $description → $actual"
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
