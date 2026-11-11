"""Observer unit contracts. These synthetic Node devices are not GPU acceptance."""
import json
from pathlib import Path
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (ROOT / "tests/browser/webgpu_observation.js").read_text()

PRELUDE = r"""
const assert = require("node:assert/strict");
const vm = require("node:vm");
const observerSource = INPUT;
function fixture(request) {
    class Adapter { requestDevice(...args) { return Reflect.apply(request, this, args); } }
    const context = vm.createContext({ GPUAdapter: Adapter, Promise, WeakSet, Reflect });
    vm.runInContext(observerSource, context);
    return { Adapter, adapter: new Adapter(), state: context.nurbsSceneGpuObservation, context };
}
function device() {
    let lose;
    const listeners = [];
    return {
        lost: new Promise(resolve => { lose = resolve; }),
        addEventListener(type, callback) { assert.equal(type, "uncapturederror"); listeners.push(callback); },
        emit(error) {
            const event = { error, prevented: false, preventDefault() { this.prevented = true; } };
            for (const callback of listeners) callback(event);
            assert.equal(event.prevented, false, "Native diagnostics must not be suppressed");
        },
        lose(reason, message = "lost") { lose({ reason, message }); }
    };
}
"""

class WebGpuObservationContracts(unittest.TestCase):
    def run_node(self, body):
        script = PRELUDE.replace("INPUT", json.dumps(SOURCE)) + "\n(async () => {\n" + body + "\n})().catch(error => { console.error(error); process.exitCode = 1; });"
        result = subprocess.run(["node", "-"], input=script, text=True, capture_output=True, timeout=20)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_original_receiver_arguments_promise_and_device_are_preserved(self):
        self.run_node(r"""
            const gpu = device(), descriptor = { requiredFeatures: [] };
            let calledReceiver, calledArgs;
            const f = fixture(function (...args) { calledReceiver = this; calledArgs = args; return Promise.resolve(gpu); });
            const result = f.adapter.requestDevice(descriptor);
            assert.ok(result instanceof Promise);
            assert.equal(await result, gpu);
            assert.equal(calledReceiver, f.adapter);
            assert.equal(calledArgs.length, 1);
            assert.equal(calledArgs[0], descriptor);
            assert.equal(f.state.installed, true);
            assert.equal(f.state.requestCount, 1);
            assert.equal(f.state.deviceCount, 1);
            assert.equal(f.state.errors.length, 0);
        """)

    def test_every_real_device_has_unsuppressed_validation_observation(self):
        self.run_node(r"""
            const devices = [device(), device()];
            let index = 0;
            const f = fixture(() => Promise.resolve(devices[index++]));
            assert.equal(await f.adapter.requestDevice(), devices[0]);
            assert.equal(await f.adapter.requestDevice(), devices[1]);
            devices[0].emit(new Error("opaque validation"));
            devices[1].emit(new Error("bloom validation"));
            assert.equal(f.state.deviceCount, 2);
            assert.equal(f.state.requestCount, 2);
            assert.equal(f.state.errors.length, 2);
            assert.equal(f.state.errors[0].kind, "uncaptured-error");
            assert.equal(f.state.errors[0].message, "opaque validation");
            assert.equal(f.state.errors[1].message, "bloom validation");
        """)

    def test_unexpected_device_loss_fails_observation(self):
        self.run_node(r"""
            const gpu = device(), f = fixture(() => Promise.resolve(gpu));
            await f.adapter.requestDevice();
            gpu.lose("unknown", "driver reset");
            await Promise.resolve();
            assert.equal(f.state.errors.length, 1);
            assert.equal(f.state.errors[0].kind, "device-lost");
            assert.equal(f.state.errors[0].reason, "unknown");
            assert.equal(f.state.errors[0].message, "driver reset");
        """)

    def test_explicit_destroyed_reason_is_the_only_ignored_loss(self):
        self.run_node(r"""
            const gpu = device(), f = fixture(() => Promise.resolve(gpu));
            await f.adapter.requestDevice();
            gpu.lose("destroyed", "explicit host teardown");
            await Promise.resolve();
            assert.equal(f.state.errors.length, 0);
            const another = device(), g = fixture(() => Promise.resolve(another));
            await g.adapter.requestDevice();
            another.lose(undefined, "missing reason");
            await Promise.resolve();
            assert.equal(g.state.errors.length, 1);
        """)

    def test_device_request_rejection_retains_original_error(self):
        self.run_node(r"""
            const original = new Error("request rejected"), f = fixture(() => Promise.reject(original));
            await assert.rejects(f.adapter.requestDevice(), error => error === original);
            assert.equal(f.state.errors.length, 1);
            assert.equal(f.state.errors[0].kind, "device-request");
            assert.equal(f.state.deviceCount, 0);
        """)

    def test_synchronous_request_exception_is_not_changed(self):
        self.run_node(r"""
            const original = new TypeError("illegal invocation"), f = fixture(() => { throw original; });
            assert.throws(() => f.adapter.requestDevice(), error => error === original);
            assert.equal(f.state.errors.length, 1);
            assert.equal(f.state.errors[0].kind, "device-request");
        """)

    def test_observer_failure_does_not_replace_the_native_device(self):
        self.run_node(r"""
            const gpu = device();
            gpu.addEventListener = () => { throw new Error("observer setup failed"); };
            const f = fixture(() => Promise.resolve(gpu));
            assert.equal(await f.adapter.requestDevice(), gpu);
            assert.equal(f.state.errors.length, 1);
            assert.equal(f.state.errors[0].kind, "observer-error");
        """)

    def test_unavailable_webgpu_cannot_produce_success_evidence(self):
        self.run_node(r"""
            const context = vm.createContext({ Promise, WeakSet, Reflect });
            vm.runInContext(observerSource, context);
            assert.equal(context.nurbsSceneGpuObservation.installed, false);
            assert.equal(context.nurbsSceneGpuObservation.deviceCount, 0);
            assert.equal(context.nurbsSceneGpuObservation.errors.length, 1);
            assert.equal(context.nurbsSceneGpuObservation.errors[0].kind, "observer-unavailable");
        """)

if __name__ == "__main__":
    unittest.main(verbosity=2)
