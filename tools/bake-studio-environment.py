#!/usr/bin/env python3
"""Bake the authored NurbsScene HDR studio image into the shared WebGPU IBL asset.

Offline only: Python 3.12 + numpy. Runtime hosts load the same precomputed bytes.
Run with --check to reproduce every committed byte without modifying the tree.
The source image, integration code and generated assets are original MIT work.
See docs/WEBGPU-ENVIRONMENT.md for the equations, format and primary references.
"""
from __future__ import annotations

import argparse
import base64
import gzip
import hashlib
import io
import json
import math
from pathlib import Path
import struct

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
DIRECTORY = ROOT / "src/NurbsScene.Rendering/Lighting"
CUBE_SIZE, MIP_COUNT, LUT_SIZE, SAMPLES = 128, 8, 128, 1024
IMAGE_WIDTH, IMAGE_HEIGHT = 512, 256
MAGIC = b"NSIBL001"
SH_FACTORS = np.array([.28209479177387814, .4886025119029199, .4886025119029199,
                      .4886025119029199, 1.0925484305920792, 1.0925484305920792,
                      .31539156525252005, 1.0925484305920792, .5462742152960396])


def normalize(value):
    return value / np.linalg.norm(value, axis=-1, keepdims=True)


def image_directions():
    theta = (np.arange(IMAGE_HEIGHT) + .5) * math.pi / IMAGE_HEIGHT
    phi = (np.arange(IMAGE_WIDTH) + .5) * 2 * math.pi / IMAGE_WIDTH - math.pi
    t, p = np.meshgrid(theta, phi, indexing="ij")
    return np.stack([np.sin(t) * np.cos(p), np.sin(t) * np.sin(p), np.cos(t)], axis=-1)


def smoothstep(low, high, value):
    t = np.clip((value - low) / (high - low), 0, 1)
    return t * t * (3 - 2 * t)


def authored_studio():
    """Z-up linear-radiance room with three finite angular softboxes and a ceiling."""
    direction = image_directions()
    sky = (direction[..., 2:3] + 1) * .5
    radiance = (1 - sky) * [.09, .075, .06] + sky * [.23, .29, .40]
    radiance += np.exp(-(direction[..., 2:3] / .12) ** 2) * [.045, .045, .04]
    for center, half_width, half_height, color in [
        ((-.6, -.45, .7), .52, .23, (5.0, 4.65, 4.1)),
        ((.65, .45, .35), .28, .72, (2.24, 2.52, 2.8)),
        ((-.1, .85, .35), .15, .55, (3.0, 3.4, 4.0)),
        ((0, 0, 1), .75, .50, (1.2, 1.15, 1.05)),
    ]:
        normal = normalize(np.array(center, dtype=np.float64))
        guide = np.array([0., 1., 0.]) if abs(normal[2]) > .95 else np.array([0., 0., 1.])
        right = normalize(np.cross(guide, normal))
        up = np.cross(normal, right)
        facing = np.sum(direction * normal, axis=-1)
        denominator = np.maximum(facing, 1e-12)
        x = np.abs(np.sum(direction * right, axis=-1)) / denominator
        y = np.abs(np.sum(direction * up, axis=-1)) / denominator
        coverage = (1 - smoothstep(half_width * .92, half_width, x)) \
            * (1 - smoothstep(half_height * .92, half_height, y)) * (facing > 0)
        radiance += coverage[..., None] * color
    return radiance


def radiance_rgbe(image):
    """Encode ordinary Radiance RGBE scanlines and return the exact decoded image."""
    maximum = np.max(image, axis=-1)
    _, exponent = np.frexp(maximum)
    scale = np.ldexp(np.ones_like(maximum), 8 - exponent)
    rgb = np.clip(np.floor(image * scale[..., None]), 0, 255).astype(np.uint8)
    encoded = np.concatenate([rgb, (exponent + 128).astype(np.uint8)[..., None]], axis=-1)
    decoded = rgb.astype(np.float64) * np.ldexp(np.ones_like(maximum), exponent - 8)[..., None]
    output = bytearray(b"#?RADIANCE\n# NurbsScene original MIT studio image\nFORMAT=32-bit_rle_rgbe\n\n")
    output += f"-Y {IMAGE_HEIGHT} +X {IMAGE_WIDTH}\n".encode("ascii")
    # Standard per-channel Radiance RLE, without a platform-specific image encoder.
    for row in encoded:
        output += bytes([2, 2, IMAGE_WIDTH >> 8, IMAGE_WIDTH & 255])
        for channel in range(4):
            values = row[:, channel].tobytes()
            cursor = 0
            while cursor < IMAGE_WIDTH:
                run = 1
                while run < 127 and cursor + run < IMAGE_WIDTH and values[cursor + run] == values[cursor]:
                    run += 1
                if run >= 4:
                    output += bytes([128 + run, values[cursor]])
                    cursor += run
                else:
                    start = cursor
                    cursor += 1
                    while cursor - start < 128 and cursor < IMAGE_WIDTH:
                        if cursor + 3 < IMAGE_WIDTH and len(set(values[cursor:cursor + 4])) == 1:
                            break
                        cursor += 1
                    output += bytes([cursor - start]) + values[start:cursor]
    return bytes(output), decoded


