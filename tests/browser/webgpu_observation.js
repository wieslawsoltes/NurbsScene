// Observe the application's real WebGPU devices without replacing devices,
// suppressing native events, consuming error scopes, or changing descriptors.
(() => {
    "use strict";
    const maps = { installed: false, requestCount: 0, droppedCount: 0, records: [] };
    const state = { version: 1, installed: false, requestCount: 0, deviceCount: 0, errors: [], bufferMaps: maps };
    globalThis.nurbsSceneGpuObservation = state;
    const record = (kind, error, extra = {}) => state.errors.push({
        kind,
        type: error?.constructor?.name || "Error",
        message: String(error?.message ?? error ?? ""),
        ...extra
    });
    const prototype = globalThis.GPUAdapter?.prototype;
    if (!prototype || typeof prototype.requestDevice !== "function") {
        record("observer-unavailable", "GPUAdapter.requestDevice is unavailable in this context.");
        return;
    }
    const descriptor = Object.getOwnPropertyDescriptor(prototype, "requestDevice");
    const original = prototype.requestDevice;
    const observed = new WeakSet();
    try {
        Object.defineProperty(prototype, "requestDevice", {
            ...descriptor,
            value: function (...args) {
                state.requestCount++;
                let requested;
                try {
                    requested = Reflect.apply(original, this, args);
                } catch (error) {
                    record("device-request", error);
                    throw error;
                }
                // Use native Promise chaining and retain the original GPUDevice.
                return Promise.prototype.then.call(requested, device => {
                    if (!observed.has(device)) {
                        observed.add(device);
                        state.deviceCount++;
                        try {
                            device.addEventListener("uncapturederror", event => {
                                record("uncaptured-error", event.error);
                                // Do not preventDefault: the host keeps its own diagnostics.
                            });
                            Promise.prototype.then.call(device.lost, info => {
                                // Explicit host teardown is expected. Every other loss fails.
                                if (info.reason !== "destroyed")
                                    record("device-lost", info.message, { reason: info.reason });
                            }, error => record("device-lost-observer", error));
                        } catch (error) {
                            // Observation failure must fail qualification, while preserving
                            // the application's original device request result.
                            record("observer-error", error);
                        }
                    }
                    return device;
                }, error => {
                    record("device-request", error);
                    throw error;
                });
            }
        });
        state.installed = true;
    } catch (error) {
        record("observer-installation", error);
    }

    // Passive timing of the native map promise distinguishes GPU completion
    // from the managed continuation. No queue work, replacement promise or retry.
    // Missing GPUBuffer is supported by the synthetic device-only Node fixtures.
    const bufferPrototype = globalThis.GPUBuffer?.prototype;
    if (!bufferPrototype || typeof bufferPrototype.mapAsync !== "function")
        return;
    const mapDescriptor = Object.getOwnPropertyDescriptor(bufferPrototype, "mapAsync");
    const originalMap = bufferPrototype.mapAsync;
    const timestamp = () => globalThis.performance?.now?.() ?? Date.now();
    const limit = 64;
    try {
        Object.defineProperty(bufferPrototype, "mapAsync", {
            ...mapDescriptor,
            value: function (...args) {
                const id = ++maps.requestCount;
                // Bound retained timing records while observing every native failure.
                const sample = maps.records.length < limit ? {
                    id, requestedAtMilliseconds: timestamp(), settledAtMilliseconds: null,
                    durationMilliseconds: null, status: "pending",
                    mode: typeof args[0] === "number" ? args[0] : null,
                    offset: typeof args[1] === "number" ? args[1] : 0,
                    size: typeof args[2] === "number" ? args[2] : null,
                    bufferByteLength: null
                } : null;
                if (sample) maps.records.push(sample);
                else maps.droppedCount++;
                const settle = (status, error) => {
                    // Observing a rejection can mark the native promise handled.
                    // Retain it in the mandatory error gate as well as diagnostics.
                    // Qualification contains no intentional failed map operations.
                    try {
                        if (status === "rejected" || status === "threw")
                            record("buffer-map", error, { requestId: id, status });
                        if (!sample) return;
                        sample.status = status;
                        sample.settledAtMilliseconds = timestamp();
                        sample.durationMilliseconds = sample.settledAtMilliseconds - sample.requestedAtMilliseconds;
                        if (error !== undefined)
                            sample.error = { type: error?.constructor?.name || "Error", message: String(error?.message ?? error ?? "") };
                    } catch (observationError) {
                        record("buffer-map-observer", observationError);
                    }
                };
                let pending;
                try {
                    pending = Reflect.apply(originalMap, this, args);
                } catch (error) {
                    settle("threw", error);
                    throw error;
                }
                if (sample) {
                    try {
                        sample.bufferByteLength = this.size;
                        if (sample.size === null)
                            sample.size = sample.bufferByteLength - sample.offset;
                    } catch {
                        // Preserve a valid native return even when a diagnostic
                        // property is inaccessible on an unusual wrapper.
                    }
                }
                try {
                    Promise.prototype.then.call(pending,
                        () => settle("fulfilled"),
                        error => settle("rejected", error));
                } catch (error) {
                    record("buffer-map-observer", error);
                }
                return pending;
            }
        });
        maps.installed = true;
    } catch (error) {
        record("buffer-map-observer-installation", error);
    }
})();
