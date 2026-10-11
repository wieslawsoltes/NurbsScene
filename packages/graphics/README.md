# @nurbsscene/graphics

Camera-relative WebGPU/WebGL2 CAD renderer with PBR shading, analytic screen-space lines and BVH selection.

Version `0.1.0-alpha.1`. Independent source implementation, not full Rhino/Grasshopper compatibility. Runtime dependencies are declared explicitly as scoped NurbsScene packages. Public module exports are listed in `package.json`.

WebGPU and WebGL2 implementation source. CPU camera/BVH/display-preparation tests pass, but GPU shader/framebuffer execution was unavailable in the delivery environment. Do not treat module import or generated draw metrics as a rendering qualification. No CPU 3D substitute is used on GPU failure.

See the source repository archive `README.md`, `docs/ARCHITECTURE.md`, `docs/PARITY.md` and `docs/VALIDATION.md` for detailed boundaries. MIT license applies to this package's own source. No npm registry publication has occurred.
