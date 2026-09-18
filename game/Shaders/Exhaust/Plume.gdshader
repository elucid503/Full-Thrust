shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

// One additive layer of a plume, after Waterfall's "Additive (Dynamic)": a unit cylinder flared
// in the vertex stage, shaded by an axial falloff, a flow-aligned fresnel and scrolling noise.

uniform sampler2D noise_texture : filter_linear_mipmap, repeat_enable;

uniform vec3 start_tint : source_color = vec3(1.0);
uniform vec3 end_tint : source_color = vec3(1.0);
uniform float tint_falloff = 1.0;
uniform float falloff = 1.0;
uniform float falloff_start = 0.0;
uniform float fresnel = 1.0;
uniform float fresnel_invert = 0.0;
uniform float noise_strength = 1.0;
uniform float brightness = 1.0;
uniform float fade_in = 0.0;
uniform float fade_out = 0.0;
uniform float symmetry = 0.0;
uniform float symmetry_strength = 0.0;

uniform float expand_offset = 0.0;
uniform float expand_linear = 0.0;
uniform float expand_square = 0.0;
uniform float expand_bounded = 0.0;

uniform vec2 tile = vec2(1.0, 1.0);
uniform vec2 scroll = vec2(0.0, 1.0);
uniform float seed = 0.0;

uniform float cell_length = 0.0;
uniform float cell_strength = 0.0;
uniform float cell_pinch = 0.0;
uniform float cell_decay = 3.0;

uniform vec3 crossflow = vec3(0.0);
uniform float length_metres = 10.0;
uniform float radius_metres = 0.5;
uniform float effect_time = 0.0;

varying float along;
varying float around;
varying vec3 flow_view;

float flare(float t) {

    return 1.0 + expand_offset + expand_linear * t + expand_square * t * t + expand_bounded * (1.0 - exp(-3.0 * t));

}

float flare_rate(float t) {

    return expand_linear + 2.0 * expand_square * t + 3.0 * expand_bounded * exp(-3.0 * t);

}

float cells_at(float x) {

    if (cell_length <= 0.0) {

        return 0.0;

    }

    float phase = x / cell_length;
    float disk = 0.5 + 0.5 * cos(phase * 6.2831853);

    return exp(-phase / cell_decay) * disk * disk * disk * disk;

}

void vertex() {

    along = clamp(0.5 - VERTEX.y, 0.0, 1.0);
    around = atan(VERTEX.x, VERTEX.z) / 6.2831853 + 0.5;

    float x = along * length_metres;
    float radius = radius_metres * flare(along) * (1.0 - cell_pinch * cells_at(x));
    vec2 radial = normalize(VERTEX.xz);

    VERTEX.xz = radial * radius + crossflow.xz * along * along;
    VERTEX.y = -x;

    // Surface of revolution: the normal leans back toward the nozzle by the flare's slope.
    float slope = radius_metres * flare_rate(along) / max(length_metres, 0.001);
    NORMAL = normalize(vec3(radial.x, slope, radial.y));

    flow_view = normalize((MODELVIEW_MATRIX * vec4(0.0, -1.0, 0.0, 0.0)).xyz);

}

void fragment() {

    vec3 normal = normalize(NORMAL);

    // Fresnel against the view projected off the flow, so looking down the axis does not darken it.
    vec3 flow = normalize(cross(cross(flow_view, normal), normal));
    vec3 view = normalize(cross(cross(VIEW, flow), flow));
    float viewdot = clamp(abs(dot(normal, view)), 0.0, 1.0);
    float rim = smoothstep(0.0, 1.0, viewdot);
    float rim2 = clamp(1.0 - rim, 0.001, 10.0);

    float g = min(1.0, (1.0 + falloff_start) * (1.0 - along));
    float fade = pow(g, falloff);
    float v = pow(fade * (rim * 0.5 + 0.5), tint_falloff);
    vec3 tint = mix(end_tint, start_tint, v);

    vec2 uv = (vec2(around, along) - scroll * effect_time + vec2(seed)) * tile;
    float sample_value = texture(noise_texture, uv).r;
    float contrast = mix(0.5, sample_value, noise_strength);
    float noise = mix(contrast, 1.0, fade);

    fade *= smoothstep(0.0, max(fade_in, 0.0001), along);
    fade *= 1.0 - smoothstep(1.0 - fade_out, 1.0, along);
    fade *= 1.0 - symmetry_strength + symmetry_strength * pow(cos(symmetry * 3.1415926 * around), 2.0);
    fade *= 1.0 + cell_strength * cells_at(along * length_metres);

    float shaping = 1.0 - noise + 0.5 * noise_strength;
    float edge = pow(rim, shaping * fresnel) * pow(rim2, clamp(shaping * fresnel_invert, 0.001, 10.0));

    ALBEDO = tint * edge * fade * noise * brightness;
    ALPHA = 1.0;

}
