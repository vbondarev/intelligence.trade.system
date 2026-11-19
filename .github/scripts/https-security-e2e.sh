#!/usr/bin/env bash
set -euo pipefail

# Run from any directory, with Docker Compose, Node, OpenSSL and Playwright
# browsers installed. The project and volume are reserved exclusively here.
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
web_dir="$repo_dir/frontend/intelligence-trade-web"
project=issue225securityci
if [[ -n "$(docker compose -p "$project" ls --all --format json | node -e 'let s="";process.stdin.on("data",x=>s+=x).on("end",()=>process.stdout.write(JSON.parse(s).some(x=>x.Name===process.argv[1])?"busy":""))' "$project")" ]]; then
  echo "The isolated security test project already exists; refusing to reuse it." >&2
  exit 1
fi

cert_dir="$(mktemp -d)"
export SECURITY_CERT_DIR="$cert_dir"
if command -v cygpath >/dev/null 2>&1; then SECURITY_CERT_DIR="$(cygpath -m "$cert_dir")"; fi
export TRADE_CREDENTIAL_KEY="$(openssl rand -base64 32)"
export TRADE_WEB_BFF_CLIENT_SECRET="$(openssl rand -hex 32)"
export TRADE_WEB_DEV_PASSWORD="Test-$(openssl rand -hex 24)"
export E2E_PASSWORD="$TRADE_WEB_DEV_PASSWORD"
export WEB_BASE_URL=https://localtest.me:18994
export IDENTITY_BASE_URL=https://localtest.me:18995
compose=(docker compose -p "$project" -f "$repo_dir/backend/compose.yaml" -f "$repo_dir/backend/compose.https-security.yaml")
proxy_pid=
started=0
cleanup() {
  if [[ -n "$proxy_pid" ]]; then kill "$proxy_pid" 2>/dev/null || true; wait "$proxy_pid" 2>/dev/null || true; fi
  if [[ "$started" == 1 ]]; then "${compose[@]}" down -v --remove-orphans; fi
  rm -f -- "$cert_dir/key.pem" "$cert_dir/cert.pem" "$cert_dir/identity.pfx"
  rmdir -- "$cert_dir"
}
trap cleanup EXIT

MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj '/CN=localtest.me' \
  -addext 'subjectAltName=DNS:localtest.me,DNS:127.0.0.1.sslip.io' \
  -keyout "$SECURITY_CERT_DIR/key.pem" -out "$SECURITY_CERT_DIR/cert.pem" >/dev/null 2>&1
openssl pkcs12 -export -out "$cert_dir/identity.pfx" -inkey "$cert_dir/key.pem" \
  -in "$cert_dir/cert.pem" -passout pass:isolated-test-only
chmod a+rx "$cert_dir"
chmod a+r "$cert_dir/identity.pfx"
started=1
"${compose[@]}" up -d --build
node "$web_dir/security-e2e/proxy.cjs" &
proxy_pid=$!
ready=0
for _ in {1..90}; do
  if curl --noproxy '*' -fsSk https://localtest.me:18994/ >/dev/null 2>&1 && curl --noproxy '*' -fsSk https://localtest.me:18995/.well-known/openid-configuration >/dev/null 2>&1; then ready=1; break; fi
  if ! kill -0 "$proxy_pid" 2>/dev/null; then break; fi
  sleep 2
done
if [[ "$ready" != 1 ]]; then echo 'Isolated HTTPS stack did not become ready.' >&2; exit 1; fi
cd "$web_dir"
if [[ "${SECURITY_BROWSER_DOCKER:-0}" == 1 ]]; then
  docker_web_dir="$web_dir"
  if command -v cygpath >/dev/null 2>&1; then docker_web_dir="$(cygpath -m "$web_dir")"; fi
  MSYS_NO_PATHCONV=1 docker run --rm --add-host host.docker.internal:host-gateway \
    --add-host localtest.me:127.0.0.1 --add-host 127.0.0.1.sslip.io:127.0.0.1 \
    -e SECURITY_CERT_DIR=/cert -e SECURITY_UPSTREAM_HOST=host.docker.internal \
    -e E2E_PASSWORD -e WEB_BASE_URL -e IDENTITY_BASE_URL \
    -v "$docker_web_dir:/work" -v "$SECURITY_CERT_DIR:/cert:ro" -w /work \
    mcr.microsoft.com/playwright:v1.63.0-noble@sha256:eff16c30e6f3f4af0a03fa4b706120d5e9b0891c344a27d64559aff5900a4a27 \
    bash -c 'node security-e2e/proxy.cjs & proxy=$!; trap "kill $proxy 2>/dev/null || true" EXIT; npx playwright test --config playwright.security.config.ts'
else
  npx playwright test --config playwright.security.config.ts
fi
