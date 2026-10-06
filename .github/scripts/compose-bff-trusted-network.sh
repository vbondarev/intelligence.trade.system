#!/usr/bin/env bash
# Проверяет trust boundary forwarded headers BFF в Compose stack:
# - trade-agent-network принадлежит stack и имеет явно зафиксированный subnet, а не адрес из address pool
#   Docker daemon;
# - Bff:ForwardedHeaders:KnownNetworks содержит ровно этот subnet и не шире него;
# - frontend и BFF подключены к этой network;
# - с флагом --running network запущенного stack действительно создана с этим subnet, а frontend и BFF
#   получили адреса из него.
# Конфигурацию разбирает сам Docker Compose без interpolation, поэтому secrets не требуются и не выводятся.
set -euo pipefail

compose=(docker compose -f backend/compose.yaml)
network="trade-agent-network"
expected_subnet="172.28.0.0/24"
known_networks_prefix="Bff__ForwardedHeaders__KnownNetworks__"

fail() {
  echo "$1" >&2
  exit 1
}

config="$("${compose[@]}" config --no-interpolate --format json)"

if jq -e --arg network "$network" '.networks[$network].external == true' >/dev/null <<< "$config"; then
  fail "networks.$network не должна быть external: subnet external network не задаётся этим stack."
fi

actual_subnets="$(jq -r --arg network "$network" \
  '(.networks[$network].ipam.config // []) | map(.subnet // "") | join(",")' <<< "$config")"
[[ "$actual_subnets" == "$expected_subnet" ]] \
  || fail "networks.$network.ipam.config должен содержать ровно subnet $expected_subnet, получено: '$actual_subnets'."
echo "ok: $network subnet = $expected_subnet"

known_networks="$(jq -r --arg prefix "$known_networks_prefix" '
  (.services.bff.environment // {})
  | if type == "array" then map(capture("^(?<key>[^=]+)=(?<value>.*)$")) | from_entries else . end
  | to_entries
  | map(select(.key | startswith($prefix)) | .value)
  | join(",")' <<< "$config")"
[[ "$known_networks" == "$expected_subnet" ]] \
  || fail "BFF ${known_networks_prefix}* должен содержать только subnet $network ($expected_subnet), получено: '$known_networks'."
echo "ok: BFF KnownNetworks = $expected_subnet"

for service in frontend bff; do
  jq -e --arg service "$service" --arg network "$network" \
    '.services[$service].networks | has($network)' >/dev/null <<< "$config" \
    || fail "Service $service должен быть подключён к $network."
done
echo "ok: frontend и BFF подключены к $network"

[[ "${1:-}" == "--running" ]] || exit 0

running_subnets="$(docker network inspect "$network" --format '{{range .IPAM.Config}}{{.Subnet}} {{end}}')"
running_subnets="${running_subnets% }"
[[ "$running_subnets" == "$expected_subnet" ]] \
  || fail "Network $network создана с subnet '$running_subnets' вместо $expected_subnet; её нужно пересоздать (см. docs/local-development.md)."
echo "ok: running $network subnet = $expected_subnet"

for service in frontend bff; do
  container_id="$("${compose[@]}" ps -q "$service")"
  [[ -n "$container_id" ]] || fail "Service $service не запущен."
  address="$(docker inspect "$container_id" \
    --format "{{with index .NetworkSettings.Networks \"$network\"}}{{.IPAddress}}{{end}}")"
  python3 -c 'import ipaddress, sys; sys.exit(0 if ipaddress.ip_address(sys.argv[1]) in ipaddress.ip_network(sys.argv[2]) else 1)' \
    "$address" "$expected_subnet" \
    || fail "Service $service получил адрес '$address' вне $expected_subnet."
  echo "ok: $service address $address ∈ $expected_subnet"
done
