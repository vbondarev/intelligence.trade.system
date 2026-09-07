#!/usr/bin/env bash
set -euo pipefail

identity_url="http://localhost:8081"
api_url="http://localhost:8080"
client_id="compose-smoke-client"
redirect_uri="http://client.test/callback"
username="compose-smoke-user"
password="Compose-smoke-password-123"
compose=(docker compose -f backend/compose.yaml --profile ci)
tmp_dir="$(mktemp -d)"

cleanup() {
  rm -f "$tmp_dir"/*
  rmdir "$tmp_dir"
}

trap cleanup EXIT

seeder_id=""
for _ in {1..30}; do
  seeder_id="$("${compose[@]}" ps -aq auth-test-seeder)"
  if [[ -n "$seeder_id" ]]; then
    seeder_status="$(docker inspect -f '{{.State.Status}}' "$seeder_id")"
    if [[ "$seeder_status" == "exited" ]]; then
      seeder_exit_code="$(docker inspect -f '{{.State.ExitCode}}' "$seeder_id")"
      [[ "$seeder_exit_code" == "0" ]] || {
        docker logs "$seeder_id"
        exit 1
      }
      break
    fi
  fi
  sleep 2
done
[[ -n "$seeder_id" ]]
[[ "$(docker inspect -f '{{.State.Status}}' "$seeder_id")" == "exited" ]]

extract_location() {
  awk 'BEGIN { IGNORECASE = 1 } /^Location:/ {
    sub(/\r$/, "")
    sub(/^[^:]+:[[:space:]]*/, "")
    print
    exit
  }' "$1"
}

request_without_redirect() {
  set +e
  curl --silent --show-error --max-redirs 0 "$@"
  local status=$?
  set -e
  if [[ "$status" -ne 0 && "$status" -ne 47 ]]; then
    return "$status"
  fi
}

to_identity_url() {
  case "$1" in
    http://localhost:8081/*)
      printf '%s' "$1"
      ;;
    /*)
      printf '%s%s' "$identity_url" "$1"
      ;;
    *)
      printf '%s' "$1"
      ;;
  esac
}

discovery=""
for _ in {1..30}; do
  if discovery="$(curl --fail --silent --show-error "$identity_url/.well-known/openid-configuration")"; then
    break
  fi
  sleep 2
done
[[ -n "$discovery" ]]
printf '%s' "$discovery" | jq -e \
  --arg issuer "$identity_url/" \
  '(.issuer == $issuer)
   and (.authorization_endpoint | startswith($issuer))
   and (.token_endpoint | startswith($issuer))
   and (.jwks_uri | startswith($issuer))
   and ((.scopes_supported | index("openid")) != null)
   and ((.scopes_supported | index("trade.api")) != null)
   and ((.code_challenge_methods_supported | index("S256")) != null)' >/dev/null

jwks_uri="$(printf '%s' "$discovery" | jq -er '.jwks_uri')"
jwks="$(curl --fail --silent --show-error "$jwks_uri")"
printf '%s' "$jwks" | jq -e '(.keys | length) > 0' >/dev/null

for _ in {1..30}; do
  if curl --fail --silent --show-error "$api_url/alive" >/dev/null; then
    break
  fi
  sleep 2
done
unauthenticated_status="$(curl --silent --show-error -o /dev/null -w '%{http_code}' "$api_url/api/v1/auth/me")"
[[ "$unauthenticated_status" == "401" ]]

verifier="$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=[:space:]')"
challenge="$(printf '%s' "$verifier" | openssl dgst -sha256 -binary | openssl base64 -A | tr '+/' '-_' | tr -d '=[:space:]')"
cookie_jar="$tmp_dir/cookies"

request_without_redirect \
  -D "$tmp_dir/authorize.headers" \
  -o /dev/null \
  -c "$cookie_jar" \
  -G "$identity_url/connect/authorize" \
  --data-urlencode "client_id=$client_id" \
  --data-urlencode "redirect_uri=$redirect_uri" \
  --data-urlencode "response_type=code" \
  --data-urlencode "scope=openid trade.api" \
  --data-urlencode "code_challenge=$challenge" \
  --data-urlencode "code_challenge_method=S256" \
  --data-urlencode "state=compose-smoke-state"

login_location="$(extract_location "$tmp_dir/authorize.headers")"
[[ -n "$login_location" ]]
curl --fail --silent --show-error -b "$cookie_jar" -c "$cookie_jar" "$login_location" \
  > "$tmp_dir/login.html"
antiforgery_token="$(sed -n 's/.*name="__RequestVerificationToken" value="\([^"]*\)".*/\1/p' "$tmp_dir/login.html" | head -n 1)"
[[ -n "$antiforgery_token" ]]
return_url="$(python3 -c 'from urllib.parse import parse_qs, urlsplit; import sys; print(parse_qs(urlsplit(sys.argv[1]).query)["returnUrl"][0])' "$login_location")"

request_without_redirect \
  -D "$tmp_dir/login.headers" \
  -o /dev/null \
  -b "$cookie_jar" \
  -c "$cookie_jar" \
  -X POST "$identity_url/account/login" \
  --data-urlencode "__RequestVerificationToken=$antiforgery_token" \
  --data-urlencode "returnUrl=$return_url" \
  --data-urlencode "username=$username" \
  --data-urlencode "password=$password"

authorization_location="$(extract_location "$tmp_dir/login.headers")"
[[ -n "$authorization_location" ]]
authorization_url="$(to_identity_url "$authorization_location")"
request_without_redirect \
  -D "$tmp_dir/completed.headers" \
  -o /dev/null \
  -b "$cookie_jar" \
  "$authorization_url"

callback_location="$(extract_location "$tmp_dir/completed.headers")"
[[ -n "$callback_location" ]]
code="$(python3 -c 'from urllib.parse import parse_qs, urlsplit; import sys; print(parse_qs(urlsplit(sys.argv[1]).query)["code"][0])' "$callback_location")"
state="$(python3 -c 'from urllib.parse import parse_qs, urlsplit; import sys; print(parse_qs(urlsplit(sys.argv[1]).query)["state"][0])' "$callback_location")"
[[ "$state" == "compose-smoke-state" ]]

token_response="$(curl --fail --silent --show-error -X POST "$identity_url/connect/token" \
  --data-urlencode "client_id=$client_id" \
  --data-urlencode "grant_type=authorization_code" \
  --data-urlencode "code=$code" \
  --data-urlencode "redirect_uri=$redirect_uri" \
  --data-urlencode "code_verifier=$verifier")"
access_token="$(printf '%s' "$token_response" | jq -er '.access_token')"

protected_response="$(curl --fail --silent --show-error \
  -H "Authorization: Bearer $access_token" \
  "$api_url/api/v1/auth/me")"
printf '%s' "$protected_response" | jq -e \
  '(.authenticated == true) and (.subject | length > 0)' >/dev/null
