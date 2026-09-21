shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_front, depth_test_disabled, shadows_disabled, fog_disabled;

// Integrate emissive density through a depth-clipped volume, with no visible shell surfaces.
// Ordinary surface fog adds its blue ambient colour repeatedly across additive layers.

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


uniform float cell_length = 0.0;
uniform float cell_strength = 0.0;
uniform float cell_pinch = 0.0;
uniform float cell_decay = 3.0;

uniform vec3 crossflow = vec3(0.0);
uniform float length_metres = 10.0;
uniform float radius_metres = 0.5;


#include "res://Shaders/Exhaust/Volume.gdshaderinc"
uniform float exit_radius = 0.5;
uniform float merged_layer = 0.0;
uniform float expansion = 0.0;
uniform float vacuum_opening = 0.55;
uniform float vacuum_envelope = 0.0;
uniform bool shock_only = false;
uniform float residual_gas = 0.0;
uniform float diffuse_tail = 0.0;
uniform float effect_delta = 0.016667;
float flare(float t) {

    return 1.0 + expand_offset + expand_linear * t + expand_square * t * t + expand_bounded * (1.0 - exp(-3.0 * t));

}

float cells_at(float x) {

    if (cell_length <= 0.0) {

        return 0.0;

    }

    float phase = x / cell_length;
    float disk = 0.5 - 0.5 * cos(phase * 6.2831853);

    return exp(-phase / cell_decay) * disk * disk * disk * disk;

}

float flow_hash(vec3 p) {

    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);

}

