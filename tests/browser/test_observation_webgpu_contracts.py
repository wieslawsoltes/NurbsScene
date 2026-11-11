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
function fixture(request, mapRequest, now) {
    class Adapter { requestDevice(...args) { return Reflect.apply(request, this, args); } }
    class Buffer {
        constructor() { this.size = 4096; }
        mapAsync(...args) { return Reflect.apply(mapRequest, this, args); }
    }
    const globals = { GPUAdapter: Adapter, Promise, WeakSet, Reflect };
    if (mapRequest) globals.GPUBuffer = Buffer;
    if (now) globals.performance = { now };
    const context = vm.createContext(globals);
    vm.runInContext(observerSource, context);
    return { Adapter, adapter: new Adapter(), Buffer, buffer: new Buffer(), state: context.nurbsSceneGpuObservation, context };
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

    def test_map_returns_exact_native_promise_and_preserves_invocation(self):
        self.run_node(r"""
            let resolve, receiver, argumentsSeen, time = 100;
            const pending = new Promise(done => { resolve = done; });
            const f = fixture(() => Promise.resolve(device()), function (...args) {
                receiver = this; argumentsSeen = args; return pending;
            }, () => time);
            const originalDescriptor = Object.getOwnPropertyDescriptor(f.Buffer.prototype, "mapAsync");
            const result = f.buffer.mapAsync(1, 256, 512);
            assert.equal(result, pending, "The application must receive the exact native promise");
            assert.equal(receiver, f.buffer);
            assert.deepEqual(argumentsSeen, [1, 256, 512]);
            assert.equal(originalDescriptor.enumerable, false);
            assert.equal(f.state.bufferMaps.installed, true);
            assert.equal(f.state.bufferMaps.records[0].status, "pending");
            time = 136; resolve();
            await result;
            const sample = f.state.bufferMaps.records[0];
            assert.equal(sample.requestedAtMilliseconds, 100);
            assert.equal(sample.settledAtMilliseconds, 136);
            assert.equal(sample.durationMilliseconds, 36);
            assert.equal(sample.mode, 1);
            assert.equal(sample.offset, 256);
            assert.equal(sample.size, 512);
            assert.equal(sample.bufferByteLength, 4096);
            assert.equal(sample.status, "fulfilled");
            assert.equal(f.state.errors.length, 0);
        """)

    def test_map_rejection_retains_exact_promise_and_error(self):
        self.run_node(r"""
            const original = new Error("map rejected"), pending = Promise.reject(original);
            const f = fixture(() => Promise.resolve(device()), () => pending);
            const result = f.buffer.mapAsync(1);
            assert.equal(result, pending);
            await assert.rejects(result, error => error === original);
            const sample = f.state.bufferMaps.records[0];
            assert.equal(sample.status, "rejected");
            assert.equal(sample.error.message, "map rejected");
            assert.equal(f.state.errors.length, 1);
            assert.equal(f.state.errors[0].kind, "buffer-map");
            assert.equal(f.state.errors[0].message, "map rejected");
            assert.equal(f.state.errors[0].status, "rejected");
        """)

    def test_synchronous_map_exception_is_preserved(self):
        self.run_node(r"""
            const original = new TypeError("illegal buffer");
            const f = fixture(() => Promise.resolve(device()), () => { throw original; });
            assert.throws(() => f.buffer.mapAsync(1), error => error === original);
            assert.equal(f.state.bufferMaps.records[0].status, "threw");
            assert.equal(f.state.bufferMaps.records[0].error.message, "illegal buffer");
            assert.equal(f.state.errors.length, 1);
            assert.equal(f.state.errors[0].kind, "buffer-map");
            assert.equal(f.state.errors[0].status, "threw");
        """)

    def test_default_map_range_is_observed_without_adding_arguments(self):
        self.run_node(r"""
            let argumentsSeen;
            const pending = Promise.resolve();
            const f = fixture(() => Promise.resolve(device()), (...args) => { argumentsSeen = args; return pending; });
            assert.equal(f.buffer.mapAsync(1), pending);
            await pending;
            assert.deepEqual(argumentsSeen, [1]);
            assert.equal(f.state.bufferMaps.records[0].offset, 0);
            assert.equal(f.state.bufferMaps.records[0].size, 4096);
        """)

    def test_map_timing_records_are_bounded(self):
        self.run_node(r"""
            const pending = Promise.resolve();
            const f = fixture(() => Promise.resolve(device()), () => pending);
            for (let i = 0; i < 70; i++)
                assert.equal(f.buffer.mapAsync(1), pending);
            await pending;
            assert.equal(f.state.bufferMaps.requestCount, 70);
            assert.equal(f.state.bufferMaps.records.length, 64);
            assert.equal(f.state.bufferMaps.droppedCount, 6);
            assert.ok(f.state.bufferMaps.records.every(sample => sample.status === "fulfilled"));
        """)

    def test_failed_map_beyond_timing_cap_still_fails_error_observation(self):
        self.run_node(r"""
            const original = new Error("late map rejected");
            let count = 0;
            const f = fixture(() => Promise.resolve(device()),
                () => ++count <= 64 ? Promise.resolve() : Promise.reject(original));
            for (let i = 0; i < 64; i++) await f.buffer.mapAsync(1);
            await assert.rejects(f.buffer.mapAsync(1), error => error === original);
            assert.equal(f.state.bufferMaps.records.length, 64);
            assert.equal(f.state.bufferMaps.droppedCount, 1);
            assert.equal(f.state.errors.length, 1);
            assert.equal(f.state.errors[0].kind, "buffer-map");
            assert.equal(f.state.errors[0].requestId, 65);
            assert.equal(f.state.errors[0].message, "late map rejected");
        """)

    def test_device_only_fixture_does_not_require_gpu_buffer(self):
        self.run_node(r"""
            const f = fixture(() => Promise.resolve(device()));
            await f.adapter.requestDevice();
            assert.equal(f.state.installed, true);
            assert.equal(f.state.bufferMaps.installed, false);
            assert.equal(f.state.bufferMaps.records.length, 0);
            assert.equal(f.state.errors.length, 0);
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
