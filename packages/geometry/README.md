# @nurbsscene/geometry

Double-precision rational NURBS on a shared native/WebAssembly numerical kernel.

Version `0.1.0-alpha.1`. Independent source implementation, not full Rhino/Grasshopper compatibility. Runtime dependencies are declared explicitly as scoped NurbsScene packages. Public module exports are listed in `package.json`.

Includes the actual C-compiled `dist/nurbsscene.wasm` numerical kernel. Browser loading defaults to that relative asset; Node should pass WASM bytes loaded from the `./kernel.wasm` export. Positive weights, binary64 authoring, operation-specific domain/degree limits. No B-rep solid kernel.

See the source repository archive `README.md`, `docs/ARCHITECTURE.md`, `docs/PARITY.md` and `docs/VALIDATION.md` for detailed boundaries. MIT license applies to this package's own source. No npm registry publication has occurred.
