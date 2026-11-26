#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_dir/frontend/intelligence-trade-web"
project=browser-e2e-webkit
base_file=backend/compose.yaml
overlay_file=backend/compose.browser-e2e-webkit.yaml
primary=(docker compose -p backend -f "$repo_dir/$base_file" --profile ci)
webkit=(docker compose -p "$project" -f "$repo_dir/$base_file" -f "$repo_dir/$overlay_file" --profile ci)
results="$(mktemp -d)"
owns_stack=0
chromium_pid=
webkit_pid=

stamp() {
  printf '%s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%S.%NZ)" "$*"
}

cleanup() {
  local status=$? pid
  trap - EXIT
  trap '' INT TERM
  for pid in "$chromium_pid" "$webkit_pid"; do
    if [[ -n "$pid" ]]; then kill -TERM -- "-$pid" 2>/dev/null || true; fi
  done
  for pid in "$chromium_pid" "$webkit_pid"; do
    if [[ -n "$pid" ]]; then wait "$pid" || true; fi
  done
  if (( owns_stack )); then
    stamp "$project cleanup start"
    if ! "${webkit[@]}" down -v; then
      echo "::error::$project cleanup failed"
      status=1
    fi
    stamp "$project cleanup end"
    if [[ -n "${CONTROL_ACCOUNT:-}" ]]; then
      if curl --max-time 3 -fsS -o /dev/null http://localhost:8082/bff/auth/session &&
        [[ "$("${primary[@]}" exec -T postgres psql -U tradesystem -d tradesystem -Atc \
          "SELECT count(*) FROM exchange_accounts WHERE id = '$CONTROL_ACCOUNT'")" == 1 ]]; then
        echo 'Independent cleanup: backend HTTP available and control row present'
      else
        echo 'Independent cleanup verification failed' >&2
        status=1
      fi
    fi
  fi
  rm -rf -- "$results"
  exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

containers="$(docker ps -aq --filter "label=com.docker.compose.project=$project")"
networks="$(docker network ls -q --filter "label=com.docker.compose.project=$project")"
volumes="$(docker volume ls -q --filter "label=com.docker.compose.project=$project")"
if [[ -n "$containers$networks$volumes" ]] ||
    docker network inspect "$project-network" >/dev/null 2>&1 ||
    docker volume inspect "${project}_trade-system-postgres-data" >/dev/null 2>&1; then
  echo "::error::$project Docker resources already exist"
  exit 1
fi

primary_name="$(docker compose -f "$repo_dir/$base_file" config --format json | jq -r .name)"
if [[ "$primary_name" != backend ]]; then
  echo "::error::Expected primary Compose project backend, got $primary_name"
  exit 1
fi

services=(postgres identity-db-init identity-migrations identity api bff frontend auth-test-seeder)
docker image inspect --format '{{.Id}}' postgres:16-alpine backend-realtime-e2e-driver:latest >/dev/null
for service in identity-migrations identity api bff frontend auth-test-seeder; do
  docker image inspect --format '{{.Id}}' "backend-$service:latest" >/dev/null
done

stamp "$project up start"
owns_stack=1
timeout 180s "${webkit[@]}" up -d --no-build --pull never "${services[@]}"
for service in "${services[@]}"; do
  primary_id="$("${primary[@]}" ps -aq "$service")"
  webkit_id="$("${webkit[@]}" ps -aq "$service")"
  if [[ -z "$primary_id" || -z "$webkit_id" ]] ||
      [[ "$(docker inspect --format '{{.Image}}' "$primary_id")" != "$(docker inspect --format '{{.Image}}' "$webkit_id")" ]]; then
    echo "::error::$project image mismatch for $service"
    exit 1
  fi
done

ready=0
for _ in {1..60}; do
  seed_id="$("${webkit[@]}" ps -aq auth-test-seeder)"
  seed_state="$(docker inspect --format '{{.State.Status}} {{.State.ExitCode}}' "$seed_id")"
  if [[ "$seed_state" == exited\ * && "$seed_state" != 'exited 0' ]]; then
    echo "::error::$project auth-test-seeder failed"
    exit 1
  fi
  if [[ "$seed_state" == 'exited 0' ]] &&
      [[ "$(docker inspect --format '{{.State.Health.Status}}' "$("${webkit[@]}" ps -q frontend)")" == healthy ]] &&
      "${webkit[@]}" exec -T frontend wget -q -O /dev/null http://bff:8080/healthz &&
      curl --max-time 3 -fsS -o /dev/null http://localhost:18092/bff/auth/session &&
      curl --max-time 3 -fsS http://localhost:18091/.well-known/openid-configuration |
        jq -e '.issuer == "http://localhost:18091/"' >/dev/null; then
    ready=1
    break
  fi
  sleep 2
done
if (( ! ready )); then
  echo "::error::$project readiness timed out"
  exit 1
fi
for service in identity-db-init identity-migrations; do
  id="$("${webkit[@]}" ps -aq "$service")"
  if [[ "$(docker inspect --format '{{.State.Status}} {{.State.ExitCode}}' "$id")" != 'exited 0' ]]; then
    echo "::error::$project $service did not complete successfully"
    exit 1
  fi
done
stamp "$project ready"
export CONTROL_USER="$(cat /proc/sys/kernel/random/uuid)"
export CONTROL_ACCOUNT="$(cat /proc/sys/kernel/random/uuid)"
webkit_account="$(cat /proc/sys/kernel/random/uuid)"
for side in backend browser-e2e-webkit; do
  if [[ "$side" == backend ]]; then ctl=("${primary[@]}"); account=$CONTROL_ACCOUNT
  else ctl=("${webkit[@]}"); account=$webkit_account; fi
  "${ctl[@]}" run --rm --no-deps -e REALTIME_E2E_USER_ID="$CONTROL_USER" \
    -e REALTIME_E2E_ACCOUNT_ID="$account" -e REALTIME_E2E_NAME='G07 initial' realtime-e2e-driver
  "${ctl[@]}" config --format json |
    jq -c '{name, networks: [.networks[].name], volumes: [.volumes[].name],
      frontendPorts: [.services.frontend.ports[].published], identityPorts: [.services.identity.ports[].published]}'
