$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0'
}
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot '.env'))) {
    throw "Missing $repoRoot\.env. Copy .env.example to .env and add OKX demo credentials."
}

dotnet run --project (Join-Path $repoRoot 'src/MicroGrid.Bot/MicroGrid.Bot.csproj') --configuration Release
