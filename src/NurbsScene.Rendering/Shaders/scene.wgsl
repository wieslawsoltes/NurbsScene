// Linear-light, HDR CAD rendering. Geometry and both matrices are origin-relative.
// Frame uniforms occupy 256 bytes; object uniforms occupy 32 bytes and use dynamic offsets.
struct Frame {
    viewProjection: mat4x4<f32>,
    shadowMatrix: mat4x4<f32>,
    eyeExposure: vec4<f32>,
    clipPlane: vec4<f32>,
    viewport: vec4<f32>,
    light: vec4<f32>,
    options: vec4<f32>,
    reserved0: vec4<f32>,
    reserved1: vec4<f32>,
    reserved2: vec4<f32>,
};
struct ObjectUniforms { color: vec4<f32>, material: vec4<f32> };
@group(0) @binding(0) var<uniform> frame: Frame;
@group(0) @binding(1) var shadowMap: texture_depth_2d;
@group(0) @binding(2) var shadowSampler: sampler_comparison;
@group(1) @binding(0) var<uniform> object: ObjectUniforms;

const PI: f32 = 3.141592653589793;

fn safeNormalize(value: vec3<f32>, fallback: vec3<f32>) -> vec3<f32> {
    let magnitudeSquared = dot(value, value);
    return select(fallback, value * inverseSqrt(max(magnitudeSquared, 1e-20)), magnitudeSquared > 1e-20);
}

fn srgbToLinear(value: vec3<f32>) -> vec3<f32> {
    return select(value / 12.92, pow((value + 0.055) / 1.055, vec3<f32>(2.4)), value > vec3<f32>(0.04045));
}

fn isClipped(world: vec3<f32>) -> bool {
    return frame.options.x > 0.5 && dot(frame.clipPlane.xyz, world) + frame.clipPlane.w < 0.0;
}

struct SurfaceOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) world: vec3<f32>,
    @location(1) normal: vec3<f32>,
};

@vertex
fn surfaceVertex(@location(0) position: vec3<f32>, @location(1) normal: vec3<f32>) -> SurfaceOutput {
    var output: SurfaceOutput;
    output.position = frame.viewProjection * vec4<f32>(position, 1.0);
    output.world = position;
    output.normal = normal;
    return output;
}

fn fresnelSchlick(cosine: f32, f0: vec3<f32>) -> vec3<f32> {
    let grazing = pow(clamp(1.0 - cosine, 0.0, 1.0), 5.0);
    return f0 + (vec3<f32>(1.0) - f0) * grazing;
}

// Correlated Smith visibility and GGX/Trowbridge-Reitz normal distribution.
fn directLight(normal: vec3<f32>, view: vec3<f32>, light: vec3<f32>, base: vec3<f32>,
               metallic: f32, roughness: f32, radiance: vec3<f32>) -> vec3<f32> {
    let halfVector = safeNormalize(view + light, normal);
    let nDotL = max(dot(normal, light), 0.0);
    let nDotV = max(dot(normal, view), 1e-4);
    let nDotH = max(dot(normal, halfVector), 0.0);
    let vDotH = max(dot(view, halfVector), 0.0);
    let alpha = roughness * roughness;
    let alphaSquared = alpha * alpha;
    let denominator = nDotH * nDotH * (alphaSquared - 1.0) + 1.0;
    let distribution = alphaSquared / max(PI * denominator * denominator, 1e-6);
    let visibilityV = nDotL * sqrt(nDotV * nDotV * (1.0 - alphaSquared) + alphaSquared);
    let visibilityL = nDotV * sqrt(nDotL * nDotL * (1.0 - alphaSquared) + alphaSquared);
    let visibility = 0.5 / max(visibilityV + visibilityL, 1e-5);
    let fresnel = fresnelSchlick(vDotH, mix(vec3<f32>(0.04), base, metallic));
    let diffuse = (vec3<f32>(1.0) - fresnel) * (1.0 - metallic) * base / PI;
    return (diffuse + fresnel * distribution * visibility) * radiance * nDotL;
}