def sample_image(image, directions):
    """Bilinear lat/long sampling with horizontal wrap and clamped poles."""
    height, width = image.shape[:2]
    x = (np.arctan2(directions[..., 1], directions[..., 0]) / (2 * math.pi) + .5) * width - .5
    y = np.clip(np.arccos(np.clip(directions[..., 2], -1, 1)) / math.pi * height - .5, 0, height - 1)
    x0, y0 = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    tx, ty = (x - x0)[..., None], (y - y0)[..., None]
    x0 %= width
    x1, y1 = (x0 + 1) % width, np.minimum(y0 + 1, height - 1)
    return (image[y0, x0] * (1 - tx) + image[y0, x1] * tx) * (1 - ty) \
        + (image[y1, x0] * (1 - tx) + image[y1, x1] * tx) * ty


def cube_directions(face, size):
    """WebGPU cube layer order +X,-X,+Y,-Y,+Z,-Z; array rows follow +V."""
    coordinate = (np.arange(size) + .5) * 2 / size - 1
    u, v = np.meshgrid(coordinate, coordinate)
    one = np.ones_like(u)
    directions = [(one, -v, -u), (-one, -v, u), (u, one, v),
                  (u, -one, -v), (u, -v, one), (-u, -v, -one)]
    return normalize(np.stack(directions[face], axis=-1))


def hammersley(count):
    bits = np.arange(count, dtype=np.uint32)
    bits = (bits << 16) | (bits >> 16)
    bits = ((bits & 0x55555555) << 1) | ((bits & 0xAAAAAAAA) >> 1)
    bits = ((bits & 0x33333333) << 2) | ((bits & 0xCCCCCCCC) >> 2)
    bits = ((bits & 0x0F0F0F0F) << 4) | ((bits & 0xF0F0F0F0) >> 4)
    bits = ((bits & 0x00FF00FF) << 8) | ((bits & 0xFF00FF00) >> 8)
    return np.arange(count, dtype=np.float64) / count, bits.astype(np.float64) / 2**32


def importance_ggx(roughness, count=SAMPLES):
    u, v = hammersley(count)
    alpha_squared = np.asarray(roughness)[..., None] ** 4
    cosine = np.sqrt((1 - v) / ((1 - v) + alpha_squared * v))
    sine = np.sqrt(np.maximum(0, 1 - cosine * cosine))
    return np.stack([np.cos(2 * math.pi * u) * sine, np.sin(2 * math.pi * u) * sine, cosine], axis=-1)


def prefilter(image, directions, roughness):
    if roughness == 0:
        return sample_image(image, directions)
    half_local = importance_ggx(roughness)
    normals = directions.reshape(-1, 3)
    result = np.empty_like(normals)
    for start in range(0, len(normals), 256):
        normal = normals[start:start + 256]
        guide = np.tile([0., 0., 1.], (len(normal), 1))
        guide[np.abs(normal[:, 2]) > .999] = [0, 1, 0]
        tangent = normalize(np.cross(guide, normal))
        bitangent = np.cross(normal, tangent)
        half = tangent[:, None] * half_local[None, :, 0:1] \
            + bitangent[:, None] * half_local[None, :, 1:2] \
            + normal[:, None] * half_local[None, :, 2:3]
        light = 2 * half_local[None, :, 2:3] * half - normal[:, None]
        weight = np.maximum(2 * half_local[:, 2] ** 2 - 1, 0)
        result[start:start + len(normal)] = np.sum(sample_image(image, light) * weight[None, :, None], axis=1) / np.sum(weight)
    return result.reshape(directions.shape)


def sh_basis(directions):
    x, y, z = np.moveaxis(directions, -1, 0)
    return np.stack([np.ones_like(x), y, z, x, x * y, y * z,
                     3 * z * z - 1, x * z, x * x - y * y], axis=-1) * SH_FACTORS