float flow_value(vec3 p) {

    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    return mix(
        mix(mix(flow_hash(i), flow_hash(i + vec3(1, 0, 0)), f.x),
            mix(flow_hash(i + vec3(0, 1, 0)), flow_hash(i + vec3(1, 1, 0)), f.x), f.y),
        mix(mix(flow_hash(i + vec3(0, 0, 1)), flow_hash(i + vec3(1, 0, 1)), f.x),
            mix(flow_hash(i + vec3(0, 1, 1)), flow_hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);

}
void fragment() {

    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);
    if (interval.y <= interval.x) { discard; }

    // Fixed integration strata avoid contour changes as the camera changes the ray length.
    float ds = (interval.y - interval.x) / 64.0;
    vec3 total = vec3(0.0);
    float pixel_width = length(dFdx(volume_position)) + length(dFdy(volume_position));

    for (int i = 0; i < 64; i++) {

        float sample_offset = 0.35 + 0.3 * grain(FRAGCOORD.xy + vec2(float(i) * 37.0, seed));
        vec3 p = volume_camera + ray * (interval.x + (float(i) + sample_offset) * ds);
        float along = clamp(-p.y / length_metres, 0.0, 1.0);
        float downstream = -p.y / max(exit_radius, 0.001);
        float mouth = max(min(radius_metres, exit_radius), 0.001);
        mouth = mix(mouth, max(radius_metres, exit_radius * (1.12 / 0.94)), merged_layer);
        float x = max(-p.y, 0.0);
        float turn = max(exit_radius * mix(1.5, 0.25, expansion), 0.001);
        float opened = x - turn * (1.0 - exp(-x / turn));
        float authored = mouth * (flare(along) - 1.0);
        float atmospheric = mouth + clamp(authored, -mouth * 0.25, opened * 0.12)
            * (1.0 - exp(-x / turn));
        atmospheric = mix(atmospheric, max(atmospheric, mouth + opened * 0.035), diffuse_tail);
        // All layers share a smooth exit tangent instead of forming separate inflated collars.
        float cone = mouth + opened * vacuum_opening;
        float radius = mix(atmospheric, cone, expansion);
        radius *= 1.0 - cell_pinch * cells_at(-p.y) * (1.0 - expansion) * clamp(cell_strength, 0.0, 1.0);
        vec2 radial = p.xz - crossflow.xz * along * along;
        float r = length(radial) / max(radius, 0.001);
        if (r >= 1.4) { continue; }

        // Non-repeating, rotated value noise changes density, never the integration boundary.
        float mixing = smoothstep(0.5, 5.0, downstream) * clamp(noise_strength, 0.0, 1.5);
        mixing *= mix(0.65, 1.0, smoothstep(0.1, 0.85, r));
        float scale = max(exit_radius, 0.001);
        float lateral_scale = scale + opened * mix(0.035, 0.22, expansion);
        vec2 tiling = max(tile * 0.5, vec2(0.25));
        float speed = max(scroll.y, 0.1) * 7.0;
        vec3 q = vec3(radial.x / lateral_scale, (p.y + effect_time * speed * scale) * 0.32 / scale, radial.y / lateral_scale);
        q *= vec3(tiling.x, tiling.y, tiling.x);
        q += vec3(seed * 5.17, seed * 1.31, seed * 3.73);
        mat3 rotate_noise = mat3(vec3(0.0, 0.8, 0.6), vec3(-0.8, 0.36, -0.48), vec3(-0.6, -0.48, 0.64));
        q = rotate_noise * q;
        vec3 noise_rate = vec3(tiling.x / lateral_scale, tiling.y * 0.32 / scale, tiling.x / lateral_scale);
        float footprint = max(ds * length(ray * noise_rate), pixel_width * max(noise_rate.x, noise_rate.y));
        float shutter = speed * effect_delta * 0.32 * tiling.y;
        float coarse = flow_value(q * 0.45);
        // Advect broad eddies and fold finer filaments around them, rather than flickering speckles.
        vec3 warped = q + (coarse - 0.5) * vec3(1.4, -0.8, 1.1);
        float detail = flow_value(rotate_noise * warped * 2.03 + vec3(17.3, 9.2, 4.7));
        float coarse_weight = 1.0 - smoothstep(0.6, 1.8, max(footprint, shutter) * 0.45);
        float detail_weight = 1.0 - smoothstep(0.3, 1.1, max(footprint, shutter) * 2.03);
        float turbulence = clamp(0.5 + (coarse - 0.5) * 0.9 * coarse_weight
            + (detail - 0.5) * 0.35 * detail_weight, 0.0, 1.0);
        float edge_warp = 1.0 + 0.32 * mixing * (coarse - 0.5) * coarse_weight;
        r *= mix(edge_warp, 1.0 / (1.0 + 0.22 * mixing * coarse * coarse_weight), diffuse_tail);
        float edge_power = mix(max(1.4, fresnel), 1.1, expansion);
        float edge = pow(max(1.0 - r * r, 0.0), edge_power);
        // Fade density, not radius, and leave an invisible margin before the proxy's end plane.
        float cooling = max(x / length_metres + mixing * 0.12 * (turbulence - 0.5), 0.0);
        float fade = exp(-max(cooling - falloff_start * 0.25, 0.0) * max(falloff, 1.0) * 1.35);
        fade *= smoothstep(0.0, max(fade_in, 0.002), along);
        float dissolve = max(cooling - 0.25, 0.0) / max(0.8 - fade_out * 0.2, 0.4);
        fade *= exp(-5.0 * dissolve * dissolve * dissolve);
        float boundary = x / max(-bounds_min.y, 0.001);
        fade *= 1.0 - smoothstep(0.72, 0.9, boundary);
        float churn = exp2((turbulence - 0.5) * 5.0 * mixing);
        float around = atan(radial.x, radial.y);
        float symmetry_mask = mix(1.0, pow(cos(symmetry * around * 0.5), 2.0), symmetry_strength);
        float hollow = mix(1.0, smoothstep(0.0, 0.8, r), clamp(fresnel_invert, 0.0, 1.0) * 0.4);
        float density = edge * fade * churn * symmetry_mask * hollow / max(radius, 0.001);
        // Expansion dilutes emissive gas; a wider cone must not retain the core's surface brightness.
        density *= mix(1.0, pow(mouth / max(radius, mouth), mix(0.65, 0.25, max(vacuum_envelope, residual_gas))), expansion);
        density *= 1.0 + cell_strength * cells_at(-p.y);
        if (shock_only) {

            // Converging and reflected shock sheets surround a compact compression disk.
            float phase = x / max(cell_length, 0.001);
            float cell = fract(phase);
            float axial = (cell - 0.5) * cell_length;
            float radial_distance = length(radial);
            float shock_width = mouth;
            float softness = max(shock_width * 0.1, ds * 0.35);
            float half_width = max(min(cell_length * 0.065, shock_width * 0.3), ds * abs(ray.y) * 0.5);
            float disk = exp(-2.0 * pow(axial / max(half_width, 0.001), 2.0))
                * exp(-3.0 * pow(radial_distance / (shock_width * 0.42), 2.0));
            float shock_radius = shock_width * (0.18 + 0.82 * abs(cell * 2.0 - 1.0));
            float sheets = exp(-pow((radial_distance - shock_radius) / softness, 2.0));
            sheets *= pow(max(sin(cell * 3.14159265), 0.0), 0.7);
            float body = exp(-3.0 * pow(radial_distance / shock_width, 2.0));
            density = (disk + sheets * 0.42 + body * 0.06) * fade
                * exp(-phase / max(cell_decay, 0.1)) * cell_strength / shock_width;

        }
        vec3 tint = mix(end_tint, start_tint, pow(1.0 - along, tint_falloff));
        tint = mix(tint, mix(start_tint, vec3(1.0), 0.12), merged_layer * exp(-along * 9.0));
        total += tint * density * ds * brightness * 0.45;

    }

    ALBEDO = total;
    ALPHA = 1.0;

}
