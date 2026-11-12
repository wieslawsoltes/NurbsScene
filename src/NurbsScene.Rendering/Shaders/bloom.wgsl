// Half-resolution threshold extraction and separable Gaussian bloom.
// Storage targets are distinct from their source texture in each dispatch.
struct BloomParameters { settings: vec4<f32>, reserved: vec4<f32> };
@group(0) @binding(0) var source: texture_2d<f32>;
@group(0) @binding(1) var destination: texture_storage_2d<rgba16float, write>;
@group(0) @binding(2) var<uniform> parameters: BloomParameters;
@group(0) @binding(3) var accumulation: texture_2d<f32>;
@group(0) @binding(4) var revealage: texture_2d<f32>;

// reserved.xy is the first separator pixel; reserved.zw is separator thickness.
// Keep each tap in its CAD view, including when a two-pixel footprint meets a gap.
fn sampleBounds(fullCenter: vec2<f32>, sourceScale: f32) -> vec4<i32> {
    var minimum = vec2<i32>(0);
    var maximum = vec2<i32>(textureDimensions(source)) - vec2<i32>(1);
    if (parameters.settings.z > 0.5) {
        for (var axis = 0u; axis < 2u; axis++) {
            let split = parameters.reserved[axis];
            let end = split + parameters.reserved[axis + 2u];
            if (fullCenter[axis] < split) {
                maximum[axis] = min(maximum[axis], i32(ceil(split / sourceScale - 0.5)) - 1);
            } else if (fullCenter[axis] >= end) {
                minimum[axis] = max(minimum[axis], i32(ceil(end / sourceScale - 0.5)));
            } else {
                return vec4<i32>(0, 0, -1, -1);
            }
        }
    }
    return vec4<i32>(minimum, maximum);
}

fn loadClamped(position: vec2<i32>, bounds: vec4<i32>) -> vec3<f32> {
    return textureLoad(source, clamp(position, bounds.xy, bounds.zw), 0).rgb;
}

// Resolve the same linear HDR scene as composite before threshold extraction.
// Keeping this in the downsample avoids a full-resolution intermediate/pass.
// The neutral opaque-only OIT textures are 1x1, so each load uses its own extent.
fn loadComposed(position: vec2<i32>, bounds: vec4<i32>) -> vec3<f32> {
    let pixel = clamp(position, bounds.xy, bounds.zw);
    let accumPixel = clamp(pixel, vec2<i32>(0), vec2<i32>(textureDimensions(accumulation)) - vec2<i32>(1));
    let revealPixel = clamp(pixel, vec2<i32>(0), vec2<i32>(textureDimensions(revealage)) - vec2<i32>(1));
    let background = textureLoad(source, pixel, 0).rgb;
    let accumulated = textureLoad(accumulation, accumPixel, 0);
    let transmission = clamp(textureLoad(revealage, revealPixel, 0).r, 0.0, 1.0);
    let transparent = min(accumulated.rgb, vec3<f32>(65504.0)) / clamp(accumulated.a, 1e-5, 65504.0);
    return mix(transparent, background, transmission);
}

@compute @workgroup_size(8, 8)
fn downsample(@builtin(global_invocation_id) id: vec3<u32>) {
    let dimensions = textureDimensions(destination);
    if (any(id.xy >= dimensions)) { return; }
    let position = vec2<i32>(id.xy * 2u);
    let bounds = sampleBounds(vec2<f32>(position) + vec2<f32>(1.0), 1.0);
    if (any(bounds.zw < bounds.xy)) {
        textureStore(destination, vec2<i32>(id.xy), vec4<f32>(0.0));
        return;
    }
    let color = (loadComposed(position, bounds) + loadComposed(position + vec2<i32>(1, 0), bounds)
        + loadComposed(position + vec2<i32>(0, 1), bounds) + loadComposed(position + vec2<i32>(1, 1), bounds)) * 0.25;
    let threshold = max(parameters.settings.x, 0.0);
    let knee = max(parameters.settings.y, 1e-4);
    let brightness = max(color.r, max(color.g, color.b));
    let soft = clamp(brightness - threshold + knee, 0.0, 2.0 * knee);
    let contribution = max(brightness - threshold, soft * soft / (4.0 * knee));
    let extracted = min(color * contribution / max(brightness, 1e-4), vec3<f32>(64.0));
    textureStore(destination, vec2<i32>(id.xy), vec4<f32>(extracted, 1.0));
}

fn blur(position: vec2<i32>, axis: vec2<i32>) -> vec3<f32> {
    let bounds = sampleBounds((vec2<f32>(position) + vec2<f32>(0.5)) * 2.0, 2.0);
    if (any(bounds.zw < bounds.xy)) { return vec3<f32>(0.0); }
    // Symmetric nine-tap Gaussian; coefficients sum to one.
    let weights = array<f32, 5>(0.2270270270, 0.1945945946, 0.1216216216, 0.0540540541, 0.0162162162);
    var color = loadClamped(position, bounds) * weights[0];
    for (var i = 1; i < 5; i++) {
        color += (loadClamped(position + axis * i, bounds) + loadClamped(position - axis * i, bounds)) * weights[i];
    }
    return color;
}

@compute @workgroup_size(8, 8)
fn blurHorizontal(@builtin(global_invocation_id) id: vec3<u32>) {
    if (any(id.xy >= textureDimensions(destination))) { return; }
    textureStore(destination, vec2<i32>(id.xy), vec4<f32>(blur(vec2<i32>(id.xy), vec2<i32>(1, 0)), 1.0));
}

@compute @workgroup_size(8, 8)
fn blurVertical(@builtin(global_invocation_id) id: vec3<u32>) {
    if (any(id.xy >= textureDimensions(destination))) { return; }
    textureStore(destination, vec2<i32>(id.xy), vec4<f32>(blur(vec2<i32>(id.xy), vec2<i32>(0, 1)), 1.0));
}
