#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ -z "${CI:-}" && "${DEV_BOOTSTRAP:-1}" == "1" ]]; then
  "$SCRIPT_DIR/dev/bootstrap.sh"
fi
MSBUILDDISABLENODEREUSE=1 "$SCRIPT_DIR/dotnet.sh" build "$SCRIPT_DIR/../Mvp.Trading.sln" --no-restore -m:1 "$@"
