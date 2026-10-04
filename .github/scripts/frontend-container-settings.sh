#!/usr/bin/env bash
# Проверяет deployment settings standalone frontend image: BFF_UPSTREAM и PUBLIC_SCHEME обязательны,
# некорректные значения останавливают container до генерации nginx config, а envsubst подставляет
# только эти две переменные и не затрагивает переменные nginx.
set -euo pipefail

image="${1:-intelligence-trade-web:ci}"
# IP-адрес не требует DNS: nginx стартует без доступного BFF.
upstream="http://127.0.0.1:9"
containers=()
started=""

cleanup() {
  if (( ${#containers[@]} > 0 )); then
    docker rm -f "${containers[@]}" >/dev/null 2>&1 || true
  fi
}

trap cleanup EXIT

start_container() {
  started="$(docker run -d "$@" "$image")"
  containers+=("$started")
}

expect_startup_failure() {
  local description="$1"
  local expected_message="$2"
  shift 2

  start_container "$@"
  local container="$started"
  for _ in {1..30}; do
    [[ "$(docker inspect -f '{{.State.Status}}' "$container")" == "exited" ]] && break
    sleep 1
  done

  local status exit_code logs
  status="$(docker inspect -f '{{.State.Status}}' "$container")"
  exit_code="$(docker inspect -f '{{.State.ExitCode}}' "$container")"
  logs="$(docker logs "$container" 2>&1)"
  if [[ "$status" != "exited" || "$exit_code" == "0" ]] \
    || ! grep --fixed-strings --quiet "$expected_message" <<< "$logs"; then
    echo "Frontend container должен завершиться с ошибкой: $description (status=$status, exit=$exit_code)." >&2
    printf '%s\n' "$logs" >&2
    exit 1
  fi
  echo "ok: $description"
}

expect_startup_success() {
  local scheme="$1"

  start_container -e "BFF_UPSTREAM=$upstream" -e "PUBLIC_SCHEME=$scheme"
  local container="$started"
  local ready=false
  for _ in {1..30}; do
    if docker exec "$container" wget -q -O /dev/null http://127.0.0.1:8080/index.html 2>/dev/null; then
      ready=true
      break
    fi
    sleep 1
  done
  if [[ "$ready" != true ]]; then
    echo "Frontend container с PUBLIC_SCHEME=$scheme не стал доступен." >&2
    docker logs "$container" >&2 || true
    exit 1
  fi

  local config
  config="$(docker exec "$container" cat /etc/nginx/conf.d/default.conf)"
  grep --fixed-strings --quiet "proxy_set_header X-Forwarded-Proto $scheme;" <<< "$config"
  grep --fixed-strings --quiet "proxy_pass $upstream;" <<< "$config"
  grep --fixed-strings --quiet 'proxy_set_header X-Forwarded-Host $http_host;' <<< "$config"
  grep --fixed-strings --quiet 'proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;' <<< "$config"
  if grep --quiet -e '\${' -e 'http_x_forwarded_proto' <<< "$config"; then
    echo "nginx config содержит неподставленную переменную или пересылает client X-Forwarded-Proto." >&2
    exit 1
  fi
  echo "ok: PUBLIC_SCHEME=$scheme"
}

expect_startup_failure "BFF_UPSTREAM отсутствует" "BFF_UPSTREAM" -e PUBLIC_SCHEME=http
expect_startup_failure "PUBLIC_SCHEME отсутствует" "PUBLIC_SCHEME" -e "BFF_UPSTREAM=$upstream"
expect_startup_failure "PUBLIC_SCHEME пустой" "PUBLIC_SCHEME" -e "BFF_UPSTREAM=$upstream" -e PUBLIC_SCHEME=
expect_startup_failure "PUBLIC_SCHEME=ftp" "PUBLIC_SCHEME" -e "BFF_UPSTREAM=$upstream" -e PUBLIC_SCHEME=ftp
expect_startup_failure "PUBLIC_SCHEME=HTTPS" "PUBLIC_SCHEME" -e "BFF_UPSTREAM=$upstream" -e PUBLIC_SCHEME=HTTPS
expect_startup_success http
expect_startup_success https
