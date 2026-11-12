import { M4, V3 } from '@nurbsscene/geometry/math.js';

/** Column-major, right-handed WebGPU camera transform; near=1, far=0.
 * Keep binary64 through rebasing and projection, then encode uniforms as float32.
 * The adaptive clipping distances preserve Camera.matrices' public framing.
 */
export function reverseZMatrices(camera, width, height, origin = [0, 0, 0]) {
  if (!(width > 0 && height > 0) || !V3.finite(origin)) {
    throw new RangeError('A finite origin and positive viewport dimensions are required.');
  }
  const basis = camera.basis(origin), { eye } = basis;
  const backward = V3.scale(basis.forward, -1), { right, up } = basis;
  const view = [
    right[0], up[0], backward[0], 0,
    right[1], up[1], backward[1], 0,
    right[2], up[2], backward[2], 0,
    -V3.dot(right, eye), -V3.dot(up, eye), -V3.dot(backward, eye), 1,
  ];
  const near = Math.max(1e-4, camera.distance * 1e-5);
  const far = Math.max(camera.far, camera.distance * 20, near * 2);
  const aspect = width / height;
  let projection;
  if (camera.perspective) {
    const focal = 1 / Math.tan(camera.fov / 2);
    projection = [
      focal / aspect, 0, 0, 0, 0, focal, 0, 0,
      0, 0, near / (far - near), -1, 0, 0, near * (far / (far - near)), 0,
    ];
  } else {
    const half = camera.scale / 2;
    projection = [
      1 / (half * aspect), 0, 0, 0, 0, 1 / half, 0, 0,
      0, 0, 1 / (far - near), 0, 0, 0, far / (far - near), 1,
    ];
  }
  const matrix = M4.multiply(projection, view);
  if (!matrix.every(Number.isFinite) || !eye.every(Number.isFinite)) {
    throw new RangeError('Camera cannot produce a finite WebGPU transform.');
  }
  return { matrix, eye, near, far };
}

/** Texel-snapped directional shadow projection with ordinary WebGPU 0..1 depth. */
export function shadowMatrix(bounds, origin, lightDirection, resolution) {
  if (!V3.finite(origin) || !V3.finite(lightDirection) ||
      !Number.isInteger(resolution) || resolution < 1) {
    throw new RangeError('Invalid WebGPU shadow coordinates or resolution.');
  }
  if (!bounds || bounds.min.some((v, i) => v > bounds.max[i])) return M4.identity();
  if (!bounds.min.every(Number.isFinite) || !bounds.max.every(Number.isFinite)) {
    throw new RangeError('Shadow bounds must be finite.');
  }
  const backward = V3.unit(lightDirection);
  const guide = Math.abs(backward[2]) > .95 ? [0, 1, 0] : [0, 0, 1];
  const right = V3.unit(V3.cross(guide, backward)), up = V3.cross(backward, right);
  const radius = Math.max(V3.length(V3.sub(bounds.max, bounds.min)) * .525, .001);
  const texel = 2 * radius / resolution;
  const center = bounds.min.map((value, i) =>
    (value - origin[i]) / 2 + (bounds.max[i] - origin[i]) / 2);
  const x = Math.round(V3.dot(right, center) / texel) * texel;
  const y = Math.round(V3.dot(up, center) / texel) * texel;
  const z = V3.dot(backward, center) + 2 * radius;
  const view = [
    right[0], up[0], backward[0], 0,
    right[1], up[1], backward[1], 0,
    right[2], up[2], backward[2], 0, -x, -y, -z, 1,
  ];
  const near = radius * .5, far = radius * 3.5;
  const projection = [
    1 / radius, 0, 0, 0, 0, 1 / radius, 0, 0,
    0, 0, 1 / (near - far), 0, 0, 0, near / (near - far), 1,
  ];
  return M4.multiply(projection, view);
}

/** Conservative AABB test, without perspective division or corner allocation. */
export function isVisible(bounds, matrix, origin) {
  const center = bounds.min.map((value, i) =>
    (value - origin[i]) / 2 + (bounds.max[i] - origin[i]) / 2);
  const extent = bounds.max.map((value, i) => value / 2 - bounds.min[i] / 2);
  const outside = (a, b, c, d) => {
    const distance = a * center[0] + b * center[1] + c * center[2] + d;
    const support = Math.abs(a) * extent[0] + Math.abs(b) * extent[1] + Math.abs(c) * extent[2];
    return distance + support < -1e-12 * Math.max(1, Math.abs(distance) + support);
  };
  const m = matrix;
  return !outside(m[3] + m[0], m[7] + m[4], m[11] + m[8], m[15] + m[12]) &&
    !outside(m[3] - m[0], m[7] - m[4], m[11] - m[8], m[15] - m[12]) &&
    !outside(m[3] + m[1], m[7] + m[5], m[11] + m[9], m[15] + m[13]) &&
    !outside(m[3] - m[1], m[7] - m[5], m[11] - m[9], m[15] - m[13]) &&
    !outside(m[2], m[6], m[10], m[14]) &&
    !outside(m[3] - m[2], m[7] - m[6], m[11] - m[10], m[15] - m[14]);
}

export function alignedBytesPerRow(width) {
  if (!Number.isSafeInteger(width) || width < 1 || width > 0x3fffffff) {
    throw new RangeError('A positive supported capture width is required.');
  }
  return Math.ceil(width * 4 / 256) * 256;
}