fn shadowVisibility(world: vec3<f32>, normal: vec3<f32>, light: vec3<f32>) -> f32 {
    if (frame.light.w < 0.5) { return 1.0; }
    let homogeneous = frame.shadowMatrix * vec4<f32>(world, 1.0);
    let projected = homogeneous.xyz / max(homogeneous.w, 1e-6);
    let uv = projected.xy * vec2<f32>(0.5, -0.5) + vec2<f32>(0.5);
    if (any(uv < vec2<f32>(0.0)) || any(uv > vec2<f32>(1.0)) || projected.z <= 0.0 || projected.z >= 1.0) {
        return 1.0;
    }
    let texel = 1.0 / vec2<f32>(textureDimensions(shadowMap));
    // Slope-aware receiver bias in normalized shadow depth; caster bias lives in the pipeline.
    let bias = max(0.00012, 0.0008 * (1.0 - max(dot(normal, light), 0.0)));
    var visibility = 0.0;
    // Bilinear comparison filtering plus a tent kernel avoids a hard square footprint.
    for (var y = -1; y <= 1; y++) {
        for (var x = -1; x <= 1; x++) {
            let weight = f32((2 - abs(x)) * (2 - abs(y)));
            visibility += weight * textureSampleCompareLevel(shadowMap, shadowSampler,
                uv + vec2<f32>(f32(x), f32(y)) * texel, projected.z - bias);
        }
    }
    return visibility / 16.0;
}

fn shadeSurface(input: SurfaceOutput, front: bool) -> vec3<f32> {
    // Derivative fallback covers degenerate NURBS normals without propagating NaN.
    let geometric = safeNormalize(cross(dpdx(input.world), dpdy(input.world)), vec3<f32>(0.0, 0.0, 1.0));
    var normal = safeNormalize(input.normal, geometric);
    if (!front) { normal = -normal; }
    let view = safeNormalize(frame.eyeExposure.xyz - input.world, normal);
    // Two-sided CAD surfaces remain visible from either side; no artificial black backfaces.
    normal = faceForward(normal, -view, normal);
    let base = srgbToLinear(clamp(object.color.rgb, vec3<f32>(0.0), vec3<f32>(1.0)));
    let metallic = clamp(object.material.x, 0.0, 1.0);
    let roughness = clamp(object.material.y, 0.075, 1.0);
    let light = safeNormalize(frame.light.xyz, vec3<f32>(0.0, 0.0, 1.0));
    let f0 = mix(vec3<f32>(0.04), base, metallic);
    let fresnel = fresnelSchlick(max(dot(normal, view), 0.0), f0);
    // Analytic hemispherical studio illumination. This is not image-based lighting.
    let skyAmount = clamp(normal.z * 0.5 + 0.5, 0.0, 1.0);
    let irradiance = mix(vec3<f32>(0.16, 0.145, 0.13), vec3<f32>(0.45, 0.52, 0.64), skyAmount);
    var color = base * (1.0 - metallic) * (vec3<f32>(1.0) - fresnel) * irradiance * frame.options.y;
    let reflection = reflect(-view, normal);
    let environment = mix(vec3<f32>(0.12, 0.105, 0.09), vec3<f32>(0.55, 0.65, 0.82), clamp(reflection.z * 0.5 + 0.5, 0.0, 1.0));
    color += environment * fresnel * mix(0.65, 0.2, roughness) * frame.options.y;
    color += directLight(normal, view, light, base, metallic, roughness,
        vec3<f32>(1.0, 0.95, 0.86) * frame.options.z) * shadowVisibility(input.world, normal, light);
    color += directLight(normal, view, normalize(vec3<f32>(0.65, 0.4, 0.5)), base, metallic, roughness,
        vec3<f32>(0.75, 0.9, 1.15));
    color += directLight(normal, view, normalize(vec3<f32>(-0.25, 0.7, 0.2)), base, metallic, roughness,
        vec3<f32>(0.35, 0.4, 0.5));
    return min(max(color, vec3<f32>(0.0)), vec3<f32>(128.0));
}

@fragment
fn surfaceFragment(input: SurfaceOutput, @builtin(front_facing) front: bool) -> @location(0) vec4<f32> {
    // Derivatives must run before the nonuniform clip-plane discard.
    let color = shadeSurface(input, front);
    if (isClipped(input.world)) { discard; }
    return vec4<f32>(color, 1.0);
}

struct GhostOutput {
    @location(0) accumulation: vec4<f32>,
    @location(1) revealage: vec4<f32>,
};

