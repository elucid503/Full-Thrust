#!/usr/bin/env bash
# Thin wrapper so the engine path lives in one place. Override with GODOT=/path/to/binary.

set -euo pipefail

# Let terrain workers catch AccessViolationException instead of taking the process down.
export DOTNET_legacyCorruptedStateExceptionsPolicy=1
export COMPlus_legacyCorruptedStateExceptionsPolicy=1

GODOT="${GODOT:-C:/Users/paul/Documents/Godot/Godot_v4.7.2-stable_mono_win64_console.exe}"
PROJECT="$(cd "$(dirname "$0")/../game" && pwd)"

exec "$GODOT" --path "$PROJECT" "$@"
