#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if ! command -v dotnet >/dev/null 2>&1; then
  echo '.NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0' >&2
  exit 1
fi

echo 'Starting Micro Grid Bot at http://127.0.0.1:5080 (Ctrl+C to stop)'
echo 'OKX credentials: open the dashboard and use the credentials panel (or set OKX_* env vars).'
cd "$repo_root"
exec dotnet run --project "$repo_root/src/MicroGrid.Bot/MicroGrid.Bot.csproj" --configuration Release
