# Uno Platform 7 and WebGPU migration

## Scope and baseline

This migration starts at `5c14b18` and replaces the Uno application's GL/WebGL2
viewport with commands submitted through Uno 7's shared WebGPU device. It also
migrates the graph canvas, viewport overlays, icons, project configuration and
qualification workflows. Exact binary64 NURBS geometry, editing, document history,
parametric solving, picking and interchange remain the modeling authority.

The target is a high quality, GPU-resident CAD presentation pipeline: explicit
resource ownership, cached geometry, depth-tested surfaces and curves, physically
based shading, HDR lighting, anti-aliasing, transparency and predictable resize,
reload and capture behavior. A renderer feature list is not evidence of AAA visual
quality or of a particular frame rate; qualification records below distinguish
compilation, software-adapter execution and physical-device measurements.

## Primary references

- [Uno 7 migration guide](https://platform.uno/docs/articles/migrating-to-uno-7.html)
- [WebGPU backend](https://platform.uno/docs/articles/features/using-webgpu-rendering.html)
- [WebGpuCanvasElement](https://platform.uno/docs/articles/controls/WebGpuCanvasElement.html)
- [Uno.Sdk packages](https://www.nuget.org/packages/Uno.Sdk)
- [WebGPU specification](https://www.w3.org/TR/webgpu/)

## Migration journal

### 1. Baseline and migration branch

- Recorded the existing Uno 6.7.30 SDK, GL viewport, Skia graph canvas and
  independent JavaScript validation harness.
- Established `feat/uno7-webgpu` for incremental, reviewable commits.
- Identified Uno's borrowed device/queue/target lifetime and demand-driven
  invalidation as core integration contracts.

## Validation record

Implementation and qualification are in progress. Results will be recorded here
with the actual commands and limitations before the PR is marked ready.
