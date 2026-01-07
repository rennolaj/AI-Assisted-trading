# PowerShell script to run all .NET tests
# Usage: .\test.ps1 [additional test args]

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$DotnetScript = Join-Path $ScriptDir "dotnet.ps1"

$TestProjects = @(
    "..\tests\Mvp.Trading.Contracts.Tests\Mvp.Trading.Contracts.Tests.csproj",
    "..\tests\Mvp.Trading.Api.Tests\Mvp.Trading.Api.Tests.csproj",
    "..\tests\Mvp.Trading.Indicators.Tests\Mvp.Trading.Indicators.Tests.csproj",
    "..\tests\Mvp.Trading.Elliott.Tests\Mvp.Trading.Elliott.Tests.csproj",
    "..\tests\Mvp.Trading.Execution.Tests\Mvp.Trading.Execution.Tests.csproj",
    "..\tests\Mvp.Trading.Integrations.Kraken.Tests\Mvp.Trading.Integrations.Kraken.Tests.csproj",
    "..\tests\Mvp.Trading.Risk.Tests\Mvp.Trading.Risk.Tests.csproj"
)

foreach ($project in $TestProjects) {
    $projectPath = Join-Path $ScriptDir $project
    Write-Host "Testing: $projectPath" -ForegroundColor Cyan
    
    & $DotnetScript test $projectPath --no-build @args
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed for: $projectPath"
        exit $LASTEXITCODE
    }
}

Write-Host "All tests passed!" -ForegroundColor Green
