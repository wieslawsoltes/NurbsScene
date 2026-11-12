import test from 'node:test';
import assert from 'node:assert/strict';
import { Camera } from '../packages/graphics/camera.js';
import { V3 } from '../packages/geometry/math.js';
import { reverseZMatrices, shadowMatrix, isVisible } from '../packages/graphics/render-math.js';

const near = (actual, expected) => assert.ok(
  Math.abs(actual - expected) <= 1e-13 * Math.max(1, Math.abs(expected)),
  `Expected ${expected}; got ${actual}.`);

test('sub-unit orbit offsets survive world translations larger than the camera distance', () => {
  for (const kind of ['Perspective', 'Top', 'Front', 'Right'])
    for (const distance of [.001, 7.25, 125])
      for (const origin of [[1e12, -2e12, 3e12], [1e16, -2e16, 3e16]]) {
        const camera = new Camera(kind), localTarget = [8, -16, 32];
        camera.target = [...localTarget]; camera.distance = distance;
        const expected = reverseZMatrices(camera, 1231, 759);
        const legacy = camera.matrices(1231, 759);
        const projected = camera.project(localTarget, 1231, 759);
        camera.target = V3.add(origin, localTarget);
        const actual = reverseZMatrices(camera, 1231, 759, origin);
        actual.matrix.forEach((value, i) => near(value, expected.matrix[i]));
        actual.eye.forEach((value, i) => near(value, expected.eye[i]));
        camera.matrices(1231, 759, origin).matrix.forEach((value, i) => near(value, legacy.matrix[i]));
        const translatedProjection = camera.project(camera.target, 1231, 759);
        near(translatedProjection.x, projected.x); near(translatedProjection.y, projected.y);
        near(translatedProjection.depth, distance);
      }
});

test('local shadow bounds and edge visibility survive unrepresentable world midpoints', () => {
  const origin = [1e16, -2e16, 3e16], local = { min: [0, 0, 0], max: [2, 12, 20] };
  const translated = { min: V3.add(local.min, origin), max: V3.add(local.max, origin) };
  const expected = shadowMatrix(local, [0, 0, 0], [-.45, -.6, 1], 2048);
  shadowMatrix(translated, origin, [-.45, -.6, 1], 2048).forEach((value, i) => near(value, expected[i]));
  const edgeOrigin = [1e16, 1e16, 1e16], edge = { min: [2, 0, 0], max: [4, 2, 2] };
  const matrix = [.8,0,0,0, 0,.5,0,0, 0,0,.25,0, -1,-.5,0,1];
  assert.ok(isVisible(edge, matrix, [0, 0, 0]));
  assert.ok(isVisible({ min: V3.add(edge.min, edgeOrigin), max: V3.add(edge.max, edgeOrigin) }, matrix, edgeOrigin));
});
