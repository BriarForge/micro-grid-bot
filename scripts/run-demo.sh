#!/usr/bin/env bash
set -euo pipefail

export OKX_DEMO_MODE=true
exec "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/run-local.sh"
