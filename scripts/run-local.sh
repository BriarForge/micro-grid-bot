#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if ! command -v dotnet >/dev/null 2>&1; then
  echo '.NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0' >&2
  exit 1
fi
if [[ ! -f "$repo_root/.env" ]]; then
  echo "Missing $repo_root/.env. Copy .env.example to .env and add OKX credentials." >&2
  exit 1
fi

echo 'Starting Micro Grid Bot at http://localhost:5080 (Ctrl+C to stop)'
exec dotnet run --project "$repo_root/src/MicroGrid.Bot/MicroGrid.Bot.csproj" --configuration Release
