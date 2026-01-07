#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
"$SCRIPT_DIR/dotnet.sh" test "$SCRIPT_DIR/../tests/Mvp.Trading.Contracts.Tests/Mvp.Trading.Contracts.Tests.csproj" --no-build "$@"
"$SCRIPT_DIR/dotnet.sh" test "$SCRIPT_DIR/../tests/Mvp.Trading.Api.Tests/Mvp.Trading.Api.Tests.csproj" --no-build "$@"
"$SCRIPT_DIR/dotnet.sh" test "$SCRIPT_DIR/../tests/Mvp.Trading.Indicators.Tests/Mvp.Trading.Indicators.Tests.csproj" --no-build "$@"
"$SCRIPT_DIR/dotnet.sh" test "$SCRIPT_DIR/../tests/Mvp.Trading.Elliott.Tests/Mvp.Trading.Elliott.Tests.csproj" --no-build "$@"
"$SCRIPT_DIR/dotnet.sh" test "$SCRIPT_DIR/../tests/Mvp.Trading.Execution.Tests/Mvp.Trading.Execution.Tests.csproj" --no-build "$@"
"$SCRIPT_DIR/dotnet.sh" test "$SCRIPT_DIR/../tests/Mvp.Trading.Integrations.Kraken.Tests/Mvp.Trading.Integrations.Kraken.Tests.csproj" --no-build "$@"
"$SCRIPT_DIR/dotnet.sh" test "$SCRIPT_DIR/../tests/Mvp.Trading.Risk.Tests/Mvp.Trading.Risk.Tests.csproj" --no-build "$@"
