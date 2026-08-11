$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0'
}

Write-Host 'Starting Micro Grid Bot at http://127.0.0.1:5080 (Ctrl+C to stop)'
Write-Host 'OKX credentials: open the dashboard and use the credentials panel (or set OKX_* env vars).'
Set-Location -LiteralPath $repoRoot
dotnet run --project (Join-Path $repoRoot 'src/MicroGrid.Bot/MicroGrid.Bot.csproj') --configuration Release