@fragment
fn ghostFragment(input: SurfaceOutput, @builtin(front_facing) front: bool) -> GhostOutput {
    let color = shadeSurface(input, front);
    if (isClipped(input.world)) { discard; }
    let alpha = clamp(object.color.a, 0.0, 0.98);
    // Bounded weighted blended OIT is independent of object submission order.
    // Reverse-Z is largest near the viewer. The weight avoids FP16 accumulation overflow.
    let weight = clamp((0.1 + pow(alpha, 3.0) * 8.0) * (0.5 + input.position.z * 1.5), 0.05, 12.0);
    var output: GhostOutput;
    output.accumulation = vec4<f32>(color * alpha, alpha) * weight;
    output.revealage = vec4<f32>(alpha);
    return output;
}

struct LineOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) world: vec3<f32>,
    @location(1) @interpolate(linear) transverse: f32,
    @location(2) @interpolate(flat) halfWidth: f32,
};

@vertex
fn lineVertex(@builtin(vertex_index) vertex: u32,
              @location(0) endpointA: vec3<f32>, @location(1) endpointB: vec3<f32>) -> LineOutput {
    let endpoint = array<f32, 6>(0.0, 1.0, 1.0, 0.0, 1.0, 0.0);
    let side = array<f32, 6>(-1.0, -1.0, 1.0, -1.0, 1.0, 1.0);
    var a = frame.viewProjection * vec4<f32>(endpointA, 1.0);
    var b = frame.viewProjection * vec4<f32>(endpointB, 1.0);
    var wa = endpointA;
    var wb = endpointB;
    // Homogeneous reverse-Z near plane: w-z >= 0. Clip BEFORE dividing by w.
    // A segment entirely behind the camera must never produce a giant screen quad.
    let da = a.w - a.z;
    let db = b.w - b.z;
    var output: LineOutput;
    output.world = endpointA;
    output.transverse = 0.0;
    output.halfWidth = max(object.material.z * 0.5, 0.25);
    if ((da < 0.0 && db < 0.0) || (a.w <= 0.0 && b.w <= 0.0)) {
        output.position = vec4<f32>(2.0, 2.0, 0.0, 1.0);
        return output;
    }
    if (da < 0.0) {
        let t = clamp(da / (da - db), 0.0, 1.0);
        a = mix(a, b, t);
        wa = mix(wa, wb, t);
    } else if (db < 0.0) {
        let t = clamp(db / (db - da), 0.0, 1.0);
        b = mix(b, a, t);
        wb = mix(wb, wa, t);
    }
    let screenDelta = (b.xy / max(b.w, 1e-8) - a.xy / max(a.w, 1e-8)) * frame.viewport.xy;
    let magnitude = length(screenDelta);
    if (magnitude < 1e-6) {
        output.position = vec4<f32>(2.0, 2.0, 0.0, 1.0);
        return output;
    }
    let perpendicular = vec2<f32>(-screenDelta.y, screenDelta.x) / magnitude;
    let halfWidth = max(object.material.z * 0.5, 0.25);
    let expandedHalfWidth = halfWidth + 0.75;
    var position = mix(a, b, endpoint[vertex]);
    // Reverse-Z precision is relative to Z. A fixed NDC bias would pull distant
    // curves through foreground surfaces; a few float ULPs retain coplanar lines.
    let depthBias = max(abs(position.z), 1e-6 * abs(position.w)) * 2e-6;
    position = vec4<f32>(position.xy + perpendicular * frame.viewport.zw * 2.0
        * expandedHalfWidth * side[vertex] * position.w,
        min(position.z + depthBias, position.w), position.w);
    output.position = position;
    output.world = mix(wa, wb, endpoint[vertex]);
    output.transverse = side[vertex] * expandedHalfWidth;
    output.halfWidth = halfWidth;
    return output;
}

@fragment
fn lineFragment(input: LineOutput) -> @location(0) vec4<f32> {
    if (isClipped(input.world)) { discard; }
    let coverage = 1.0 - smoothstep(input.halfWidth - 0.5, input.halfWidth + 0.5, abs(input.transverse));
    return vec4<f32>(srgbToLinear(object.color.rgb), object.color.a * coverage);
}

struct ShadowOutput { @builtin(position) position: vec4<f32>, @location(0) world: vec3<f32> };

@vertex
fn shadowVertex(@location(0) position: vec3<f32>) -> ShadowOutput {
    var output: ShadowOutput;
    output.position = frame.shadowMatrix * vec4<f32>(position, 1.0);
    output.world = position;
    return output;
}

@fragment
fn shadowFragment(input: ShadowOutput) {
    if (isClipped(input.world)) { discard; }
}
