#!/usr/bin/env bash
set -euo pipefail

export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1
export DOTNET_NO_WORKLOAD_UPDATE_NOTIFY=1

DOTNET_DEFAULT="/usr/local/share/dotnet/dotnet"

if [[ -n "${DOTNET_ROOT:-}" && -x "${DOTNET_ROOT}/dotnet" ]]; then
  DOTNET="${DOTNET_ROOT}/dotnet"
elif [[ -x "$DOTNET_DEFAULT" ]]; then
  DOTNET="$DOTNET_DEFAULT"
elif command -v dotnet >/dev/null 2>&1; then
  DOTNET="$(command -v dotnet)"
else
  echo "dotnet not found. Install .NET 10 or set DOTNET to a valid path." >&2
  exit 1
fi

exec "$DOTNET" "$@"
