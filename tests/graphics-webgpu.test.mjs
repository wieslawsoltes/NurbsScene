import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { Camera, viewportLayout, createRenderer, WebGpuRenderer, WebGl2Renderer } from '../packages/graphics/index.js';
import { M4, V3 } from '../packages/geometry/math.js';
import { SHADERS, SHADER_SOURCE_SHA256 } from '../packages/graphics/shaders.js';
import { reverseZMatrices, shadowMatrix, isVisible, alignedBytesPerRow } from '../packages/graphics/render-math.js';

test('published shaders are exactly the authoritative Uno WGSL, with matching provenance', () => {
  for (const [name, source] of Object.entries(SHADERS)) {
    const authoritative = readFileSync(new URL('../src/NurbsScene.Rendering/Shaders/' + name + '.wgsl', import.meta.url), 'utf8');
    assert.equal(source, authoritative, 'Regenerate packages/graphics/shaders.js after changing canonical WGSL.');
    assert.equal(SHADER_SOURCE_SHA256[name], createHash('sha256').update(authoritative).digest('hex'));
  }
});

test('auto and explicit WebGPU fail without an API, preserving the original canvas', async () => {
  const descriptor = Object.getOwnPropertyDescriptor(globalThis, 'navigator');
  Object.defineProperty(globalThis, 'navigator', { configurable: true, value: {} });
  let contextCalls = 0, replacementCalls = 0;
  const canvas = {
    getContext() { contextCalls++; throw new Error('An unsupported API must not acquire a rendering context.'); },
    cloneNode() { replacementCalls++; return this; },
    replaceWith() { replacementCalls++; },
  };
  try {
    for (const backend of ['auto', 'webgpu']) {
      await assert.rejects(createRenderer(canvas, { backend }), /WebGPU.*required.*secure/i);
    }
    assert.equal(contextCalls, 0);
    assert.equal(replacementCalls, 0);
  } finally {
    if (descriptor) Object.defineProperty(globalThis, 'navigator', descriptor); else delete globalThis.navigator;
  }
});

test('legacy GL entrypoints reject explicitly with asynchronous WebGPU migration guidance', async () => {
  const canvas = { getContext() { assert.fail('Legacy API must never acquire a context.'); } };
  await assert.rejects(createRenderer(canvas, { backend: 'webgl2' }), /removed.*await createRenderer.*webgpu/i);
  await assert.rejects(WebGl2Renderer.create(canvas), /removed.*WebGpuRenderer.create/);
  assert.throws(() => new WebGl2Renderer(canvas), /removed.*asynchronous/);
  assert.throws(() => new WebGpuRenderer(canvas), /await WebGpuRenderer.create/);
  await assert.rejects(createRenderer(canvas, { backend: 'canvas' }), TypeError);
});

test('reverse-Z projection maps actual near/far camera points to one/zero in both camera modes', () => {
  for (const kind of ['Perspective', 'Top']) {
    const camera = new Camera(kind);
    const { matrix, near, far } = reverseZMatrices(camera, 801, 599);
    const { eye, forward } = camera.basis();
    const nearPoint = V3.add(eye, V3.scale(forward, near));
    const farPoint = V3.add(eye, V3.scale(forward, far));
    assert.ok(Math.abs(M4.point(matrix, nearPoint)[2] - 1) < 1e-8);
    assert.ok(Math.abs(M4.point(matrix, farPoint)[2]) < 1e-12);
    const target = M4.point(matrix, camera.target);
    assert.ok(Math.abs(target[0]) < 1e-12 && Math.abs(target[1]) < 1e-12);
    assert.ok(target[2] > 0 && target[2] < 1);
  }
});

test('binary64 rebasing preserves framing and visibility at billion-unit translations', () => {
  const camera = new Camera('Top');
  camera.target = [0, 0, 0]; camera.distance = 100; camera.scale = 50;
  const first = reverseZMatrices(camera, 800, 600);
  const offset = [1e9, -2e9, 3e9];
  camera.target = [...offset];
  const second = reverseZMatrices(camera, 800, 600, offset);
  first.matrix.forEach((value, i) => assert.ok(Math.abs(value - second.matrix[i]) < 1e-12));
  const bounds = { min: offset.map(value => value - 2), max: offset.map(value => value + 2) };
  assert.equal(isVisible(bounds, second.matrix, offset), true);
  const behind = { min: V3.add(offset, [-2, -2, 101]), max: V3.add(offset, [2, 2, 103]) };
  assert.equal(isVisible(behind, second.matrix, offset), false);
  const outside = { min: V3.add(offset, [100, 100, -2]), max: V3.add(offset, [102, 102, 2]) };
  assert.equal(isVisible(outside, second.matrix, offset), false);
});

test('directional shadow projection contains every corner, including a large rebased model', () => {
  const origin = [1e9, -2e9, 3e9];
  const bounds = { min: V3.add(origin, [-20, -5, -3]), max: V3.add(origin, [40, 9, 25]) };
  const matrix = shadowMatrix(bounds, origin, [-.45, -.6, 1], 2048);
  for (let mask = 0; mask < 8; mask++) {
    const point = [0, 1, 2].map(axis => (mask & (1 << axis) ? bounds.max : bounds.min)[axis] - origin[axis]);
    const clip = M4.point(matrix, point);
    assert.ok(Math.abs(clip[0]) <= 1 && Math.abs(clip[1]) <= 1, 'Every corner must fit the shadow image.');
    assert.ok(clip[2] >= 0 && clip[2] <= 1, 'Shadow depth must use WebGPU zero-to-one clip space.');
  }
});

test('capture rows retain odd widths with the required 256-byte transfer alignment', () => {
  for (const width of [1, 63, 64, 65, 129, 1001]) {
    const pitch = alignedBytesPerRow(width);
    assert.equal(pitch % 256, 0);
    assert.ok(pitch >= width * 4 && pitch - width * 4 < 256);
  }
  assert.throws(() => alignedBytesPerRow(0), RangeError);
  assert.throws(() => alignedBytesPerRow(Infinity), RangeError);
});

test('tiny canvas layouts remain valid positive rectangles through resize', () => {
  for (const [width, height] of [[1, 1], [2, 3], [3, 100], [4, 4], [129, 97]]) {
    const views = viewportLayout(width, height);
    assert.ok(views.length === 1 || views.length === 4);
    for (const view of views) {
      assert.ok(view.x >= 0 && view.y >= 0 && view.width > 0 && view.height > 0);
      assert.ok(view.x + view.width <= width && view.y + view.height <= height);
    }
  }
  assert.throws(() => viewportLayout(-1, 3), RangeError);
});
