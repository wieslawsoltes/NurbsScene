import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { gunzipSync } from 'node:zlib';
import { decodeStudioEnvironment, STUDIO_ENVIRONMENT_GZIP_BASE64, STUDIO_ENVIRONMENT_SHA256 }
  from '../packages/graphics/environment.js';

const directory = new URL('../src/NurbsScene.Rendering/Lighting/', import.meta.url);
const canonical = readFileSync(new URL('studio.ibl.gz', directory));
const bytes = gunzipSync(canonical);
const manifest = JSON.parse(readFileSync(new URL('studio.json', directory), 'utf8'));
const digest = data => createHash('sha256').update(data).digest('hex');
function half(bits) {
  const sign = bits & 0x8000 ? -1 : 1, exponent = (bits >> 10) & 31, mantissa = bits & 1023;
  return sign * (exponent === 0 ? mantissa * 2 ** -24 : exponent === 31 ? Infinity : (1 + mantissa / 1024) * 2 ** (exponent - 15));
}

test('npm and embedded native IBL assets have identical source and integration provenance', () => {
  assert.deepEqual(Buffer.from(STUDIO_ENVIRONMENT_GZIP_BASE64, 'base64'), canonical);
  assert.equal(digest(bytes), STUDIO_ENVIRONMENT_SHA256);
  assert.equal(digest(bytes), manifest.payloadSha256);
  assert.equal(digest(canonical), manifest.compressedSha256);
  assert.equal(digest(readFileSync(new URL('studio.hdr', directory))), manifest.sourceSha256);
  const loader = readFileSync(new URL('../src/NurbsScene.Rendering/StudioEnvironmentData.cs', import.meta.url), 'utf8');
  assert.ok(loader.includes('"' + manifest.payloadSha256 + '"'), 'Native loader must validate the same payload hash.');
  assert.equal(bytes.length, manifest.payloadBytes);
  assert.equal(canonical.length, manifest.compressedBytes);
});

test('actual browser-compatible decompression yields complete six-face roughness mips and finite HDR data', async () => {
  const environment = await decodeStudioEnvironment();
  assert.strictEqual(await decodeStudioEnvironment(), environment, 'Do not decompress per frame or per renderer.');
  assert.equal(environment.cubeSize, 128); assert.equal(environment.mipCount, 8); assert.equal(environment.lutSize, 128);
  assert.equal(environment.sphericalHarmonics.length, 144);
  assert.equal(environment.brdfLut.length, 128 * 128 * 8);
  let offset = 176, maximum = 0;
  for (const [level, mip] of environment.mips.entries()) {
    assert.equal(mip.size, 128 >> level);
    assert.equal(mip.pixels.length, mip.size ** 2 * 6 * 8);
    assert.deepEqual(Buffer.from(mip.pixels), bytes.subarray(offset, offset + mip.pixels.length));
    offset += mip.pixels.length;
    const view = new DataView(mip.pixels.buffer, mip.pixels.byteOffset, mip.pixels.byteLength);
    for (let i = 0; i < view.byteLength; i += 2) {
      const value = half(view.getUint16(i, true));
      assert.ok(Number.isFinite(value) && value >= 0);
      if (i % 8 === 6) assert.equal(value, 1); else maximum = Math.max(maximum, value);
    }
  }
  assert.ok(maximum > 5, 'The environment must retain real HDR softbox radiance, not tone-mapped pixels.');
  assert.deepEqual(Buffer.from(environment.brdfLut), bytes.subarray(offset));
});

test('integrated BRDF retains Schlick limits and a bounded white-furnace response', async () => {
  const { brdfLut, lutSize } = await decodeStudioEnvironment();
  const view = new DataView(brdfLut.buffer, brdfLut.byteOffset, brdfLut.byteLength);
  const texel = (x, y) => [0, 1].map(c => half(view.getUint16((y * lutSize + x) * 8 + c * 2, true)));
  for (let y = 0; y < lutSize; y++) for (let x = 0; x < lutSize; x++) {
    const [a, b] = texel(x, y);
    assert.ok(Number.isFinite(a) && Number.isFinite(b) && a >= 0 && b >= 0 && a + b <= 1.015);
  }
  const normalMirror = texel(127, 0), grazingMirror = texel(0, 0), roughNormal = texel(127, 127);
  assert.ok(normalMirror[0] > .99 && normalMirror[1] < 1e-5);
  assert.ok(grazingMirror[1] > .95, 'Schlick Fresnel approaches one at grazing incidence.');
  assert.ok(roughNormal[0] > .25 && roughNormal[0] < .4, 'The diffuse GGX lobe must retain single-scatter masking loss.');
});
