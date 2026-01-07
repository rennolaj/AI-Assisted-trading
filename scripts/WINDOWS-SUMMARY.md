# Windows PowerShell Scripts - Quick Summary

## What Was Created

PowerShell equivalents of all bash scripts needed for building Docker images on Windows:

### Created Files

1. **`scripts/dotnet.ps1`**
   - PowerShell wrapper for .NET CLI
   - Finds dotnet.exe in standard Windows locations
   - Disables workload update notifications

2. **`scripts/restore.ps1`**
   - Restores NuGet package dependencies
   - Calls dotnet.ps1 with restore command

3. **`scripts/build.ps1`**
   - Builds the entire solution
   - Runs dev bootstrap in non-CI environments
   - Disables MSBuild node reuse for clean builds

4. **`scripts/test.ps1`**
   - Runs all unit tests across all test projects
   - Exits on first failure
   - Colored output for better visibility

5. **`scripts/dev/bootstrap.ps1`**
   - Sets up local development environment on Windows
   - Checks for PostgreSQL and Redis
   - Creates database and runs init.sql
   - Provides helpful installation instructions if dependencies missing

6. **`scripts/WINDOWS.md`**
   - Comprehensive documentation for Windows users
   - Installation prerequisites
   - Usage examples for all scripts
   - Troubleshooting guide
   - Docker build instructions

### Modified Files

1. **`Dockerfile`**
   - Updated to support both bash and PowerShell scripts
   - Falls back to PowerShell if bash not available
   - Works on both Linux and Windows containers

2. **`Dockerfile.worker`**
   - Updated to support both bash and PowerShell scripts
   - Same fallback mechanism as main Dockerfile

3. **`docs/command-reference.md`**
   - Added Windows support section
   - Included PowerShell command examples
   - Added reference to WINDOWS.md
   - Updated best practices to mention Windows support

## How It Works

### In Docker Builds

The Dockerfiles now use a fallback pattern:

```dockerfile
RUN ./scripts/restore.sh || pwsh -File ./scripts/restore.ps1
```

This means:
1. **Linux containers**: Use bash scripts (`.sh`)
2. **Windows containers**: Automatically fall back to PowerShell (`.ps1`)
3. **No manual intervention required**

### Local Development on Windows

Windows developers can now use native PowerShell scripts:

```powershell
# Instead of ./scripts/build.sh (bash)
.\scripts\build.ps1

# Instead of ./scripts/test.sh (bash)
.\scripts\test.ps1
```

## Key Features

### 1. Cross-Platform Compatibility
- Same functionality on Linux, macOS, and Windows
- Docker builds work identically across platforms
- No need to maintain separate workflows

### 2. Windows-Native Experience
- Uses PowerShell (built into Windows)
- Finds .NET, PostgreSQL, and Redis in standard Windows locations
- Provides Windows-specific installation instructions

### 3. Error Handling
- All scripts use `$ErrorActionPreference = "Stop"` (PowerShell equivalent of `set -e`)
- Proper exit code propagation
- Colored output for errors and warnings

### 4. Smart Dependency Detection
- `bootstrap.ps1` searches common PostgreSQL installation paths:
  - Program Files locations
  - Environment variables (DOTNET_ROOT, PGBIN)
  - PATH
- Provides installation links if dependencies not found

## Usage Examples

### Building Docker Images on Windows

```powershell
# Build API image
docker build -t mvp-trading-api:latest -f Dockerfile .

# Build Worker image
docker build -t mvp-trading-worker:latest -f Dockerfile.worker .

# Build with Docker Compose
docker compose build

# Build and run
docker compose up --build -d
```

### Local Development on Windows

```powershell
# Initial setup
.\scripts\dev\bootstrap.ps1
.\scripts\restore.ps1
.\scripts\build.ps1
.\scripts\test.ps1

# Day-to-day development
.\scripts\build.ps1
.\scripts\test.ps1

# Run specific project
.\scripts\dotnet.ps1 run --project src\Mvp.Trading.Api\Mvp.Trading.Api.csproj
```

### Mixed Teams (Linux/Mac + Windows)

**Linux/macOS developers:**
```bash
./scripts/build.sh
./scripts/test.sh
```

**Windows developers:**
```powershell
.\scripts\build.ps1
.\scripts\test.ps1
```

**Both get the same result!**

## Testing the Changes

### Verify PowerShell Scripts Work

```powershell
# Test dotnet wrapper
.\scripts\dotnet.ps1 --version

# Test restore
.\scripts\restore.ps1

# Test build
.\scripts\build.ps1

# Test tests
.\scripts\test.ps1
```

### Verify Docker Builds Work

```powershell
# Build with Docker (should use bash in Linux container)
docker compose build

# Check the build logs show successful script execution
docker compose build --progress=plain 2>&1 | Select-String "scripts"
```

## Prerequisites for Windows Users

### Minimum Requirements
- **Windows 10/11** with PowerShell 5.1+
- **.NET 10 SDK** (required)
- **Docker Desktop** (for Docker builds)

### Optional for Local Development
- **PostgreSQL 14+** (or use Docker)
- **Redis** (or use Docker)

### Installation Methods

**Via Chocolatey (Recommended):**
```powershell
choco install dotnet-sdk postgresql redis-64
```

**Via Official Installers:**
- .NET: https://dotnet.microsoft.com/download/dotnet/10.0
- PostgreSQL: https://www.postgresql.org/download/windows/
- Redis: https://github.com/microsoftarchive/redis/releases
- Docker: https://www.docker.com/products/docker-desktop/

## Troubleshooting

### Script Execution Policy Error

```powershell
# Set execution policy for current user
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

# Or bypass for single script
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

### Line Ending Issues in Git

```powershell
# Configure Git to use LF line endings
git config core.autocrlf false
git rm --cached -r .
git reset --hard
```

### PostgreSQL Not Found

Use Docker instead:
```powershell
docker compose up -d postgres
# Skip bootstrap.ps1, use Docker database
```

## Documentation References

- **Main Documentation**: [scripts/WINDOWS.md](../scripts/WINDOWS.md)
- **Command Reference**: [docs/command-reference.md](../docs/command-reference.md)
- **Project README**: [README.md](../README.md)

## Summary

✅ **All scripts ported to PowerShell**
✅ **Dockerfiles support both platforms**
✅ **Documentation updated**
✅ **No breaking changes for Linux/macOS users**
✅ **Windows users now have full parity**

Windows developers can now build, test, and deploy the MVP Trading system using native PowerShell scripts, with the same experience as Linux/macOS developers.
