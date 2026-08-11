#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUTPUT="${1:-$ROOT/artifacts/publish/todox-landing}"
PROJECT="$ROOT/TodoX.Landing/TodoX.Landing.csproj"

rm -rf "$OUTPUT"
dotnet restore "$PROJECT"
dotnet build "$PROJECT" -c Release --no-restore
dotnet publish "$PROJECT" -c Release -o "$OUTPUT" --no-restore --no-build --self-contained false

printf 'TodoX Landing published to: %s\n' "$OUTPUT"