done

run_browser() {
  local browser=$1
  shift
  setsid env "$@" bash -c '
    browser=$1
    dir=$2
    printf "%s %s start\n" "$(date -u +%Y-%m-%dT%H:%M:%S.%NZ)" "$browser"
    code=0
    PLAYWRIGHT_JSON_OUTPUT_FILE="$dir/$browser.json" npm run test:e2e -- \
      --project="$browser" --workers=1 --output="$dir/$browser" --reporter=list,json || code=$?
    printf "%s %s end exit=%s\n" "$(date -u +%Y-%m-%dT%H:%M:%S.%NZ)" "$browser" "$code"
    exit "$code"
  ' bash "$browser" "$results" &
}

run_browser chromium WEB_BASE_URL=http://localhost:8082 IDENTITY_BASE_URL=http://localhost:8081 \
  REALTIME_E2E_COMPOSE_PROJECT=backend REALTIME_E2E_COMPOSE_FILE="$base_file" \
  REALTIME_E2E_COMPOSE_OVERRIDE_FILE=
chromium_pid=$!

run_browser mobile-webkit WEB_BASE_URL=http://localhost:18092 IDENTITY_BASE_URL=http://localhost:18091 \
  REALTIME_E2E_COMPOSE_PROJECT="$project" REALTIME_E2E_COMPOSE_FILE="$base_file" \
  REALTIME_E2E_COMPOSE_OVERRIDE_FILE="$overlay_file"
webkit_pid=$!

status=0
if ! wait "$chromium_pid"; then echo "::error::chromium failed"; status=1; fi
chromium_pid=
if ! wait "$webkit_pid"; then echo "::error::mobile-webkit failed"; status=1; fi
webkit_pid=
for browser in chromium mobile-webkit; do
  if ! jq -c '[.suites[] | recurse(.suites[]?) | .specs[]? | . as $spec | .tests[] |
      {id: $spec.id, project: .projectName, status, results: [.results[] | {status, duration}]}]' \
      "$results/$browser.json"; then
    echo "::error::$browser results unavailable"
    status=1
  fi
done
exit "$status"