def diffuse_sh(image):
    boundaries = np.arange(IMAGE_HEIGHT + 1) * math.pi / IMAGE_HEIGHT
    solid_angle = (np.cos(boundaries[:-1]) - np.cos(boundaries[1:])) * 2 * math.pi / IMAGE_WIDTH
    basis = sh_basis(image_directions())
    coefficients = np.empty((9, 4), dtype=np.float64)
    coefficients[:, 3] = 0
    for i, kernel in enumerate([math.pi] + [2 * math.pi / 3] * 3 + [math.pi / 4] * 5):
        coefficients[i, :3] = np.sum(image * (basis[..., i] * solid_angle[:, None])[..., None], axis=(0, 1)) * kernel
    return coefficients


def integrate_brdf(no_v, roughness, count=SAMPLES):
    """Split Schlick Fresnel using the same height-correlated Smith visibility as WGSL."""
    no_v, roughness = np.broadcast_arrays(no_v, roughness)
    half = importance_ggx(roughness, count)
    view_x = np.sqrt(np.maximum(0, 1 - no_v * no_v))
    vo_h = np.maximum(view_x[..., None] * half[..., 0] + no_v[..., None] * half[..., 2], 0)
    no_h = half[..., 2]
    no_l = np.maximum(2 * vo_h * no_h - no_v[..., None], 0)
    alpha_squared = roughness[..., None] ** 4
    visibility_v = no_l * np.sqrt(no_v[..., None] ** 2 * (1 - alpha_squared) + alpha_squared)
    visibility_l = no_v[..., None] * np.sqrt(no_l ** 2 * (1 - alpha_squared) + alpha_squared)
    visibility = .5 / np.maximum(visibility_v + visibility_l, 1e-15)
    weight = 4 * visibility * no_l * vo_h / np.maximum(no_h, 1e-15)
    fresnel = np.clip(1 - vo_h, 0, 1) ** 5
    return np.stack([np.mean((1 - fresnel) * weight, axis=-1), np.mean(fresnel * weight, axis=-1)], axis=-1)


def brdf_lut():
    coordinates = (np.arange(LUT_SIZE) + .5) / LUT_SIZE
    no_v, roughness = np.meshgrid(coordinates, coordinates)
    result = np.zeros((LUT_SIZE, LUT_SIZE, 4), dtype=np.float64)
    result[..., 3] = 1
    for start in range(0, LUT_SIZE, 2):
        result[start:start + 2, :, :2] = integrate_brdf(no_v[start:start + 2], roughness[start:start + 2])
    return result


def validate_math(image, coefficients, lut):
    # A uniform white environment must remain white after GGX prefiltering, and its
    # Lambertian irradiance must be pi in every direction (quadrature tolerance).
    white = np.ones_like(image)
    probes = normalize(np.array([[1., 0, 0], [0, 1., 0], [0, 0, 1.], [1., -2, 3]]))
    for roughness in [.04, .25, .6, 1]:
        np.testing.assert_allclose(prefilter(white, probes, roughness), 1, atol=2e-13)
    irradiance = np.sum(sh_basis(probes)[..., :, None] * diffuse_sh(white)[None, :, :3], axis=1)
    np.testing.assert_allclose(irradiance, math.pi, atol=4e-5)
    assert np.isfinite(coefficients).all() and np.isfinite(lut).all()
    assert (lut[..., :2] >= 0).all() and (lut[..., 0] + lut[..., 1] <= 1.015).all()
    # Independent high-sample integrations bound the stored approximation away
    # from the singular grazing corner rather than merely re-reading its bytes.
    for x, y in [(16, 6), (64, 32), (112, 96), (8, 120)]:
        reference = integrate_brdf(np.array((x + .5) / LUT_SIZE), np.array((y + .5) / LUT_SIZE), 65536)
        np.testing.assert_allclose(lut[y, x, :2], reference, atol=.008, rtol=.015)


def gzip_bytes(data):
    buffer = io.BytesIO()
    with gzip.GzipFile(fileobj=buffer, mode="wb", compresslevel=9, mtime=0, filename="") as stream:
        stream.write(data)
    return buffer.getvalue()


