import { createRenderer, RenderScene, Camera } from '../../packages/graphics/index.js';

// Procedural display fixtures, not a substitute for exact NURBS modeling tests.
// Each panel is a complete capture from the public production renderer. Material
// controls apply to the sphere and receiving plane; geometry stays identical.
function sphereFixture() {
  const vertices = [], indices = [], rings = 40, sectors = 80, radius = .8;
  for (let ring = 0; ring <= rings; ring++) {
    const theta = ring * Math.PI / rings;
    for (let sector = 0; sector <= sectors; sector++) {
      const phi = sector * 2 * Math.PI / sectors;
      const normal = [Math.sin(theta) * Math.cos(phi), Math.sin(theta) * Math.sin(phi), Math.cos(theta)];
      vertices.push(normal[0] * radius, normal[1] * radius, radius + normal[2] * radius, ...normal);
      if (ring < rings && sector < sectors) {
        const a = ring * (sectors + 1) + sector, b = a + sectors + 1;
        if (ring > 0) indices.push(a, b, a + 1);
        if (ring < rings - 1) indices.push(a + 1, b, b + 1);
      }
    }
  }
  return fixture('procedural-material-sphere', vertices, indices,
    { min: [-radius, -radius, 0], max: [radius, radius, radius * 2] }, [.85, .62, .28, 1]);
}

function fixture(id, vertices, indices, bounds, color) {
  const geometry = Object.freeze({ id, bounds: () => bounds });
  return { item: { id, geometry, color }, mesh: Object.freeze({ source: geometry, bounds,
    vertices: new Float32Array(vertices), indices: new Uint32Array(indices), lines: new Float32Array() }) };
}

function changedPixels(left, right, width, height) {
  let changed = 0;
  // Restrict comparison to the sphere region, excluding most of the receiving plane.
  for (let y = Math.floor(height * .2); y < height * .75; y++) {
    for (let x = Math.floor(width * .3); x < width * .7; x++) {
      const pixel = (y * width + x) * 4;
      if ([0, 1, 2].some(channel => Math.abs(left[pixel + channel] - right[pixel + channel]) > 4)) changed++;
    }
  }
  return changed;
}

export async function captureMaterialShowcase(canvas, software) {
  const width = 320, height = 270, roughnesses = [.08, .32, .8], metallics = [0, 1];
  canvas.width = width; canvas.height = height;
  const { renderer } = await createRenderer(canvas, { backend: 'webgpu', forceFallbackAdapter: software,
    shadowResolution: 1024, shadows: true, ambientIntensity: .8, lightIntensity: 1.6,
    lightDirection: [-.6, -.45, .7], bloomStrength: .04 });
  const checks = [], panels = [];
  const check = (condition, description) => { if (!condition) throw new Error(description); checks.push(description); };
  renderer.device.pushErrorScope('validation');
  try {
    const plane = fixture('procedural-receiving-plane', [
      -2.2, -2.2, 0, 0, 0, 1, 2.2, -2.2, 0, 0, 0, 1,
      2.2, 2.2, 0, 0, 0, 1, -2.2, 2.2, 0, 0, 0, 1,
    ], [0, 1, 2, 0, 2, 3], { min: [-2.2, -2.2, 0], max: [2.2, 2.2, 0] }, [.40, .43, .46, 1]);
    const fixtures = [plane, sphereFixture()], scene = new RenderScene();
    scene.showGrid = false; scene.showIsocurves = false;
    scene.items = fixtures.map(value => value.item);
    scene.meshes = new Map(fixtures.map(value => [value.item.geometry.id, value.mesh]));
    scene.revision++;
    renderer.setScene(scene);
    const uploadCount = renderer.geometryUploads;
    const residentBuffers = fixtures.map(value => renderer.buffers.get(value.item.geometry.id).vertex);
    const camera = new Camera('Perspective');
    Object.assign(camera, { target: [0, 0, .65], distance: 5.2, azimuth: -.95, elevation: .38 });
    const views = [{ name: 'Perspective', x: 0, y: 0, width, height, camera }];
    const captures = [];
    for (const [row, metallic] of metallics.entries()) {
      for (const [column, roughness] of roughnesses.entries()) {
        renderer.setOptions({ metallic, roughness });
        const rgba = await renderer.render(views, { capture: true });
        check(rgba.length === width * height * 4 && rgba.every((value, index) => index % 4 !== 3 || value === 255),
          `material panel ${row},${column} contains complete opaque RGBA readback`);
        captures.push(rgba); panels.push({ row, column, metallic, roughness });
      }
    }
    renderer.setOptions({ ambientIntensity: 0 });
    const environmentOff = await renderer.render(views, { capture: true });
    const environmentChanged = changedPixels(captures[5], environmentOff, width, height);
    const dielectricRoughnessChanged = changedPixels(captures[0], captures[2], width, height);
    const metallicRoughnessChanged = changedPixels(captures[3], captures[5], width, height);
    check(environmentChanged > 500, 'image lighting changes the actual material sphere readback');
    check(dielectricRoughnessChanged > 500 && metallicRoughnessChanged > 500,
      'roughness visibly changes dielectric and metallic sphere reflections');
    check(renderer.geometryUploads === uploadCount && fixtures.every((value, index) =>
      renderer.buffers.get(value.item.geometry.id).vertex === residentBuffers[index]),
      'every material and illumination variation reuses the original geometry buffers');
    check(renderer.shadowPasses === 1, 'all material variations reuse one directional shadow map');
    await renderer.device.queue.onSubmittedWorkDone();
    check(!(await renderer.device.popErrorScope()), 'material showcase has no GPU validation errors');
    check(renderer.errors.length === 0, 'material showcase has no unexpected device loss or GPU errors');
    const outputWidth = width * 3, outputHeight = height * 2, output = new Uint8Array(outputWidth * outputHeight * 4);
    for (const [index, rgba] of captures.entries()) {
      const x = index % 3 * width, y = Math.floor(index / 3) * height;
      for (let row = 0; row < height; row++) {
        output.set(rgba.subarray(row * width * 4, (row + 1) * width * 4), ((y + row) * outputWidth + x) * 4);
      }
    }
    const chunks = [];
    for (let offset = 0; offset < output.length; offset += 32768)
      chunks.push(String.fromCharCode(...output.subarray(offset, offset + 32768)));
    return { width: outputWidth, height: outputHeight, rgbaBase64: btoa(chunks.join('')), checks, panels,
      fixture: 'procedural tessellated sphere and receiving plane; public production renderer; unmodified RGBA captures',
      environmentChanged, dielectricRoughnessChanged, metallicRoughnessChanged,
      geometryUploads: uploadCount, shadowPasses: renderer.shadowPasses, frames: renderer.frames,
      adapter: renderer.info, sampleCount: renderer.metrics.sampleCount };
  } finally { renderer.dispose(); }
}
