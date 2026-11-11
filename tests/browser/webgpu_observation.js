// Observe the application's real WebGPU devices without replacing devices,
// suppressing native events, consuming error scopes, or changing descriptors.
(() => {
    "use strict";
    const state = { version: 1, installed: false, requestCount: 0, deviceCount: 0, errors: [] };
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
})();
