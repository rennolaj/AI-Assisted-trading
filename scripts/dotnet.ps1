# PowerShell wrapper for dotnet CLI with environment configuration
# Usage: .\dotnet.ps1 [dotnet commands and args]

$ErrorActionPreference = "Stop"

# Disable workload update notifications
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = "1"
$env:DOTNET_NO_WORKLOAD_UPDATE_NOTIFY = "1"

# Find dotnet executable
$DotnetExe = $null

# Check DOTNET_ROOT environment variable
if ($env:DOTNET_ROOT) {
    $DotnetPath = Join-Path $env:DOTNET_ROOT "dotnet.exe"
    if (Test-Path $DotnetPath) {
        $DotnetExe = $DotnetPath
    }
}

# Check default Windows installation paths
if (-not $DotnetExe) {
    $DefaultPaths = @(
        "C:\Program Files\dotnet\dotnet.exe",
        "$env:ProgramFiles\dotnet\dotnet.exe",
        "${env:ProgramFiles(x86)}\dotnet\dotnet.exe"
    )
    
    foreach ($path in $DefaultPaths) {
        if (Test-Path $path) {
            $DotnetExe = $path
            break
        }
    }
}

# Try to find dotnet in PATH
if (-not $DotnetExe) {
    $DotnetInPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($DotnetInPath) {
        $DotnetExe = $DotnetInPath.Source
    }
}

# Fail if dotnet not found
if (-not $DotnetExe) {
    Write-Error "dotnet not found. Install .NET 10 or set DOTNET_ROOT environment variable."
    exit 1
}

# Execute dotnet with all arguments
& $DotnetExe @args
exit $LASTEXITCODE
