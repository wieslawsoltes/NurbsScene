# Formats, commands and API use

## Exact workspace formats are deliberately distinct

| Implementation | Format marker | Version | Suggested extension |
|---|---|---|---|
| JavaScript harness | `nurbsscene.workspace` | 1 | `.nurbs.json` |
| Managed / Uno | `nurbsscene.managed-workspace` | 1 | `.nscene` |

Both persist exact curve/surface definitions, document objects/layers and graph topology/parameters. They are **not mutually interchangeable yet**, and neither reads `.3dm`, `.gh` or `.ghx`. The managed form stores homogeneous controls explicitly. The JS form stores Euclidean controls with weights and reconstructs homogeneous coordinates. Format/version, structure, budgets and geometry validation precede replacing a loaded workspace. Invalid imports must leave the current document intact.

OBJ/STL export is display triangulation, not exact CAD interchange. STL may contain open surfaces. Units are metadata in the workspace, not a reliable unit declaration in raw STL. Export does not claim manifoldness, watertightness, solid classification or manufacturing readiness.

## Managed command subset

Coordinates use a single comma-separated `x,y,z` token. Examples:

```text
Line 0,0,0 20,10,0
Circle 0,0,0 10
Interpolate -20,0,0 -8,12,3 8,-8,6 20,0,0
SelAll
SelNone
Move 5,0,0
Copy 5,0,0
Scale 2
Rotate 30
Extrude 0,0,12
Revolve 0,0,0 0,0,1 360
Loft
Reverse
InsertKnot 0.5
ElevateDegree
Saddle
Delete
Undo
Redo
ZoomExtents
Wireframe
Shaded
Ghosted
Cancel
```

`Line` without coordinates enters the implemented two-point construction-plane prompt. Other examples are explicit numeric commands, not full interactive Rhino option grammar. Extrude/Revolve/Loft and curve edits require suitable selected curves. Rotation uses the world Z axis; these are not all original transform options. Unknown commands—including solid booleans—raise a not-implemented diagnostic without adding replacement geometry.

The harness uses separate space-separated coordinates, for example `Circle 0 0 0 10`. Type `Help` in that console for its actual dispatcher catalog. This syntax difference is a recorded boundary, not original command compatibility.

## Using the packaged JavaScript geometry

```js
import { createGeometryKernel } from '@nurbsscene/geometry';

// Browser: fetches the WASM asset relative to the geometry module.
const kernel = await createGeometryKernel();
const circle = kernel.circle([0, 0, 0], 5);
const surface = kernel.extrude(circle, [0, 0, 20]);
const point = surface.pointAt(0.125, 0.5);
```

Node applications should pass loaded WASM bytes to `createGeometryKernel` (no `file:` fetch assumption). The geometry package exports `@nurbsscene/geometry/kernel.wasm` for asset resolution. Browser hosts without a bundler need an import map mapping `@nurbsscene/` to the published package root. Preserve the `geometry/dist/nurbsscene.wasm` asset when deploying.

```js
import { readFile } from 'node:fs/promises';
import { createGeometryKernel } from '@nurbsscene/geometry';
import { Graph } from '@nurbsscene/parametric';
import { createStandardRegistry } from '@nurbsscene/parametric/components';

const wasm = await readFile(new URL(import.meta.resolve('@nurbsscene/geometry/kernel.wasm')));
const kernel = await createGeometryKernel(wasm);
const registry = createStandardRegistry(kernel);
const graph = new Graph(registry);
// Add components and typed wires using Graph's exported API.
console.log(registry.definitions.length); // 102 in this revision
```

The controls package is browser-only and registers `<nurbs-viewport>` and `<nurbs-graph-editor>`. Import its stylesheet separately. Their source is reusable without the harness chrome. The Uno control package is a separate native control implementation, not a wrapper around those elements.

## Native ABI and concurrency

`native/kernel/include/nurbsscene.h` is the native contract. Build as C11 and check `ns_abi_version() == 1`. Every evaluator receives explicit element capacities; use the corresponding workspace-size query. Status codes distinguish invalid arguments, dimensions/domain, singular arithmetic, capacity and solve failures. Caller-owned buffers and library lifetime are the host's responsibility.

Do not share a single JavaScript `GeometryKernel` arena concurrently across workers/reentrant asynchronous work. A call frame is synchronous; it rewinds its arena on exit. Use a separate kernel instance per worker. Managed geometry objects are immutable, while document/UI editing belongs on the owning UI thread. Graph revision locking does not make arbitrary component plugin code thread-safe.
