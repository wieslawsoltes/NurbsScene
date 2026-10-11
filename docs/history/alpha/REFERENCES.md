# Reference and dependency notes

The user's behavioral references are [Rhinoceros 3D](https://en.wikipedia.org/wiki/Rhinoceros_3D) and [Grasshopper 3D](https://en.wikipedia.org/wiki/Grasshopper_3D). These links identify the requested products; they are not evidence that this source matches them. No proprietary source, icons, binaries, license keys or plugin payloads were extracted.

Engineering references consulted:

- [Rhino features](https://www.rhino3d.com/features/) and [Grasshopper](https://www.grasshopper3d.com/) describe the broad target ecosystem. Full command and component inventories still need an independent compatibility audit.
- [Uno GLCanvasElement](https://platform.uno/docs/articles/controls/GLCanvasElement.html): lifecycle, context availability, Silk.NET bindings, desktop/ES shader distinction and host-state requirements.
- [Uno SKCanvasElement](https://platform.uno/docs/articles/controls/SKCanvasElement.html): native Skia control integration.
- [Uno publish documentation](https://platform.uno/docs/articles/external/uno.wasm.bootstrap/doc/deploy-and-publish.html): actual .NET publish output and web hosting.
- [Uno SDK documentation](https://platform.uno/docs/articles/features/using-the-uno-sdk.html): single-project platform targets and feature-based references.
- [Silk.NET](https://github.com/dotnet/Silk.NET) / [Silk.NET.OpenGL 2.23.0](https://www.nuget.org/packages/Silk.NET.OpenGL/2.23.0): typed OpenGL API signatures.
- [Uno.Sdk 6.7.30](https://www.nuget.org/packages/Uno.Sdk/6.7.30): pinned SDK package. The browser Graphics3DGL version mirrors the user's existing Uno project configuration; actual restore compatibility has not been confirmed here.
- [SciPy BSpline](https://docs.scipy.org/doc/scipy/reference/generated/scipy.interpolate.BSpline.html): independent B-spline numerical oracle. Rational derivative recurrences are applied to SciPy's homogeneous derivatives by the tests.
- [W3C WebGPU specification](https://www.w3.org/TR/webgpu/) and [WebGL 2 specification](https://registry.khronos.org/webgl/specs/latest/2.0/): display API contracts.
- [Open CASCADE](https://dev.opencascade.org/doc/overview/html/): investigated exact B-rep backend. **No OCCT implementation, native library or WASM library is part of this delivery.**

The algorithms are independently authored implementations of standard rational B-spline mathematics. The projects use original interface assets and the author's own source. Managed NuGet dependencies are referenced, not vendored; their exact restored dependency graph and licenses must be retained with a future successful build. The only prebuilt modeling binary included here is the project's own C-compiled WASM module (plus the optional native library in the separate native archive).

The included npm modules have only intra-project runtime dependencies. Python numerical/browser libraries are test-only. All screenshots in evidence are captured from the actual local offline harness; none represents an original Rhino/Grasshopper screen or a successful Uno/GPU execution.
