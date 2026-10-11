# Architecture and implementation boundaries

## Dependency direction

```text
Geometry <- Kernel <- Model / Parametric
Geometry + Kernel <- Rendering
Model + Parametric + Rendering <- IO
Kernel + Model + Parametric + Rendering + IO <- Workbench
Workbench <- Controls (Uno only here) <- App
```

Model actually references Geometry, not the rendering package. Numerical and graph code have no windowing dependency. Rendering consumes immutable exact geometry; it cannot overwrite model controls or knots. Workbench coordinates previews and command transactions without directly constructing XAML. Controls can be used independently or through WorkspaceView.

The native C implementation and managed implementation are separate implementations of the same numerical family. A successful C test does not validate managed source. The independent SciPy fixtures are retained to cross-check the latter when .NET is available. There is no implied cross-language binary serializer compatibility.

The six JavaScript packages are another reusable implementation path and a browser test harness, not the production Uno application in disguise. Bare `@nurbsscene/...` dependencies resolve through npm in Node and through an import map in the module harness. The offline bundler resolves only the deliberately limited syntax of this repository; it is not a general-purpose ESM compiler.

## Exact geometry

Authoritative curves contain degree, expanded nondecreasing knot vector and homogeneous controls `(x*w, y*w, z*w, w)`. Tensor surfaces carry two degrees, two knot vectors and a V-fastest control net. Modeling calculations use binary64. Positive weights permit convex-hull reasoning. Zero/negative weights are not accepted; this is an explicit representation boundary rather than silent conversion.

Active span lookup is a binary search. Basis derivatives use a triangular Cox–de Boor derivative table; curve and mixed surface derivatives use rational quotient recurrences. Interior repeated knots use the right-side span; the last domain endpoint uses the final active span. Derivatives at continuity breaks must not be interpreted as two-sided smoothness certificates.

The native ABI uses caller-provided input/output/workspace capacities and does not allocate. The WASM wrapper uses a checked, nested linear-memory arena capped at 256 MiB; arrays escaping a call are copied out before the arena is rewound. The wrapper is not shared concurrently between Workers. The managed evaluator creates its own temporary arrays per call and does not require a native DLL. The optional native managed adapter checks ABI 1 and must load a separately built library.

Knot insertion and Bezier subdivision/elevation work in homogeneous space. Degree elevation currently decomposes to clamped Bezier spans and rejoins with C0 knot structure. The geometric curve is preserved, but this is not minimal-knot degree elevation or exact Rhino control-structure parity. Nonclamped curves can be evaluated; clamped-only edit/tessellation boundaries report errors.

Interpolation solves a dense scaled-pivot linear system. Compatible lofting aligns degree, domain and knots before interpolating homogeneous controls. Pathological input can still produce an unacceptable weight distribution or ill-conditioned solve and must be diagnosed. There is no claimed certified conditioning bound.

Curve display tessellation recursively subdivides Bezier spans and bounds the distance of every Euclidean control to the endpoint segment. Positive rational weights keep the span in that convex hull. This is a directed curve-to-chord bound, not a symmetric Hausdorff certificate. Display preparation rejects exhausted tolerance. Surface display is a finite uniform parameter grid with analytic normals and singular-pole handling, not trim-aware/adaptive certified surface triangulation.

Arc length uses adaptive Gaussian quadrature with an error estimate/convergence flag. Closest point uses a multistart numerical search; no global minimum guarantee. These are JavaScript geometry facilities, not claims that every managed operation has been ported.

## Documents and evaluation

Exact geometry is immutable and shared between document history snapshots. Scene object metadata, layers and selection are copied. Document transactions are synchronous and atomic; asynchronous delegate shapes are rejected. Failed commands restore the prior document. History is bounded; editing locked objects is rejected. The model is an in-process single-author UI document, not a collaborative database or crash-safe store.

Parametric graphs have stable node/wire identities and typed ports. Adding a wire validates ports and rejects cycles before mutation. Parameters and cached outputs detach mutable caller-owned containers. Plugin implementations must honor the immutable-value contract.

Topological evaluation captures a graph revision, reuses clean node results and invalidates descendants of a semantic edit. Cancellation and supersession cannot publish a successful stale solution. A component failure propagates a blocked diagnostic to dependents instead of reusing plausible old geometry as a new result. Solving is incremental but does not currently move expensive work off the browser UI thread; large-graph throughput and cancellation responsiveness need further engineering.

Numeric paths are ordered lexicographically as integers. Empty branches are represented. Longest, shortest and cross item matching plus item/list/tree access are implemented. Branch matching is exact-path matching with a documented single-branch broadcast. This is not a claim to reproduce every Grasshopper path matching/coercion case. Cyclic solvers, clusters, script components, compiled plugins and external services are not implemented.

## Graphics and host integration

Display meshes retain double positions for geometric picking and derive origin-relative float32 GPU positions. Scene replacement/rebasing prepares all data before publishing a revision; a failed tessellation or float32-range conversion retains the prior complete scene. Camera calculations remain double precision until matrix upload. The JavaScript scene uses a triangle BVH; managed picking currently scans display triangles and curve segments.

The WebGPU path has WGSL pipelines, dynamic uniform offsets, vertex/index storage and a depth24plus target. The WebGL2 path has GLSL3, VAO/VBO/index buffers and depth testing. Both have actual GPU resource creation and error reporting. Their code exists, but successful shader compilation/drawing has not been observed in the delivery environment.

The Uno renderer uses a private RGBA8 + depth24 framebuffer, then composites to the host target. It does not assume the host supplies depth or that framebuffer blitting into a multisampled host is valid. GPU resources are cached by exact geometry identity/display revision and deleted with a current context. Changed bindings, samplers, pixel transfer state, viewport/scissor, depth/color masks and blend state are restored. This implementation still requires context-specific verification; host GL state restoration is not inferred from CPU tests.

Rendering is forward-lit GGX-style shading. Ghosted mode uses approximate depth-sorted alpha. It is not order-independent transparency, a path tracer, a material/texture asset system or a production Rhino render-mode clone. No GPU-compute NURBS evaluation or tessellation exists in this revision; GPU is used for display rendering, CPU/WASM for exact modeling.

## Controls and extension contracts

Uno controls include CadViewport/ViewportControl, GraphCanvas/GraphEditorControl, CommandConsole, LayerInspector and WorkspaceView. Node drag, port drag, numeric edits, search, canvas navigation, inspectors, graph history and exact bake are source implementations. The graph canvas uses Skia vectors; native Uno controls provide menus, inputs and inspectors. Per-node screen-reader semantics and complete keyboard behavior need further work.

New geometry operations belong in Kernel and need independent numerical fixtures. New components must declare typed ports/access, be deterministic for identical immutable input, check cancellation where meaningful, produce all declared outputs and fail explicitly. New UI features should depend on Workbench/session state rather than bypassing document transactions. New renderers must preserve binary64 authoritative geometry, expose capability failures and provide real framebuffer tests.
