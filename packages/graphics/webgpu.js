import { SHADERS, SHADER_SOURCE_SHA256 } from './shaders.js';
import { decodeStudioEnvironment, STUDIO_ENVIRONMENT_SHA256 } from './environment.js';
import { V3 } from '@nurbsscene/geometry/math.js';
import { reverseZMatrices, shadowMatrix, isVisible, alignedBytesPerRow, relativeClipPlane, normalizeRenderDirection } from './render-math.js';

const DEFAULTS = Object.freeze({
  exposure: 1, metallic: .12, roughness: .36, ambientIntensity: .55,
  lightIntensity: 3.2, bloomStrength: .055, bloomThreshold: 1.15, bloomKnee: .55,
  shadows: true, shadowResolution: 2048, lightDirection: Object.freeze([-.45, -.6, 1]),
});
const SURFACE_BUFFER = {
  arrayStride: 24,
  attributes: [
    { shaderLocation: 0, offset: 0, format: 'float32x3' },
    { shaderLocation: 1, offset: 12, format: 'float32x3' },
  ],
};
const ALPHA_BLEND = {
  color: { operation: 'add', srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha' },
  alpha: { operation: 'add', srcFactor: 'one', dstFactor: 'one-minus-src-alpha' },
};

function settingsFrom(options, previous = DEFAULTS) {
  const result = { ...previous };
  for (const key of Object.keys(DEFAULTS)) if (key in options) result[key] = options[key];
  const ranges = {
    exposure: [1e-4, 32], metallic: [0, 1], roughness: [.04, 1],
    ambientIntensity: [0, 32], lightIntensity: [0, 128], bloomStrength: [0, 1],
    bloomThreshold: [0, 128], bloomKnee: [1e-4, 128],
  };
  for (const [key, [minimum, maximum]] of Object.entries(ranges)) {
    if (!Number.isFinite(result[key]) || result[key] < minimum || result[key] > maximum) {
      throw new RangeError('Invalid WebGPU rendering setting: ' + key);
    }
  }
  if (typeof result.shadows !== 'boolean' || !Number.isInteger(result.shadowResolution) ||
      result.shadowResolution < 64 || result.shadowResolution > 8192 ||
      !V3.finite(result.lightDirection) || result.lightDirection.every(value => value === 0)) {
    throw new RangeError('Invalid WebGPU directional shadow settings.');
  }
  result.lightDirection = Object.freeze(normalizeRenderDirection(result.lightDirection));
  return Object.freeze(result);
}

function colorValues(color, fallback) {
  const result = color ?? fallback;
  if (!Array.isArray(result) || result.length < 3 || !result.slice(0, 4).every(Number.isFinite)) {
    throw new TypeError('Render colors must contain finite RGB(A) values.');
  }
  return [0, 1, 2, 3].map(i => Math.max(0, Math.min(1, result[i] ?? 1)));
}

/** Explicit WebGPU renderer sharing the Uno production WGSL.
 * Owns its browser device; immutable meshes survive camera, selection and style changes.
 * render() is synchronous; render(..., {capture:true}) returns tightly packed RGBA bytes.
 */
export class WebGpuRenderer {
  static async create(canvas, options = {}) {
    if (!globalThis.navigator?.gpu) {
      throw new Error('WebGPU is required but unavailable. Use a WebGPU-enabled browser and a secure HTTPS or localhost context.');
    }
    if (!canvas || typeof canvas.getContext !== 'function') throw new TypeError('A canvas is required.');
    const settings = settingsFrom(options);
    const adapter = await navigator.gpu.requestAdapter({
      powerPreference: 'high-performance',
      forceFallbackAdapter: options.forceFallbackAdapter === true,
    });
    if (!adapter) throw new Error('No WebGPU adapter is available. Enable WebGPU support on this device.');
    const device = await adapter.requestDevice();
    let renderer;
    try {
      renderer = new WebGpuRenderer(canvas, adapter, device, settings, options.onLost);
      device.pushErrorScope('out-of-memory');
      device.pushErrorScope('validation');
      let initializationError;
      try { await renderer.#initialize(); } catch (error) { initializationError = error; }
      const validation = await device.popErrorScope();
      const memory = await device.popErrorScope();
      if (initializationError) throw initializationError;
      if (validation || memory) throw new Error((validation ?? memory).message);
      if (renderer.lost) throw new Error('WebGPU device was lost during renderer initialization.');
      return renderer;
    } catch (error) {
      if (renderer) renderer.dispose(); else device.destroy();
      throw error;
    }
  }

  constructor(canvas, adapter, device, settings, onLost = () => {}) {
    if (!adapter || !device || !settings) {
      throw new TypeError('Use await WebGpuRenderer.create(canvas) to initialize WebGPU asynchronously.');
    }
    if (typeof onLost !== 'function') throw new TypeError('onLost must be a function.');
    this.canvas = canvas; this.adapter = adapter; this.device = device;
    this.settings = settings; this.onLost = onLost; this.backend = 'WebGPU';
    this.errors = []; this.buffers = new Map(); this.gridBuffers = [];
    this.resources = new Set(); this.captures = new Set();
    this.scene = null; this.views = []; this.lost = false; this.disposed = false;
    this.geometryUploads = 0; this.gridUploads = 0; this.frames = 0; this.shadowPasses = 0;
    this.frameCapacity = 0; this.objectCapacity = 0;
    this.uniformStride = Math.max(256, device.limits.minUniformBufferOffsetAlignment);
    this.format = navigator.gpu.getPreferredCanvasFormat();
    this.context = canvas.getContext('webgpu');
    if (!this.context) throw new Error('Could not acquire the WebGPU canvas context.');
    const info = adapter.info;
    this.info = {
      backend: this.backend, vendor: info?.vendor ?? '', architecture: info?.architecture ?? '',
      description: info?.description ?? '',
      software: info?.isFallbackAdapter === true ||
        /swiftshader|software|llvmpipe/i.test((info?.architecture ?? '') + ' ' + (info?.description ?? '')),
      api: 'WebGPU', precision: 'float64 modeling / camera-relative float32 display',
      sampleCount: 4, depth: 'reverse-Z / depth32float',
      color: 'linear rgba16float HDR / ACES fit / sRGB',
      transparency: 'weighted blended order-independent transparency',
      lighting: 'GGX studio lighting / directional PCF shadows / prefiltered HDR image-based lighting',
      environmentSourceSha256: STUDIO_ENVIRONMENT_SHA256,
      shaderSourceSha256: SHADER_SOURCE_SHA256, shaderMessages: {},
    };
    this.errorListener = event => {
      this.errors.push(event.error.message);
      this.#reportError(event.error.message);
    };
    device.addEventListener('uncapturederror', this.errorListener);
    device.lost.then(reason => {
      this.lost = true;
      if (reason.reason !== 'destroyed' && !this.disposed) {
        const message = 'WebGPU device lost (' + reason.reason + '): ' + reason.message;
        this.errors.push(message); this.#reportError(message);
      }
    });
  }

  #reportError(message) {
    try { this.onLost(message); } catch (error) { this.errors.push('onLost callback: ' + error.message); }
  }
  #assertUsable() {
    if (this.disposed) throw new Error('WebGPU renderer has been disposed.');
    if (this.lost) throw new Error('WebGPU device is lost. Recreate the renderer with WebGpuRenderer.create(canvas).');
  }
  #own(resource) { this.resources.add(resource); return resource; }
  #destroy(resource) { if (resource) { resource.destroy(); this.resources.delete(resource); } }
  #buffer(size, usage, label, data) {
    size = Math.max(4, Math.ceil(size / 4) * 4);
    if (!Number.isSafeInteger(size) || size > this.device.limits.maxBufferSize) {
      throw new RangeError(label + ' exceeds the WebGPU buffer size limit.');
    }
    const buffer = this.#own(this.device.createBuffer({ size, usage, label }));
    try { if (data?.byteLength) this.device.queue.writeBuffer(buffer, 0, data); }
    catch (error) { this.#destroy(buffer); throw error; }
    return buffer;
  }
  #texture(width, height, format, sampleCount, usage, label, collection) {
    const texture = this.#own(this.device.createTexture({
      size: [width, height], format, sampleCount, usage, label,
    }));
    collection?.push(texture);
    return { texture, view: texture.createView() };
  }
  #destroyCollection(collection) { for (const texture of collection?.textures ?? []) this.#destroy(texture); }

  async #initialize() {
    const device = this.device, modules = {};
    if (this.settings.shadowResolution > device.limits.maxTextureDimension2D) {
      throw new RangeError('Shadow resolution exceeds this WebGPU device limit.');
    }
    for (const [name, code] of Object.entries(SHADERS)) {
      const module = device.createShaderModule({ code, label: 'NurbsScene shared ' + name });
      const info = await module.getCompilationInfo();
      this.info.shaderMessages[name] = [...info.messages].map(m =>
        ({ type: m.type, line: m.lineNum, column: m.linePos, message: m.message }));
      const errors = info.messages.filter(m => m.type === 'error');
      if (errors.length) throw new Error(name + ': ' + errors.map(e => e.message).join('\n'));
      modules[name] = module;
    }
    const stages = GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT;
    const uniform = (size, dynamic = false) => ({ type: 'uniform', hasDynamicOffset: dynamic, minBindingSize: size });
    this.frameLayout = device.createBindGroupLayout({ entries: [
      { binding: 0, visibility: stages, buffer: uniform(256, true) },
      { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'depth' } },
      { binding: 2, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'comparison' } },
      { binding: 3, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float', viewDimension: 'cube' } },
      { binding: 4, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
      { binding: 5, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
      { binding: 6, visibility: GPUShaderStage.FRAGMENT, buffer: uniform(144) },
    ] });
    this.shadowLayout = device.createBindGroupLayout({ entries: [
      { binding: 0, visibility: stages, buffer: uniform(256, true) },
    ] });
    this.objectLayout = device.createBindGroupLayout({ entries: [
      { binding: 0, visibility: stages, buffer: uniform(32, true) },
    ] });
    this.bloomLayout = device.createBindGroupLayout({ entries: [
      { binding: 0, visibility: GPUShaderStage.COMPUTE, texture: { sampleType: 'float' } },
      { binding: 1, visibility: GPUShaderStage.COMPUTE, storageTexture: { access: 'write-only', format: 'rgba16float' } },
      { binding: 2, visibility: GPUShaderStage.COMPUTE, buffer: uniform(32) },
      { binding: 3, visibility: GPUShaderStage.COMPUTE, texture: { sampleType: 'float' } },
      { binding: 4, visibility: GPUShaderStage.COMPUTE, texture: { sampleType: 'float' } },
    ] });
    this.compositeLayout = device.createBindGroupLayout({ entries: [
      ...[0, 1, 2, 3].map(binding =>
        ({ binding, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } })),
      { binding: 4, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
      { binding: 5, visibility: GPUShaderStage.FRAGMENT, buffer: uniform(32) },
    ] });
    const layout = device.createPipelineLayout({ bindGroupLayouts: [this.frameLayout, this.objectLayout] });
    const shadowLayout = device.createPipelineLayout({ bindGroupLayouts: [this.shadowLayout] });
    const bloomLayout = device.createPipelineLayout({ bindGroupLayouts: [this.bloomLayout] });
    const compositeLayout = device.createPipelineLayout({ bindGroupLayouts: [this.compositeLayout] });
    const add = { operation: 'add', srcFactor: 'one', dstFactor: 'one' };
    const reveal = { operation: 'add', srcFactor: 'zero', dstFactor: 'one-minus-src-alpha' };
    const scenePipeline = (fragment, targets, line = false) => device.createRenderPipelineAsync({
      label: 'NurbsScene ' + fragment, layout,
      vertex: { module: modules.scene, entryPoint: line ? 'lineVertex' : 'surfaceVertex',
        buffers: [{ ...SURFACE_BUFFER, ...(line ? { stepMode: 'instance' } : {}) }] },
      fragment: { module: modules.scene, entryPoint: fragment, targets },
      primitive: { topology: 'triangle-list', cullMode: 'none' },
      depthStencil: { format: 'depth32float', depthWriteEnabled: fragment === 'surfaceFragment', depthCompare: 'greater-equal' },
      multisample: { count: 4 },
    });
    const pipelines = await Promise.all([
      scenePipeline('surfaceFragment', [{ format: 'rgba16float' }]),
      scenePipeline('ghostFragment', [
        { format: 'rgba16float', blend: { color: add, alpha: add } },
        { format: 'r8unorm', blend: { color: reveal, alpha: reveal } },
      ]),
      scenePipeline('lineFragment', [{ format: 'rgba16float', blend: ALPHA_BLEND }], true),
      device.createRenderPipelineAsync({
        label: 'NurbsScene directional shadow', layout: shadowLayout,
        vertex: { module: modules.scene, entryPoint: 'shadowVertex', buffers: [SURFACE_BUFFER] },
        fragment: { module: modules.scene, entryPoint: 'shadowFragment', targets: [] },
        primitive: { topology: 'triangle-list', cullMode: 'none' },
        depthStencil: { format: 'depth32float', depthWriteEnabled: true, depthCompare: 'less-equal',
          depthBias: 2, depthBiasSlopeScale: 2 },
      }),
      device.createRenderPipelineAsync({
        label: 'NurbsScene HDR composite', layout: compositeLayout,
        vertex: { module: modules.composite, entryPoint: 'fullscreenVertex' },
        fragment: { module: modules.composite, entryPoint: 'compositeFragment', targets: [{ format: this.format }] },
        primitive: { topology: 'triangle-list' },
      }),
      ...['downsample', 'blurHorizontal', 'blurVertical'].map(entryPoint =>
        device.createComputePipelineAsync({ label: 'NurbsScene bloom ' + entryPoint, layout: bloomLayout,
          compute: { module: modules.bloom, entryPoint } })),
    ]);
    [this.opaque, this.transparent, this.lines, this.shadowPipeline, this.compositePipeline,
      this.downsamplePipeline, this.horizontalPipeline, this.verticalPipeline] = pipelines;
    this.shadowSampler = device.createSampler({ compare: 'less-equal', minFilter: 'linear', magFilter: 'linear' });
    this.linearSampler = device.createSampler({ minFilter: 'linear', magFilter: 'linear' });
    const usage = GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.TEXTURE_BINDING;
    this.shadow = this.#texture(this.settings.shadowResolution, this.settings.shadowResolution,
      'depth32float', 1, usage, 'NurbsScene directional shadow');
    this.bloomParameters = this.#buffer(32, GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST, 'Bloom parameters');
    this.compositeParameters = this.#buffer(32, GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST, 'Composite parameters');
    await this.#initializeEnvironment();
    this.#ensureArenas(5, 1);
    this.context.configure({ device, format: this.format, alphaMode: 'opaque',
      usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC });
  }

  async #initializeEnvironment() {
    const data = await decodeStudioEnvironment(), device = this.device;
    const usage = GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST;
    const texture = this.#own(device.createTexture({
      label: 'Canonical GGX prefiltered HDR studio', size: [data.cubeSize, data.cubeSize, 6],
      mipLevelCount: data.mipCount, format: 'rgba16float', usage,
    }));
    this.environment = { texture, view: texture.createView({ dimension: 'cube', mipLevelCount: data.mipCount, arrayLayerCount: 6 }) };
    for (const [mipLevel, mip] of data.mips.entries()) {
      device.queue.writeTexture({ texture, mipLevel }, mip.pixels,
        { bytesPerRow: mip.size * 8, rowsPerImage: mip.size }, [mip.size, mip.size, 6]);
    }
    this.environmentBrdf = this.#texture(data.lutSize, data.lutSize, 'rgba16float', 1, usage, 'Correlated Smith DFG');
    device.queue.writeTexture({ texture: this.environmentBrdf.texture }, data.brdfLut,
      { bytesPerRow: data.lutSize * 8, rowsPerImage: data.lutSize }, [data.lutSize, data.lutSize]);
    this.environmentIrradiance = this.#buffer(144, GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
      'Cosine-convolved studio spherical harmonics', data.sphericalHarmonics);
    this.environmentSampler = device.createSampler({ minFilter: 'linear', magFilter: 'linear', mipmapFilter: 'linear',
      addressModeU: 'clamp-to-edge', addressModeV: 'clamp-to-edge', addressModeW: 'clamp-to-edge', lodMaxClamp: data.mipCount - 1 });
  }

  setOptions(options) {
    this.#assertUsable();
    const settings = settingsFrom(options, this.settings);
    if (settings.shadowResolution !== this.settings.shadowResolution) {
      throw new RangeError('Choose shadowResolution when creating the renderer; recreate it to change that allocation.');
    }
    this.settings = settings;
    return settings;
  }

  #ensureArenas(frames, objects) {
    let frameChanged = false, objectChanged = false;
    for (const [kind, count] of [['frame', frames], ['object', Math.max(1, objects)]]) {
      if (this[kind + 'Capacity'] >= count) continue;
      let capacity = Math.max(1, this[kind + 'Capacity']);
      while (capacity < count) capacity *= 2;
      const size = capacity * this.uniformStride;
      if (size > 0x100000000) throw new RangeError('Uniform arena exceeds WebGPU dynamic-offset range.');
      const buffer = this.#buffer(size, GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST, kind + ' uniforms');
      this.#destroy(this[kind + 'Buffer']);
      this[kind + 'Buffer'] = buffer; this[kind + 'Capacity'] = capacity;
      this[kind + 'Data'] = new Float32Array(size / 4);
      if (kind === 'frame') frameChanged = true; else objectChanged = true;
    }
    if (frameChanged) {
      this.frameGroup = this.device.createBindGroup({ layout: this.frameLayout, entries: [
        { binding: 0, resource: { buffer: this.frameBuffer, size: 256 } },
        { binding: 1, resource: this.shadow.view }, { binding: 2, resource: this.shadowSampler },
        { binding: 3, resource: this.environment.view }, { binding: 4, resource: this.environmentSampler },
        { binding: 5, resource: this.environmentBrdf.view }, { binding: 6, resource: { buffer: this.environmentIrradiance, size: 144 } },
      ] });
      this.shadowGroup = this.device.createBindGroup({ layout: this.shadowLayout, entries: [
        { binding: 0, resource: { buffer: this.frameBuffer, size: 256 } },
      ] });
    }
    if (objectChanged) this.objectGroup = this.device.createBindGroup({ layout: this.objectLayout, entries: [
      { binding: 0, resource: { buffer: this.objectBuffer, size: 32 } },
    ] });
  }

  #upload(mesh) {
    const owned = [];
    try {
      const make = (data, usage, label) => {
        const buffer = this.#buffer(data.byteLength, usage | GPUBufferUsage.COPY_DST, label, data);
        owned.push(buffer); return buffer;
      };
      const result = {
        mesh, vertex: make(mesh.vertices, GPUBufferUsage.VERTEX, 'NURBS surface vertices'),
        index: make(mesh.indices, GPUBufferUsage.INDEX, 'NURBS surface indices'),
        line: make(mesh.lines, GPUBufferUsage.VERTEX, 'NURBS curve ribbons'),
      };
      this.geometryUploads++;
      return result;
    } catch (error) { for (const buffer of owned) this.#destroy(buffer); throw error; }
  }
  #deleteMesh(bundle) {
    this.#destroy(bundle.vertex); this.#destroy(bundle.index); this.#destroy(bundle.line);
  }

  setScene(scene) {
    this.#assertUsable();
    if (!scene || !(scene.meshes instanceof Map) || !V3.finite(scene.origin)) {
      throw new TypeError('A RenderScene with finite origin is required.');
    }
    const buffers = new Map(), fresh = [], freshGrids = [];
    let grids = this.gridBuffers;
    try {
      for (const [id, mesh] of scene.meshes) {
        let bundle = this.buffers.get(id);
        if (!bundle || bundle.mesh !== mesh) { bundle = this.#upload(mesh); fresh.push(bundle); }
        buffers.set(id, bundle);
      }
      if (this.gridSource !== scene.grid) {
        grids = scene.grid.map(grid => {
          const buffer = this.#buffer(grid.lines.byteLength, GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
            'Construction grid ribbons', grid.lines);
          freshGrids.push(buffer); this.gridUploads++;
          return { ...grid, buffer };
        });
      }
    } catch (error) {
      for (const bundle of fresh) this.#deleteMesh(bundle);
      for (const buffer of freshGrids) this.#destroy(buffer);
      throw error;
    }
    for (const [id, bundle] of this.buffers) if (buffers.get(id) !== bundle) this.#deleteMesh(bundle);
    if (grids !== this.gridBuffers) for (const grid of this.gridBuffers) this.#destroy(grid.buffer);
    if (this.scene !== scene || this.syncedMeshes !== scene.meshes) this.shadowKey = null;
    this.scene = scene; this.buffers = buffers; this.gridBuffers = grids;
    this.syncedMeshes = scene.meshes; this.gridSource = scene.grid;
  }

  #resize(width, height, transparent) {
    let changed = false;
    const attachment = GPUTextureUsage.RENDER_ATTACHMENT;
    const sampled = attachment | GPUTextureUsage.TEXTURE_BINDING;
    if (this.targets?.width !== width || this.targets?.height !== height) {
      const next = { width, height, halfWidth: Math.ceil(width / 2), halfHeight: Math.ceil(height / 2), textures: [] };
      try {
        const make = (format, samples, usage, label, w = width, h = height) =>
          this.#texture(w, h, format, samples, usage, label, next.textures);
        next.hdr = make('rgba16float', 1, sampled, 'Resolved linear HDR');
        next.hdrMs = make('rgba16float', 4, attachment, '4x MSAA HDR');
        next.depth = make('depth32float', 4, attachment, '4x reverse-Z depth');
        const bloomUsage = GPUTextureUsage.STORAGE_BINDING | GPUTextureUsage.TEXTURE_BINDING;
        next.bloomA = make('rgba16float', 1, bloomUsage, 'Bloom A', next.halfWidth, next.halfHeight);
        next.bloomB = make('rgba16float', 1, bloomUsage, 'Bloom B', next.halfWidth, next.halfHeight);
      } catch (error) { this.#destroyCollection(next); throw error; }
      this.#destroyCollection(this.targets); this.targets = next; changed = true;
    }
    const oitWidth = transparent ? width : 1, oitHeight = transparent ? height : 1;
    if (this.oit?.width !== oitWidth || this.oit?.height !== oitHeight) {
      const next = { width: oitWidth, height: oitHeight, textures: [] };
      try {
        const make = (format, samples, usage, label) =>
          this.#texture(oitWidth, oitHeight, format, samples, usage, label, next.textures);
        next.accum = make('rgba16float', 1, sampled, 'OIT accumulation');
        next.accumMs = make('rgba16float', 4, attachment, '4x OIT accumulation');
        next.reveal = make('r8unorm', 1, sampled, 'OIT revealage');
        next.revealMs = make('r8unorm', 4, attachment, '4x OIT revealage');
      } catch (error) { this.#destroyCollection(next); throw error; }
      this.#destroyCollection(this.oit); this.oit = next; changed = true;
    }
    if (changed) {
      // OIT may transition between a full image and neutral 1x1 targets without
      // resizing HDR. Rebuild every binding that reads it after both sets exist.
      const group = (source, destination) => this.device.createBindGroup({ layout: this.bloomLayout, entries: [
        { binding: 0, resource: source.view }, { binding: 1, resource: destination.view },
        { binding: 2, resource: { buffer: this.bloomParameters, size: 32 } },
        { binding: 3, resource: this.oit.accum.view }, { binding: 4, resource: this.oit.reveal.view },
      ] });
      this.targets.downsampleGroup = group(this.targets.hdr, this.targets.bloomA);
      this.targets.horizontalGroup = group(this.targets.bloomA, this.targets.bloomB);
      this.targets.verticalGroup = group(this.targets.bloomB, this.targets.bloomA);
      this.compositeGroup = this.device.createBindGroup({ layout: this.compositeLayout, entries: [
        { binding: 0, resource: this.targets.hdr.view }, { binding: 1, resource: this.oit.accum.view },
        { binding: 2, resource: this.oit.reveal.view }, { binding: 3, resource: this.targets.bloomA.view },
        { binding: 4, resource: this.linearSampler },
        { binding: 5, resource: { buffer: this.compositeParameters, size: 32 } },
      ] });
    }
  }

  #writeObject(slot, color, metallic, roughness, width = 1) {
    if (![metallic, roughness, width].every(Number.isFinite)) throw new TypeError('Material values must be finite.');
    const offset = slot * this.uniformStride / 4;
    this.objectData.set(color, offset);
    this.objectData.set([metallic, roughness, width, 0], offset + 4);
    return slot * this.uniformStride;
  }
  #writeFrame(slot, matrix, shadow, eye, width, height) {
    const offset = slot * this.uniformStride / 4, data = this.frameData;
    data.fill(0, offset, offset + 64);
    data.set(matrix, offset); data.set(shadow, offset + 16);
    data.set([...eye, this.settings.exposure], offset + 32);
    if (this.scene.clipPlane) {
      data.set(relativeClipPlane(this.scene.clipPlane, this.scene.origin), offset + 36);
      data[offset + 48] = 1;
    }
    data.set([width, height, 1 / width, 1 / height], offset + 40);
    data.set([...this.settings.lightDirection, this.settings.shadows ? 1 : 0], offset + 44);
    data[offset + 49] = this.settings.ambientIntensity; data[offset + 50] = this.settings.lightIntensity;
    for (let i = offset; i < offset + 64; i++) {
      if (!Number.isFinite(data[i])) throw new RangeError('Scene exceeds finite float32 display range. Rebase nearer the model.');
    }
    return slot * this.uniformStride;
  }

  #prepare(views, width, height) {
    if (!Array.isArray(views) || views.length > 4) throw new RangeError('Provide at most four CAD viewports.');
    const scene = this.scene, settings = this.settings;
    if (!['Shaded', 'Ghosted', 'Wireframe'].includes(scene.mode)) throw new TypeError('Unsupported display mode.');
    const draws = scene.drawItems(), objectCount = this.gridBuffers.length + draws.length * 2;
    this.#ensureArenas(views.length + 1, objectCount);
    const dpr = Number.isFinite(globalThis.devicePixelRatio) ? Math.max(.1, globalThis.devicePixelRatio) : 1;
    let slot = 0;
    for (const grid of this.gridBuffers) grid.offset = this.#writeObject(slot++, colorValues(grid.color), 0, .5, dpr);
    for (const draw of draws) {
      const { item, selected } = draw;
      const base = colorValues(item.color, [.54, .64, .69, 1]);
      draw.color = selected ? [1, .69, .1, base[3]] : base;
      if (scene.mode === 'Ghosted') draw.color[3] *= .28;
      draw.transparent = scene.mode !== 'Wireframe' && draw.color[3] < .999;
      draw.surfaceOffset = this.#writeObject(slot++, draw.color, item.metallic ?? settings.metallic,
        item.roughness ?? settings.roughness);
      const lineColor = selected ? [1, .78, .05, 1] : draw.mesh.indices.length ?
        [.17, .22, .24, .65] : colorValues(item.color, [.15, .2, .25, 1]);
      draw.lineOffset = this.#writeObject(slot++, lineColor, 0, .5, (selected ? 2.6 : 1.35) * dpr);
    }
    const bounds = { min: [Infinity, Infinity, Infinity], max: [-Infinity, -Infinity, -Infinity] };
    for (const { mesh } of draws) for (let i = 0; i < 3; i++) {
      bounds.min[i] = Math.min(bounds.min[i], mesh.bounds.min[i]);
      bounds.max[i] = Math.max(bounds.max[i], mesh.bounds.max[i]);
    }
    const shadow = shadowMatrix(bounds, scene.origin, settings.lightDirection, settings.shadowResolution);
    const prepared = [];
    let culledItems = 0;
    for (let index = 0; index < views.length; index++) {
      const view = views[index];
      if (![view.x, view.y, view.width, view.height].every(Number.isInteger)) throw new TypeError('Viewport rectangles must use integer pixels.');
      if (view.width <= 0 || view.height <= 0) continue;
      if (view.x < 0 || view.y < 0 || view.x + view.width > width || view.y + view.height > height) {
        throw new RangeError('Viewport extends outside its canvas.');
      }
      const camera = reverseZMatrices(view.camera, view.width, view.height, scene.origin);
      const visible = draws.filter(draw => {
        // Curves have a screen-space width: retain them at an AABB/frustum boundary.
        const keep = !draw.mesh.indices.length || isVisible(draw.mesh.bounds, camera.matrix, scene.origin);
        if (!keep) culledItems++;
        return keep;
      });
      prepared.push({ ...view, draws: visible, offset: this.#writeFrame(index, camera.matrix, shadow,
        camera.eye, view.width, view.height) });
    }
    const shadowOffset = this.#writeFrame(views.length, shadow, shadow, [0, 0, 0],
      settings.shadowResolution, settings.shadowResolution);
    this.device.queue.writeBuffer(this.frameBuffer, 0, this.frameData.buffer, 0, (views.length + 1) * this.uniformStride);
    if (objectCount) this.device.queue.writeBuffer(this.objectBuffer, 0, this.objectData.buffer, 0, objectCount * this.uniformStride);
    const transparent = scene.mode !== 'Wireframe' &&
      draws.some(draw => draw.mesh.indices.length && draw.transparent && draw.color[3] > 0);
    return { views: prepared, draws, shadowOffset, transparent, culledItems };
  }

  #postParameters(views, width, height) {
    const composite = new Float32Array([this.settings.exposure, this.settings.bloomStrength, 1, 0, 0, 0, 0, 0]);
    const bloom = new Float32Array([this.settings.bloomThreshold, this.settings.bloomKnee, 0, 0, 0, 0, 0, 0]);
    if (views.length === 4) {
      const sorted = [...views].sort((a, b) => a.y - b.y || a.x - b.x), [a, b, c, d] = sorted;
      if (a.x === 0 && a.y === 0 && b.y === 0 && c.x === 0 &&
          b.x === d.x && c.y === d.y && a.width === c.width && a.height === b.height &&
          b.width === d.width && c.height === d.height && b.x >= a.width && c.y >= a.height &&
          b.x + b.width === width && c.y + c.height === height) {
        composite[3] = 1; bloom[2] = 1;
        const partition = [a.width, a.height, b.x - a.width, c.y - a.height];
        composite.set(partition, 4); bloom.set(partition, 4);
      } else composite[1] = 0;
    } else if (views.length > 1 || (views.length === 1 &&
        (views[0].x !== 0 || views[0].y !== 0 || views[0].width !== width || views[0].height !== height))) composite[1] = 0;
    // Arbitrary disjoint layouts retain all rendering; bloom is confined to standard one/four-view layouts.
    this.device.queue.writeBuffer(this.compositeParameters, 0, composite);
    this.device.queue.writeBuffer(this.bloomParameters, 0, bloom);
    return composite[1] > 0;
  }

  #setView(pass, view) {
    pass.setViewport(view.x, view.y, view.width, view.height, 0, 1);
    pass.setScissorRect(view.x, view.y, view.width, view.height);
    pass.setBindGroup(0, this.frameGroup, [view.offset]);
  }
  #mesh(pass, draw, metrics, countTriangles = true) {
    const buffers = this.buffers.get(draw.item.geometry.id);
    pass.setVertexBuffer(0, buffers.vertex); pass.setIndexBuffer(buffers.index, 'uint32');
    pass.drawIndexed(draw.mesh.indices.length); metrics.draws++;
    if (countTriangles) metrics.triangles += draw.mesh.indices.length / 3;
  }
  #line(pass, buffer, count, metrics) {
    if (!count) return;
    pass.setVertexBuffer(0, buffer); pass.draw(6, count);
    metrics.draws++; metrics.lines += count;
  }
  #colorAttachment(multisample, resolved, clearValue) {
    return { view: multisample.view, resolveTarget: resolved.view, clearValue, loadOp: 'clear', storeOp: 'discard' };
  }

  render(views = this.views, { capture = false } = {}) {
    this.#assertUsable();
    if (!this.scene) {
      if (capture) return Promise.reject(new Error('Set a RenderScene before capturing a frame.'));
      return null;
    }
    const started = performance.now();
    if (this.syncedMeshes !== this.scene.meshes || this.gridSource !== this.scene.grid) this.setScene(this.scene);
    const width = this.canvas.width, height = this.canvas.height;
    if (!Number.isInteger(width) || !Number.isInteger(height) || width < 1 || height < 1 ||
        width > this.device.limits.maxTextureDimension2D || height > this.device.limits.maxTextureDimension2D) {
      throw new RangeError('Canvas dimensions exceed the supported WebGPU texture range.');
    }
    const plan = this.#prepare(views, width, height);
    this.#resize(width, height, plan.transparent);
    const bloomEnabled = this.#postParameters(views, width, height);
    this.views = views;
    const metrics = { draws: 0, triangles: 0, lines: 0, objects: this.scene.items.length,
      culledItems: plan.culledItems, geometryUploads: this.geometryUploads, gridUploads: this.gridUploads,
      bufferBytes: [...this.scene.meshes.values()].reduce((n, mesh) =>
        n + mesh.vertices.byteLength + mesh.indices.byteLength + mesh.lines.byteLength, 0),
      sampleCount: 4, oitSize: [this.oit.width, this.oit.height] };
    const encoder = this.device.createCommandEncoder({ label: 'NurbsScene frame' });
    const scene = this.scene, settings = this.settings, targets = this.targets, oit = this.oit;
    const shadowKey = JSON.stringify([scene.revision, scene.origin, scene.clipPlane, scene.mode,
      settings.shadows, settings.lightDirection]);
    const shadowChanged = this.shadowKey !== shadowKey;
    if (shadowChanged) {
      const pass = encoder.beginRenderPass({ label: 'Directional shadow', colorAttachments: [],
        depthStencilAttachment: { view: this.shadow.view, depthClearValue: 1, depthLoadOp: 'clear', depthStoreOp: 'store' } });
      if (settings.shadows && scene.mode === 'Shaded') {
        pass.setPipeline(this.shadowPipeline); pass.setBindGroup(0, this.shadowGroup, [plan.shadowOffset]);
        for (const draw of plan.draws) if (draw.mesh.indices.length && !draw.transparent && draw.color[3] > 0) {
          this.#mesh(pass, draw, metrics, false);
        }
      }
      pass.end();
    }
    let pass = encoder.beginRenderPass({ label: '4x MSAA linear HDR',
      // Linear HDR, resolving to reference sRGB #9DA3AA after ACES at exposure 1;
      // keep in sync with the managed WebGpuRenderer clear.
      colorAttachments: [this.#colorAttachment(targets.hdrMs, targets.hdr, [.224357, .244384, .270711, 1])],
      depthStencilAttachment: { view: targets.depth.view, depthClearValue: 0, depthLoadOp: 'clear', depthStoreOp: 'store' },
    });
    for (const view of plan.views) {
      this.#setView(pass, view);
      if (scene.mode === 'Shaded') {
        pass.setPipeline(this.opaque);
        for (const draw of view.draws) if (draw.mesh.indices.length && !draw.transparent && draw.color[3] > 0) {
          pass.setBindGroup(1, this.objectGroup, [draw.surfaceOffset]); this.#mesh(pass, draw, metrics);
        }
      }
      pass.setPipeline(this.lines);
      if (scene.showGrid) for (const grid of this.gridBuffers) {
        if (grid.views && !grid.views.includes(view.name)) continue;
        pass.setBindGroup(1, this.objectGroup, [grid.offset]);
        this.#line(pass, grid.buffer, grid.lines.length / 6, metrics);
      }
      for (const draw of view.draws) if (scene.showIsocurves || !draw.mesh.indices.length ||
          scene.mode === 'Wireframe' || draw.selected) {
        pass.setBindGroup(1, this.objectGroup, [draw.lineOffset]);
        this.#line(pass, this.buffers.get(draw.item.geometry.id).line, draw.mesh.lines.length / 6, metrics);
      }
    }
    pass.end();
    pass = encoder.beginRenderPass({ label: 'Weighted blended transparency',
      colorAttachments: [this.#colorAttachment(oit.accumMs, oit.accum, [0, 0, 0, 0]),
        this.#colorAttachment(oit.revealMs, oit.reveal, [1, 1, 1, 1])],
      ...(plan.transparent ? { depthStencilAttachment: { view: targets.depth.view, depthReadOnly: true } } : {}),
    });
    if (plan.transparent) {
      pass.setPipeline(this.transparent);
      for (const view of plan.views) {
        this.#setView(pass, view);
        for (const draw of view.draws) if (draw.mesh.indices.length && draw.transparent && draw.color[3] > 0) {
          pass.setBindGroup(1, this.objectGroup, [draw.surfaceOffset]); this.#mesh(pass, draw, metrics);
        }
      }
    }
    pass.end();
    if (bloomEnabled) for (const [pipeline, group] of [
      [this.downsamplePipeline, targets.downsampleGroup],
      [this.horizontalPipeline, targets.horizontalGroup], [this.verticalPipeline, targets.verticalGroup],
    ]) {
      const compute = encoder.beginComputePass({ label: 'HDR bloom' });
      compute.setPipeline(pipeline); compute.setBindGroup(0, group);
      compute.dispatchWorkgroups(Math.ceil(targets.halfWidth / 8), Math.ceil(targets.halfHeight / 8));
      compute.end();
    }
    const texture = this.context.getCurrentTexture();
    pass = encoder.beginRenderPass({ label: 'ACES fit and sRGB output', colorAttachments: [
      { view: texture.createView(), clearValue: [0, 0, 0, 1], loadOp: 'clear', storeOp: 'store' },
    ] });
    pass.setPipeline(this.compositePipeline); pass.setBindGroup(0, this.compositeGroup); pass.draw(3);
    pass.end(); metrics.draws++;
    let readback, pitch;
    try {
      if (capture) {
        pitch = alignedBytesPerRow(width);
        readback = this.#buffer(pitch * height, GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ, 'Asynchronous RGBA capture');
        this.captures.add(readback);
        encoder.copyTextureToBuffer({ texture }, { buffer: readback, bytesPerRow: pitch, rowsPerImage: height },
          [width, height, 1]);
      }
      this.device.queue.submit([encoder.finish()]);
      if (shadowChanged) { this.shadowKey = shadowKey; this.shadowPasses++; }
    } catch (error) {
      this.captures.delete(readback); this.#destroy(readback); throw error;
    }
    this.frames++; metrics.frames = this.frames; metrics.shadowPasses = this.shadowPasses;
    metrics.cpuSubmissionMilliseconds = performance.now() - started;
    this.metrics = metrics;
    if (capture) return this.#read(readback, pitch, width, height);
    return metrics;
  }

  async #read(buffer, pitch, width, height) {
    try {
      await buffer.mapAsync(GPUMapMode.READ);
      const mapped = new Uint8Array(buffer.getMappedRange()), pixels = new Uint8Array(width * height * 4);
      for (let y = 0; y < height; y++) pixels.set(mapped.subarray(y * pitch, y * pitch + width * 4), y * width * 4);
      if (this.format.startsWith('bgra')) for (let i = 0; i < pixels.length; i += 4) {
        const red = pixels[i]; pixels[i] = pixels[i + 2]; pixels[i + 2] = red;
      }
      return pixels;
    } finally {
      if (buffer.mapState === 'mapped') buffer.unmap();
      this.captures.delete(buffer); this.#destroy(buffer);
    }
  }

  dispose() {
    if (this.disposed) return;
    this.disposed = true;
    this.device.removeEventListener('uncapturederror', this.errorListener);
    for (const resource of this.resources) resource.destroy();
    this.resources.clear(); this.captures.clear(); this.buffers.clear(); this.gridBuffers = [];
    this.scene = null; this.views = []; this.targets = null; this.oit = null;
    this.context?.unconfigure(); this.device.destroy();
  }
}
