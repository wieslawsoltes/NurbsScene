import { WebGpuRenderer } from './webgpu.js';
import { WebGl2Renderer, WEBGL2_MIGRATION_ERROR } from './webgl2.js';

export { Camera, viewportLayout } from './camera.js';
export { RenderScene } from './scene.js';
export { buildDisplayMesh } from './mesh.js';
export { TriangleBvh, rayTriangle, pickCurve } from './picking.js';
export { WebGpuRenderer, WebGl2Renderer };

/** Initialize WebGPU without replacing the caller's canvas.
 * `auto` remains an alias for `webgpu`; initialization errors are propagated.
 */
export async function createRenderer(canvas, { backend = 'auto', ...options } = {}) {
  if (backend === 'webgl2') throw new Error(WEBGL2_MIGRATION_ERROR);
  if (backend !== 'auto' && backend !== 'webgpu') {
    throw new TypeError('Renderer backend must be auto or webgpu.');
  }
  const renderer = await WebGpuRenderer.create(canvas, options);
  return { renderer, canvas, failures: [] };
}
