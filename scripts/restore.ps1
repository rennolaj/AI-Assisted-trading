# PowerShell script to restore .NET dependencies
# Usage: .\restore.ps1 [additional restore args]

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$DotnetScript = Join-Path $ScriptDir "dotnet.ps1"
$SolutionPath = Join-Path $ScriptDir "..\Mvp.Trading.sln"

& $DotnetScript restore $SolutionPath @args

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
