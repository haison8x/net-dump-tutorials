#!/usr/bin/env bash
# Collects a dump from a lab container (docker compose service) into ./dumps.
#
#   ./scripts/collect-dump.sh memoryleak leak-event-1 [Full|Heap|Mini|Triage]
set -euo pipefail

service="${1:?usage: collect-dump.sh <service> [name] [type]}"
name="${2:-$service-$(date +%Y%m%d-%H%M%S)}"
type="${3:-Full}"

cd "$(dirname "$0")/.."
# The app is PID 1 inside its container (see docker/app.Dockerfile).
MSYS_NO_PATHCONV=1 docker compose exec "$service" dotnet-dump collect --process-id 1 --type "$type" --output "/dumps/$name.dmp"
echo "Dump written to ./dumps/$name.dmp"
