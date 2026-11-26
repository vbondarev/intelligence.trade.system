#!/usr/bin/env bash
set -euo pipefail
if [[ "$GITHUB_EVENT_NAME" != push || "$GITHUB_REF" != refs/heads/validation/259-browser-e2e ]]; then
  echo 'Unexpected browser benchmark event/ref' >&2
  exit 1
fi
case "$GITHUB_RUN_ATTEMPT" in
  1|2|5|6|9|10) mode=S ;;
  3|4|7|8|11) mode=P ;;
  *) echo 'Unsupported browser benchmark attempt' >&2; exit 1 ;;
esac
printf 'Benchmark attempt=%s mode=%s ref=%s checkout=%s runner=%s image=%s\n' \
  "$GITHUB_RUN_ATTEMPT" "$mode" "$GITHUB_REF" "$(git rev-parse HEAD)" "$RUNNER_ARCH" "${ImageVersion:-unknown}"
docker version --format '{{.Client.Version}} {{.Server.Version}}'
docker compose version
docker buildx version
node --version
docker image inspect --format '{{index .RepoDigests 0}}' postgres:16-alpine
(cd frontend/intelligence-trade-web && npx --no-install playwright --version)
if [[ "$mode" == P ]]; then exec bash .github/scripts/browser-e2e.sh; fi
cd frontend/intelligence-trade-web
results="$(mktemp -d)"
trap 'rm -rf -- "$results"' EXIT
export WEB_BASE_URL=http://localhost:8082 IDENTITY_BASE_URL=http://localhost:8081
export REALTIME_E2E_COMPOSE_PROJECT=backend PLAYWRIGHT_JSON_OUTPUT_FILE="$results/serial.json"
printf '%s serial start\n' "$(date -u +%Y-%m-%dT%H:%M:%S.%NZ)"
code=0
npm run test:e2e -- --output="$results/tests" --reporter=list,json || code=$?
printf '%s serial end exit=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%S.%NZ)" "$code"
jq -c '[.suites[] | recurse(.suites[]?) | .specs[]? | . as $spec | .tests[] |
  {id: $spec.id, project: .projectName, status, results: [.results[] | {status, duration}]}]' "$results/serial.json"
exit "$code"
