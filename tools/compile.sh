#!/usr/bin/env bash
# Compile FullThrust.Sim, then FullThrust.Game. Extra args go to both `dotnet build`s.

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

dotnet build "$ROOT/sim/FullThrust.Sim.csproj" "$@"
dotnet build "$ROOT/game/FullThrust.Game.csproj" "$@"
