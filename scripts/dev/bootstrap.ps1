# PowerShell script to bootstrap development environment on Windows
# Usage: .\bootstrap.ps1

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$DbName = if ($env:DB_NAME) { $env:DB_NAME } else { "ai-trading-db" }
$PgDefaultDb = if ($env:PG_DEFAULT_DB) { $env:PG_DEFAULT_DB } else { "postgres" }
$PgUser = if ($env:PG_USER) { $env:PG_USER } else { "postgres" }
$PgPassword = if ($env:PGPASSWORD) { $env:PGPASSWORD } else { "postgres" }

Write-Host "Bootstrapping development environment..." -ForegroundColor Cyan

# Check if running in Docker or CI (skip bootstrap)
if ($env:CI) {
    Write-Host "CI environment detected, skipping bootstrap." -ForegroundColor Yellow
    exit 0
}

# Check for PostgreSQL
$PsqlPath = $null
$CreateDbPath = $null

# Try to find PostgreSQL in common Windows installation paths
$PgPaths = @(
    "C:\Program Files\PostgreSQL\16\bin",
    "C:\Program Files\PostgreSQL\15\bin",
    "C:\Program Files\PostgreSQL\14\bin",
    "$env:ProgramFiles\PostgreSQL\16\bin",
    "$env:ProgramFiles\PostgreSQL\15\bin",
    "$env:ProgramFiles\PostgreSQL\14\bin"
)

foreach ($path in $PgPaths) {
    $testPsql = Join-Path $path "psql.exe"
    $testCreateDb = Join-Path $path "createdb.exe"
    if ((Test-Path $testPsql) -and (Test-Path $testCreateDb)) {
        $PsqlPath = $testPsql
        $CreateDbPath = $testCreateDb
        break
    }
}

# Try to find in PATH
if (-not $PsqlPath) {
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    $createDbCmd = Get-Command createdb -ErrorAction SilentlyContinue
    if ($psqlCmd -and $createDbCmd) {
        $PsqlPath = $psqlCmd.Source
        $CreateDbPath = $createDbCmd.Source
    }
}

if (-not $PsqlPath) {
    Write-Warning @"
PostgreSQL not found. Please install PostgreSQL 14+ from:
https://www.postgresql.org/download/windows/

Or use Docker:
docker compose up -d postgres redis

Alternatively, install using Chocolatey:
choco install postgresql

"@
    exit 1
}

Write-Host "Found PostgreSQL at: $PsqlPath" -ForegroundColor Green

# Check for Redis (warn only, not critical for build)
$RedisRunning = $false
try {
    $RedisTest = Test-NetConnection -ComputerName localhost -Port 6379 -WarningAction SilentlyContinue -ErrorAction SilentlyContinue
    if ($RedisTest.TcpTestSucceeded) {
        $RedisRunning = $true
        Write-Host "Redis is running on localhost:6379" -ForegroundColor Green
    }
} catch {
    # Redis not running
}

if (-not $RedisRunning) {
    Write-Warning @"
Redis not detected. Install Redis for Windows:
https://github.com/microsoftarchive/redis/releases

Or use Docker:
docker compose up -d redis

Alternatively, install using Chocolatey:
choco install redis-64

"@
}

# Set PostgreSQL password environment variable
$env:PGPASSWORD = $PgPassword

# Wait for PostgreSQL to be ready
Write-Host "Waiting for PostgreSQL to be ready..." -ForegroundColor Cyan
$maxRetries = 10
$retryCount = 0
$pgReady = $false

while ($retryCount -lt $maxRetries) {
    try {
        $result = & $PsqlPath -h localhost -U $PgUser -d $PgDefaultDb -tAc "select 1" 2>$null
        if ($result -eq "1") {
            $pgReady = $true
            break
        }
    } catch {
        # Continue retrying
    }
    
    $retryCount++
    Start-Sleep -Seconds 1
}

if (-not $pgReady) {
    Write-Error @"
PostgreSQL is not ready. Ensure PostgreSQL service is running:
- Check Services (services.msc) for 'postgresql-x64-16' or similar
- Or run: net start postgresql-x64-16

Connection details:
Host: localhost
User: $PgUser
Database: $PgDefaultDb
"@
    exit 1
}

Write-Host "PostgreSQL is ready!" -ForegroundColor Green

# Check if database exists
Write-Host "Checking if database '$DbName' exists..." -ForegroundColor Cyan
$dbExists = & $PsqlPath -h localhost -U $PgUser -d $PgDefaultDb -tAc "select 1 from pg_database where datname='$DbName';" 2>$null
$dbExists = ($dbExists -replace '\s', '')

if ($dbExists -ne "1") {
    Write-Host "Creating database '$DbName'..." -ForegroundColor Yellow
    & $CreateDbPath -h localhost -U $PgUser $DbName
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create database '$DbName'"
        exit 1
    }
    
    Write-Host "Database created successfully!" -ForegroundColor Green
} else {
    Write-Host "Database '$DbName' already exists." -ForegroundColor Green
}

# Run init.sql script
$InitSqlPath = Join-Path $ScriptDir "..\db\init.sql"
if (Test-Path $InitSqlPath) {
    Write-Host "Running database initialization script..." -ForegroundColor Cyan
    & $PsqlPath -h localhost -U $PgUser -d $DbName -f $InitSqlPath > $null
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to run init.sql script"
        exit 1
    }
    
    Write-Host "Database initialized successfully!" -ForegroundColor Green
} else {
    Write-Warning "init.sql not found at: $InitSqlPath"
}

Write-Host "Bootstrap completed successfully!" -ForegroundColor Green
