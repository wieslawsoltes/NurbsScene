# Shared HDR image-based lighting

The Uno and reusable JavaScript WebGPU renderers use the same offline-integrated studio environment. A self-authored 512 × 256 Radiance RGBE equirectangular image contains a neutral room, warm and cool softboxes, a rim strip and a ceiling panel. Its maximum decoded linear radiance is 6.40625. It is an original MIT asset; no third-party photograph, service or image-generation dependency is involved.

`tools/bake-studio-environment.py` generates the source image and then integrates the **decoded RGBE pixels**. The shading path therefore samples a prefiltered HDR image, replacing the earlier analytic hemisphere/reflection approximation. Authoring the lighting layout in source keeps the asset reproducible. Arbitrary user-supplied HDR environments are not an application feature in this revision.

## Offline integration and runtime cost

| Data | Integration | Runtime use |
| --- | --- | --- |
| 128 × 128 RGBA16F cube, eight mips | GGX importance sampling, 1,024 deterministic Hammersley samples per filtered texel, `N = V = R`, `N·L` weighting | One reflection lookup, trilinearly filtered at perceptual roughness × 7 |
| Nine RGB spherical-harmonic coefficients | Exact latitude-row solid angles, normalized real basis through degree two, cosine convolution | Diffuse irradiance evaluated from the surface normal, divided by π for Lambertian radiance |
| 128 × 128 RGBA16F DFG lookup | 1,024 samples per texel, Schlick Fresnel split, height-correlated Smith visibility | One lookup at `(N·V, perceptual roughness)`; specular factor `F0 × A + B` |

Perceptual roughness is squared to obtain GGX microfacet alpha. The DFG integration uses the same correlated visibility model as direct lighting. The cube prefilter and DFG are the usual split-sum approximation: they do not reproduce the exact view-dependent environment integral, and the single-scatter model retains masking loss at high roughness. The diffuse SH representation approximates low-frequency irradiance; it does not encode local visibility or shadows.

The raw payload occupies 1,179,808 bytes. Its deterministic gzip representation occupies 372,830 bytes. Both runtimes decompress it once and retain the CPU upload data. Each renderer creates its own cube, LUT, SH buffer and filtering sampler and releases those owned GPU resources on teardown. Geometry changes, camera motion and material changes never rebake or reupload the environment. No per-frame convolution, extra full-resolution framebuffer, network request, runtime image decoder or asset URL resolution is needed. The existing Ambient intensity setting scales the diffuse and specular environment terms together.

## Reproduce and verify

Use Python 3.12 with NumPy (the checked-in asset was generated with NumPy 2.3.5):

```sh
python -m pip install numpy==2.3.5
python tools/bake-studio-environment.py
python tools/bake-studio-environment.py --check
node --test tests/graphics-environment.test.mjs
```

The baker checks that a uniform environment stays uniform under every sampled GGX prefilter roughness, that a white environment reconstructs π diffuse irradiance, that all stored values are finite, that DFG obeys a bounded white-furnace response, and that selected LUT texels agree with separate 65,536-sample integrations. `--check` rebakes and compares every committed artifact byte. There is no random seed or runtime-dependent renderer input.

The Node contracts decompress the actual browser-compatible module, compare its complete payload and compressed bytes to the native embedded asset, verify source/asset hashes, inspect every half-float radiance/LUT entry and check the Schlick mirror/grazing limits. This is asset and integration evidence. The separate real WebGPU shader/package and Uno native/browser campaigns establish actual sampling and application behavior; a source/hash check alone does not establish GPU correctness.

Generated files:

- `src/NurbsScene.Rendering/Lighting/studio.hdr`: editable-source provenance in ordinary Radiance RGBE, with `-Y +X` scanline order.
- `src/NurbsScene.Rendering/Lighting/studio.ibl.gz`: canonical compressed integration, embedded in the native rendering assembly.
- `src/NurbsScene.Rendering/Lighting/studio.json`: dimensions, integration settings, checks and SHA-256 provenance.
- `packages/graphics/environment.js`: generated standalone ESM with the identical compressed bytes and a lazy `DecompressionStream` loader. It remains installable independently of this repository.

The raw payload SHA-256 is `8a72c9015e07baaaaec137b69322f4f48e5df448d2a24740c6bef55df0c03c21`. The C# loader validates it before exposing upload memory. Regeneration must update that loader constant if the authored environment changes; the Node provenance contract rejects drift.

## Binary and shader contracts

All integer and floating-point values use little-endian byte order. The 32-byte header contains ASCII `NSIBL001`, then six uint32 values: cube size 128, mip count 8, LUT size 128, SH count 9, and two reserved zeros. It is followed by 144 bytes of nine float32 `vec4` SH coefficients; the fourth component is zero. Mip data follows in increasing mip index, each containing six complete RGBA16F faces. The final block is the RGBA16F LUT, with A/B in R/G, zero B and unit alpha. LUT rows increase in roughness; columns increase in `N·V`; both integrate at texel centers.

SH basis order is `1, y, z, x, xy, yz, 3z²−1, xz, x²−y²`, with the usual normalized real factors. The coefficients already include the Lambert cosine convolution factors π, 2π/3 and π/4 for degrees zero, one and two. The shader applies the same normalized basis; coefficients are not pre-multiplied by those basis constants a second time.

Cube faces follow WebGPU layer order. With `u,v` in [-1,1], increasing `v` follows increasing row index:

| Layer | Face | Unnormalized direction |
| --- | --- | --- |
| 0 | +X | `(1, -v, -u)` |
| 1 | −X | `(-1, -v, u)` |
| 2 | +Y | `(u, 1, v)` |
| 3 | −Y | `(u, -1, -v)` |
| 4 | +Z | `(u, -v, 1)` |
| 5 | −Z | `(-u, -v, -1)` |

World geometry remains Z-up. Cube sampling uses the world-space reflection/normal directly; no implicit axis permutation is applied. Source-image rows run from +Z to −Z, and its longitude wraps horizontally.

Scene bind group zero retains frame uniforms, the directional shadow map and its comparison sampler at bindings 0–2. The environment adds the radiance cube at binding 3, a filtering sampler at 4, the DFG texture at 5, and the 144-byte SH uniform at 6. The shadow-only layout remains separate, so the caster pass never binds its own depth attachment as a sampled texture.

## Primary references

- [WebGPU specification: texture views and cube coordinates](https://www.w3.org/TR/webgpu/#dom-gputextureviewdimension-cube).
- [Brian Karis, Real Shading in Unreal Engine 4, SIGGRAPH 2013](https://cdn2.unrealengine.com/Resources/files/2013SiggraphPresentationsNotes-26915738.pdf): split-sum environment prefiltering and BRDF integration.
- [Filament's physically based rendering documentation](https://google.github.io/filament/main/filament.html): correlated Smith visibility, DFG evaluation and SH irradiance.
- [Ramamoorthi and Hanrahan, An Efficient Representation for Irradiance Environment Maps](https://lightfield.stanford.edu/papers/envmap/): low-order spherical-harmonic diffuse irradiance.

These references describe the mathematical techniques. The implementation and authored environment here are independent repository source. The environment does not add ray tracing, reflections of neighboring scene objects, ambient occlusion, temporal accumulation or physical-GPU performance measurements.
