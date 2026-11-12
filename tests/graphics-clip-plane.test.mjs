import test from 'node:test';
import assert from 'node:assert/strict';
import { relativeClipPlane, normalizeRenderDirection } from '../packages/graphics/render-math.js';

test('equivalent huge and tiny clipping equations produce identical finite GPU uniforms', () => {
  const equation = [1, -2, 1, -.75], origin = [1000, -2000, 3000];
  const expected = new Float32Array(relativeClipPlane(equation, origin));
  for (const scale of [1e-100, 1, 1e100, 1e300]) {
    const plane = relativeClipPlane(equation.map(value => value * scale), origin);
    assert.deepEqual(new Float32Array(plane), expected);
    for (const point of [[-1, -1, 0], [3, 0, 0], [0, 2, 0]]) {
      const original = equation[3] + point.reduce((sum, value, axis) => sum + value * equation[axis], 0);
      const local = plane[3] + point.reduce((sum, value, axis) => sum + (value - origin[axis]) * plane[axis], 0);
      assert.equal(Math.sign(local), Math.sign(original), 'Normalization must preserve the clipping half-space.');
    }
  }
  assert.deepEqual(relativeClipPlane(new Float64Array(equation), origin), relativeClipPlane(equation, origin));
});

test('normalization remains stable at finite binary64 extremes', () => {
  for (const direction of [[1e308, 1e308, 1e308], [Number.MIN_VALUE, 0, 0], [1e-300, -1e-300, 0]]) {
    const unit = normalizeRenderDirection(direction);
    assert.ok(unit.every(Number.isFinite));
    assert.ok(Math.abs(Math.hypot(...unit) - 1) < 1e-14);
    assert.deepEqual(relativeClipPlane([...direction, 0]).slice(0, 3), unit);
  }
});

test('invalid or unrepresentable relative clipping planes fail before GPU submission', () => {
  for (const plane of [[0, 0, 0, 1], [Infinity, 0, 0, 0], [1, 0, 0, 1e100], [1e-300, 0, 0, 1e300]]) {
    assert.throws(() => relativeClipPlane(plane), RangeError);
  }
  assert.throws(() => relativeClipPlane([1, 0, 0, 0], [1e100, 0, 0]), /Float32/);
  assert.deepEqual(relativeClipPlane([1, 0, 0, -1e12], [1e12, 0, 0]), [1, 0, 0, 0]);
});
