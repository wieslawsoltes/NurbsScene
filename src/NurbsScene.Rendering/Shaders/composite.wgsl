// Composite weighted OIT in linear HDR, add bloom, tone-map, then encode exactly once.
struct CompositeParameters { settings: vec4<f32>, reserved: vec4<f32> };
@group(0) @binding(0) var opaque: texture_2d<f32>;
@group(0) @binding(1) var accumulation: texture_2d<f32>;
@group(0) @binding(2) var revealage: texture_2d<f32>;
@group(0) @binding(3) var bloom: texture_2d<f32>;
@group(0) @binding(4) var linearSampler: sampler;
@group(0) @binding(5) var<uniform> parameters: CompositeParameters;

struct FullscreenOutput { @builtin(position) position: vec4<f32>, @location(0) uv: vec2<f32> };

@vertex
fn fullscreenVertex(@builtin(vertex_index) vertex: u32) -> FullscreenOutput {
    let uv = vec2<f32>(f32((vertex << 1u) & 2u), f32(vertex & 2u));
    var output: FullscreenOutput;
    output.position = vec4<f32>(uv * 2.0 - vec2<f32>(1.0), 0.0, 1.0);
    output.uv = vec2<f32>(uv.x, 1.0 - uv.y);
    return output;
}

fn acesFit(value: vec3<f32>) -> vec3<f32> {
    // Narkowicz fit of the ACES filmic response; not the full ACES color pipeline.
    let a = 2.51;
    let b = 0.03;
    let c = 2.43;
    let d = 0.59;
    let e = 0.14;
    return clamp(value * (a * value + b) / (value * (c * value + d) + e), vec3<f32>(0.0), vec3<f32>(1.0));
}

fn linearToSrgb(value: vec3<f32>) -> vec3<f32> {
    return select(value * 12.92, 1.055 * pow(value, vec3<f32>(1.0 / 2.4)) - 0.055, value > vec3<f32>(0.0031308));
}

fn srgbToLinear(value: vec3<f32>) -> vec3<f32> {
    return select(value / 12.92, pow((value + 0.055) / 1.055, vec3<f32>(2.4)), value > vec3<f32>(0.04045));
}

fn bloomCoordinate(fullPixel: vec2<f32>) -> vec2<f32> {
    let dimensions = vec2<f32>(textureDimensions(bloom));
    var minimum = vec2<f32>(0.5);
    var maximum = dimensions - vec2<f32>(0.5);
    if (parameters.settings.w > 0.5) {
        for (var axis = 0u; axis < 2u; axis++) {
            let split = parameters.reserved[axis];
            let end = split + parameters.reserved[axis + 2u];
            if (fullPixel[axis] < split) {
                maximum[axis] = min(maximum[axis], ceil(split * 0.5 - 0.5) - 0.5);
            } else {
                minimum[axis] = max(minimum[axis], ceil(end * 0.5 - 0.5) + 0.5);
            }
        }
    }
    // A sub-two-pixel view may have no half-resolution texel of its own.
    if (any(maximum < minimum)) { return vec2<f32>(-1.0); }
    return clamp(fullPixel * 0.5, minimum, maximum) / dimensions;
}

@fragment
fn compositeFragment(input: FullscreenOutput) -> @location(0) vec4<f32> {
    if (parameters.settings.w > 0.5) {
        let inGapX = input.position.x >= parameters.reserved.x
            && input.position.x < parameters.reserved.x + parameters.reserved.z;
        let inGapY = input.position.y >= parameters.reserved.y
            && input.position.y < parameters.reserved.y + parameters.reserved.w;
        if (inGapX || inGapY) {
            var separator = vec3<f32>(0.57, 0.58, 0.58);
            if (parameters.settings.z < 0.5) { separator = srgbToLinear(separator); }
            return vec4<f32>(separator, 1.0);
        }
    }
    let dimensions = vec2<i32>(textureDimensions(opaque));
    let pixel = clamp(vec2<i32>(input.position.xy), vec2<i32>(0), dimensions - vec2<i32>(1));
    let background = textureLoad(opaque, pixel, 0).rgb;
    let accumulated = textureLoad(accumulation, pixel, 0);
    let transmission = clamp(textureLoad(revealage, pixel, 0).r, 0.0, 1.0);
    // Very deep overlap can saturate FP16. Clamp before division so inf/inf never
    // escapes into the tone mapper as NaN; ordinary overlap remains unchanged.
    let transparent = min(accumulated.rgb, vec3<f32>(65504.0)) / clamp(accumulated.a, 1e-5, 65504.0);
    var hdr = mix(transparent, background, transmission);
    let bloomUv = bloomCoordinate(input.position.xy);
    if (all(bloomUv >= vec2<f32>(0.0))) {
        hdr += textureSampleLevel(bloom, linearSampler, bloomUv, 0.0).rgb * max(parameters.settings.y, 0.0);
    }
    var color = acesFit(max(hdr, vec3<f32>(0.0)) * max(parameters.settings.x, 0.0));
    if (parameters.settings.z > 0.5) { color = linearToSrgb(color); }
    // Opaque viewport output is premultiplied by definition; Uno composites it without a copy.
    return vec4<f32>(color, 1.0);
}
