#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts/native packages/geometry/dist
cmake -S native/kernel -B artifacts/native -DCMAKE_BUILD_TYPE=Release
cmake --build artifacts/native --parallel
CLANG="${CLANG:-clang}"
"$CLANG" --target=wasm32 -O3 -std=c11 -nostdlib -fno-builtin -fno-fast-math \
 -Inative/kernel/include native/kernel/src/nurbsscene.c \
 -Wl,--no-entry -Wl,--export-all -Wl,--export-memory \
 -Wl,--initial-memory=2097152 -Wl,--max-memory=268435456 \
 -Wl,-z,stack-size=65536 -o packages/geometry/dist/nurbsscene.wasm
