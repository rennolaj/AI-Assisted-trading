# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ENV CI=true
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

COPY . .

# Support both bash and PowerShell scripts
# Linux: use bash scripts
# Windows: detect PowerShell (pwsh or powershell) and use appropriate path
# Fix line endings for bash scripts (convert CRLF to LF on Windows-built images)
RUN find scripts -name "*.sh" -type f -exec sed -i 's/\r$//' {} \; 2>/dev/null || true
RUN chmod +x scripts/*.sh 2>/dev/null || true
RUN ./scripts/restore.sh || \
    (pwsh -File ./scripts/restore.ps1 2>/dev/null || powershell -File ./scripts/restore.ps1)
RUN ./scripts/build.sh || \
    (pwsh -File ./scripts/build.ps1 2>/dev/null || powershell -File ./scripts/build.ps1)
RUN ./scripts/test.sh || \
    (pwsh -File ./scripts/test.ps1 2>/dev/null || powershell -File ./scripts/test.ps1)
RUN ./scripts/dotnet.sh publish src/Mvp.Trading.Api/Mvp.Trading.Api.csproj -c Release -o /app/publish || \
    (pwsh -File ./scripts/dotnet.ps1 publish src/Mvp.Trading.Api/Mvp.Trading.Api.csproj -c Release -o /app/publish 2>/dev/null || \
     powershell -File ./scripts/dotnet.ps1 publish src/Mvp.Trading.Api/Mvp.Trading.Api.csproj -c Release -o /app/publish)

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

COPY --from=build /app/publish ./

# Run as the non-root user shipped with the .NET base images.
# App files stay root-owned and read-only for the runtime user.
USER app

ENTRYPOINT ["dotnet", "Mvp.Trading.Api.dll"]
