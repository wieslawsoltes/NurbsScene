#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p evidence
npm ci --offline --ignore-scripts
bash tools/build-kernel.sh
python3 -m unittest discover -s tests/native -v 2>&1 | tee evidence/native-tests.log
npm test 2>&1 | tee evidence/modules-tests.log
node tools/pack-javascript.mjs 2>&1 | tee evidence/npm-pack.log
node tools/test-npm-consumer.mjs 2>&1 | tee evidence/npm-consumer.log
python3 tools/build-offline-harness.py
python3 tests/browser/test_offline.py 2>&1 | tee evidence/browser-offline-tests.log
python3 tools/check-source-layout.py
printf '\nValidated: native kernel, JavaScript modules and offline browser interaction subset.\n'
printf 'NOT qualified: managed/Uno build, GPU execution, full Rhino/Grasshopper parity.\n'
