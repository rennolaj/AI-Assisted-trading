# Windows PowerShell Scripts

This directory contains PowerShell equivalents of all bash scripts needed for building and testing the MVP Trading system on Windows.

## Prerequisites

### Required Software

1. **.NET 10 SDK**
   - Download from: https://dotnet.microsoft.com/download/dotnet/10.0
   - Or install via Chocolatey: `choco install dotnet-sdk`

2. **PowerShell 5.1 or higher**
   - Built into Windows 10/11
   - For PowerShell Core 7+: https://github.com/PowerShell/PowerShell

3. **PostgreSQL 14+** (for local development)
   - Download from: https://www.postgresql.org/download/windows/
   - Or via Chocolatey: `choco install postgresql`
   - Or use Docker: `docker compose up -d postgres`

4. **Redis** (for local development)
   - Download from: https://github.com/microsoftarchive/redis/releases
   - Or via Chocolatey: `choco install redis-64`
   - Or use Docker: `docker compose up -d redis`

## Available Scripts

### Core Build Scripts

#### `dotnet.ps1`
PowerShell wrapper for the .NET CLI with environment configuration.

```powershell
# Examples:
.\scripts\dotnet.ps1 --version
.\scripts\dotnet.ps1 restore
.\scripts\dotnet.ps1 build
.\scripts\dotnet.ps1 test
```

**Features:**
- Disables workload update notifications
- Searches for dotnet.exe in standard locations:
  - `$env:DOTNET_ROOT`
  - `C:\Program Files\dotnet`
  - `%PATH%`

#### `restore.ps1`
Restores all NuGet package dependencies for the solution.

```powershell
.\scripts\restore.ps1

# With additional arguments:
.\scripts\restore.ps1 --force
```

**Equivalent to:**
```bash
./scripts/restore.sh
```

#### `build.ps1`
Builds the entire solution with proper configuration.

```powershell
.\scripts\build.ps1

# With additional arguments:
.\scripts\build.ps1 -c Debug
.\scripts\build.ps1 --no-incremental
```

**Features:**
- Automatically runs `dev\bootstrap.ps1` in dev mode (unless `$env:CI` is set)
- Disables MSBuild node reuse for clean builds
- Uses single-threaded build (`-m:1`) for consistency

**Equivalent to:**
```bash
./scripts/build.sh
```

#### `test.ps1`
Runs all unit tests across all test projects.

```powershell
.\scripts\test.ps1

# With additional arguments:
.\scripts\test.ps1 --verbosity detailed
.\scripts\test.ps1 --filter "FullyQualifiedName~Mvp.Trading.Api"
```

**Test Projects:**
- `Mvp.Trading.Contracts.Tests`
- `Mvp.Trading.Api.Tests`
- `Mvp.Trading.Indicators.Tests`
- `Mvp.Trading.Elliott.Tests`
- `Mvp.Trading.Execution.Tests`
- `Mvp.Trading.Integrations.Kraken.Tests`
- `Mvp.Trading.Risk.Tests`

**Equivalent to:**
```bash
./scripts/test.sh
```

### Development Environment Scripts

#### `dev\bootstrap.ps1`
Sets up the local development environment on Windows.

```powershell
.\scripts\dev\bootstrap.ps1
```

**What it does:**
1. Checks for PostgreSQL installation
2. Checks for Redis (warns if not found)
3. Waits for PostgreSQL to be ready
4. Creates the `ai-trading-db` database if it doesn't exist
5. Runs `scripts\db\init.sql` to initialize schema

**Environment Variables:**
- `DB_NAME`: Database name (default: `ai-trading-db`)
- `PG_DEFAULT_DB`: Default PostgreSQL database (default: `postgres`)
- `PG_USER`: PostgreSQL user (default: `postgres`)
- `PGPASSWORD`: PostgreSQL password (default: `postgres`)

**Equivalent to:**
```bash
./scripts/dev/bootstrap.sh
```

## Docker Build Support

The Dockerfiles have been updated to support both bash (Linux) and PowerShell scripts with smart detection. When building in Docker:

1. **Linux containers** (default): Use bash scripts
2. **Windows containers**: Automatically detect and use PowerShell
   - Tries PowerShell Core (`pwsh`) first
   - Falls back to Windows PowerShell (`powershell`) if needed

### PowerShell Detection Strategy

The Dockerfiles use a three-tier fallback:
```dockerfile
# Try bash first (Linux)
RUN ./scripts/restore.sh || \
    # Try PowerShell Core (modern)
    (pwsh -File ./scripts/restore.ps1 2>/dev/null || \
     # Fall back to Windows PowerShell (legacy)
     powershell -File ./scripts/restore.ps1)
```

This ensures compatibility with:
- **Linux containers**: Use bash (standard)
- **Windows Server Core**: Use Windows PowerShell 5.1
- **Windows containers with PowerShell Core**: Use pwsh 7+
- **Command line builds**: Works from both CMD and PowerShell

### Building Docker Images on Windows

