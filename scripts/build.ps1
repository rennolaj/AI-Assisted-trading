# PowerShell script to build .NET solution
# Usage: .\build.ps1 [additional build args]

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$DotnetScript = Join-Path $ScriptDir "dotnet.ps1"
$SolutionPath = Join-Path $ScriptDir "..\Mvp.Trading.sln"
$BootstrapScript = Join-Path $ScriptDir "dev\bootstrap.ps1"

# Run bootstrap in dev mode (not in CI)
$CI = $env:CI
$DevBootstrap = if ($env:DEV_BOOTSTRAP) { $env:DEV_BOOTSTRAP } else { "1" }

if (-not $CI -and $DevBootstrap -eq "1") {
    if (Test-Path $BootstrapScript) {
        Write-Host "Running dev bootstrap..."
        & $BootstrapScript
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}

$env:MSBUILDDISABLENODEREUSE = "1"
& $DotnetScript build $SolutionPath --no-restore -m:1 @args

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