def js_module(compressed, digest):
    encoded = base64.b64encode(compressed).decode("ascii")
    return f'''// Generated by tools/bake-studio-environment.py. Do not edit.
// Original MIT studio image, GGX radiance mips, diffuse SH and correlated-Smith DFG.
export const STUDIO_ENVIRONMENT_SHA256 = '{digest}';
export const STUDIO_ENVIRONMENT_GZIP_BASE64 = '{encoded}';

let shared;
export async function decodeStudioEnvironment() {{
  // Each renderer borrows immutable upload bytes; it owns its GPU resources.
  return shared ??= decode();
}}
async function decode() {{
  const compressed = Uint8Array.from(atob(STUDIO_ENVIRONMENT_GZIP_BASE64), c => c.charCodeAt(0));
  const stream = new Blob([compressed]).stream().pipeThrough(new DecompressionStream('gzip'));
  const bytes = new Uint8Array(await new Response(stream).arrayBuffer());
  const header = new DataView(bytes.buffer);
  if (new TextDecoder().decode(bytes.subarray(0, 8)) !== 'NSIBL001' || bytes.length !== 1179808 ||
      header.getUint32(8, true) !== 128 || header.getUint32(12, true) !== 8 ||
      header.getUint32(16, true) !== 128 || header.getUint32(20, true) !== 9 ||
      header.getUint32(24, true) !== 0 || header.getUint32(28, true) !== 0) {{
    throw new Error('Invalid bundled WebGPU studio environment.');
  }}
  let offset = 176;
  const mips = [];
  for (let level = 0; level < 8; level++) {{
    const size = 128 >> level, length = size * size * 6 * 8;
    mips.push(Object.freeze({{size, pixels: bytes.subarray(offset, offset + length)}}));
    offset += length;
  }}
  return Object.freeze({{cubeSize:128, mipCount:8, lutSize:128,
    sphericalHarmonics:bytes.subarray(32,176), mips:Object.freeze(mips), brdfLut:bytes.subarray(offset)}});
}}
'''.encode("utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source, image = radiance_rgbe(authored_studio())
    coefficients, lut = diffuse_sh(image), brdf_lut()
    validate_math(image, coefficients, lut)
    payload = bytearray(struct.pack("<8s6I", MAGIC, CUBE_SIZE, MIP_COUNT, LUT_SIZE, 9, 0, 0))
    payload += coefficients.astype("<f4").tobytes()
    for level in range(MIP_COUNT):
        size = CUBE_SIZE >> level
        for face in range(6):
            rgb = prefilter(image, cube_directions(face, size), level / (MIP_COUNT - 1))
            rgba = np.concatenate([rgb, np.ones((*rgb.shape[:-1], 1))], axis=-1)
            assert np.isfinite(rgba).all() and np.min(rgba) >= 0 and np.max(rgba) < 65504
            payload += rgba.astype("<f2").tobytes()
        print(f"Baked {size}x{size} cube mip {level}, roughness {level / (MIP_COUNT - 1):.6f}", flush=True)
    payload += lut.astype("<f2").tobytes()
    assert len(payload) == 1179808
    compressed = gzip_bytes(payload)
    digest = hashlib.sha256(payload).hexdigest()
    manifest = {
        "format":"NSIBL001", "source":"studio.hdr", "sourceSha256":hashlib.sha256(source).hexdigest(),
        "payloadSha256":digest, "compressedSha256":hashlib.sha256(compressed).hexdigest(),
        "payloadBytes":len(payload), "compressedBytes":len(compressed),
        "cubeSize":CUBE_SIZE, "mipCount":MIP_COUNT, "lutSize":LUT_SIZE, "samples":SAMPLES,
        "sourceImageSize":[IMAGE_WIDTH,IMAGE_HEIGHT], "sourceMaximumRadiance":float(image.max()),
        "license":"MIT", "provenance":"Original authored angular softboxes; no external image or generated AI asset.",
        "brdf":"GGX with perceptual roughness squared and height-correlated Smith visibility",
        "checks":["uniform-environment prefilter", "constant irradiance SH", "finite nonnegative HDR payload",
                  "single-scatter DFG energy bound", "65536-sample independent DFG reference"],
    }
    outputs = {
        DIRECTORY / "studio.hdr":source,
        DIRECTORY / "studio.ibl.gz":compressed,
        DIRECTORY / "studio.json":(json.dumps(manifest, indent=2) + "\n").encode(),
        ROOT / "packages/graphics/environment.js":js_module(compressed, digest),
    }
    for path, data in outputs.items():
        if args.check:
            if not path.is_file() or path.read_bytes() != data:
                raise SystemExit(f"Generated environment is stale: {path.relative_to(ROOT)}")
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()
