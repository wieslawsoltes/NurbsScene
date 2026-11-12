// Retained import path for an actionable migration error. No GL API is used.
export const WEBGL2_MIGRATION_ERROR =
  'The WebGL2 renderer has been removed. Use await createRenderer(canvas, ' +
  '{ backend: "webgpu" }) or await WebGpuRenderer.create(canvas). ' +
  'WebGPU initialization is asynchronous and requires a supported secure context.';

/** @deprecated Use await WebGpuRenderer.create(canvas). */
export class WebGl2Renderer {
  constructor() { throw new Error(WEBGL2_MIGRATION_ERROR); }
  static async create() { throw new Error(WEBGL2_MIGRATION_ERROR); }
}