```powershell
# Build the API image
docker build -t mvp-trading-api:latest -f Dockerfile .

# Build the Worker image
docker build -t mvp-trading-worker:latest -f Dockerfile.worker .

# Build with Docker Compose
docker compose build

# Build and run
docker compose up --build
```

The Dockerfiles will automatically detect the platform and use the appropriate scripts.

## Typical Development Workflow

### Initial Setup

```powershell
# 1. Clone the repository
git clone <repository-url>
cd AI-Assisted

# 2. Bootstrap the development environment
.\scripts\dev\bootstrap.ps1

# 3. Restore dependencies
.\scripts\restore.ps1

# 4. Build the solution
.\scripts\build.ps1

# 5. Run tests
.\scripts\test.ps1
```

### Day-to-Day Development

```powershell
# Quick build and test cycle
.\scripts\build.ps1
.\scripts\test.ps1

# Or use Docker Compose
docker compose up --build

# Watch logs
docker compose logs -f api worker
```

### Running Individual Components

```powershell
# Run the API locally
.\scripts\dotnet.ps1 run --project src\Mvp.Trading.Api\Mvp.Trading.Api.csproj

# Run the Worker locally
.\scripts\dotnet.ps1 run --project src\Mvp.Trading.Worker\Mvp.Trading.Worker.csproj

# Run specific tests
.\scripts\dotnet.ps1 test tests\Mvp.Trading.Api.Tests\Mvp.Trading.Api.Tests.csproj
```

## Troubleshooting

### PostgreSQL Not Found

**Error:**
```
PostgreSQL not found. Please install PostgreSQL 14+ from:
https://www.postgresql.org/download/windows/
```

**Solutions:**
1. Install PostgreSQL from the official website
2. Use Chocolatey: `choco install postgresql`
3. Use Docker: `docker compose up -d postgres`
4. Set `PGBIN` environment variable to PostgreSQL bin directory

### Redis Not Found

**Warning:**
```
Redis not detected. Install Redis for Windows:
https://github.com/microsoftarchive/redis/releases
```

**Solutions:**
1. Install Redis from the GitHub releases
2. Use Chocolatey: `choco install redis-64`
3. Use Docker: `docker compose up -d redis`
4. Redis is optional for building; required only for runtime

### .NET Not Found

**Error:**
```
dotnet not found. Install .NET 10 or set DOTNET_ROOT environment variable.
```

**Solutions:**
1. Install .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0
2. Set `DOTNET_ROOT` environment variable to .NET installation directory
3. Ensure .NET is in your `PATH`

### Build Fails in Docker

**Issue:** Docker build fails with "script not found" or `bash\r: No such file or directory` error.

**Root Cause:** Windows line endings (CRLF) in shell scripts instead of Unix line endings (LF).

**Solutions:**

1. **Automatic Fix (Recommended):** The Dockerfiles now automatically convert line endings:
   ```dockerfile
   # This happens automatically during Docker build
   RUN find scripts -name "*.sh" -type f -exec sed -i 's/\r$//' {} \;
   ```

2. **Fix Git configuration** (prevents future issues):
   ```powershell
   # Configure Git to checkout with LF line endings
   git config core.autocrlf false
   
   # Re-checkout all files with correct line endings
   git rm --cached -r .
   git reset --hard
   ```

3. **Manual verification:**
   ```powershell
   # Check if files have CRLF (bad)
   git ls-files --eol | Select-String "crlf"
   
   # Should show: i/lf for .sh files (input uses LF)
   git ls-files --eol | Select-String ".sh"
   ```

4. **Rebuild with no cache:**
   ```powershell
   docker compose build --no-cache
   ```

**Note:** The repository includes `.gitattributes` that enforces:
- Shell scripts (`.sh`): Always use LF (Unix)
- PowerShell scripts (`.ps1`): Use CRLF (Windows)
- Dockerfiles: Always use LF

### Permission Denied on Scripts

**Issue:** Scripts fail to execute in PowerShell.

**Solution:** Set execution policy:
```powershell
# For current user only (recommended)
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

# Or bypass for specific script
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

## Differences from Bash Scripts

### Path Separators
- **Bash:** Uses `/` (forward slash)
- **PowerShell:** Uses `\` (backslash) but also accepts `/`

### Environment Variables
- **Bash:** `$VAR` or `${VAR}`
- **PowerShell:** `$env:VAR`

### Script Execution
- **Bash:** `./script.sh`
- **PowerShell:** `.\script.ps1` or `powershell -File script.ps1`

### Exit Codes
- **Bash:** `set -e` exits on any error
- **PowerShell:** `$ErrorActionPreference = "Stop"` achieves the same

## See Also

- [Command Reference](../docs/command-reference.md) - Complete command documentation
- [Environment Switching](../docs/environment-switching.md) - Managing different environments
- [Docker Documentation](../README.md#docker-setup) - Docker setup and configuration

## Contributing

When adding new scripts:

1. Create both `.sh` and `.ps1` versions
2. Test on both Linux/macOS and Windows
3. Update this README with usage examples
4. Ensure Docker builds work on both platforms
