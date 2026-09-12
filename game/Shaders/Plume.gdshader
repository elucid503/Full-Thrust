shader_type spatial;
render_mode unshaded, blend_premul_alpha, cull_front, depth_draw_never, depth_test_disabled, shadows_disabled, fog_disabled;

#include "res://Shaders/Volume.gdshaderinc"
#include "res://Shaders/IrregularNoise.gdshaderinc"

uniform float throttle = 0.0;
uniform float exit_radius = 0.6;
uniform float plume_length = 20.0;
uniform float spread = 0.3;
uniform float vacuum = 1.0;
uniform float air = 0.0;
uniform float cell_length = 0.0;
uniform float cell_strength = 0.0;
uniform vec3 crossflow = vec3(0.0);

uniform vec3 core_colour : source_color = vec3(1.0, 0.83, 0.62);
uniform vec3 tail_colour : source_color = vec3(1.0, 0.38, 0.10);
uniform vec3 flame_colour : source_color = vec3(1.0, 0.48, 0.10);
uniform float afterburn = 1.0;
uniform float luminosity = 1.0;
uniform float brightness = 1.0;
uniform int sample_count = 40;

uniform vec3 ground_normal = vec3(0.0, 1.0, 0.0);
uniform float ground_offset = 1000000.0;
uniform vec3 ground_impact = vec3(0.0);
uniform float ground_radius = 0.0;
uniform float ground_strength = 0.0;

uniform bool ship_enabled = false;
uniform sampler2D obstacle_mask : filter_nearest, repeat_disable;
uniform float obstacle_apex = 1.0;
uniform float obstacle_slope = 0.2;
uniform vec3 cluster_pull = vec3(0.0);
uniform mat4 cluster_from_plume;
uniform float cluster_radius = 0.0;
uniform float cluster_mix = 0.0;
uniform float cluster_transient = 0.0;
uniform float ground_water = 0.0;

vec4 exhaust_at(vec3 p) {

    float h = dot(p, ground_normal) + ground_offset;

    if (h < 0.0) {

        return vec4(0.0);

    }

    float x = -p.y;
    float u = clamp(x / plume_length, 0.0, 1.0);
    float merge_start = max(exit_radius * 2.0, cluster_radius);
    float merge_end = merge_start + max(cluster_radius * 22.0, exit_radius * 26.0);
    float merged = smoothstep(merge_start, merge_end, x) * smoothstep(0.0, 0.5, cluster_mix);
    vec3 centre = crossflow * (u * u);
    float r = length(p.xz - centre.xz);
    float radius = exit_radius + max(x, 0.0) * spread;
    float support_radius = mix(radius, max(radius, cluster_radius * 1.5 + length(cluster_pull)), merged);
    float radial = r / support_radius;
    float entrainment = smoothstep(0.0, exit_radius * 4.0, x);
    float envelope = mix(1.0, 1.45, entrainment);
    vec3 light = vec3(0.0);
    float extinction = 0.0;

    if (x >= 0.0 && x < plume_length && radial < envelope) {

        float end = (1.0 - smoothstep(0.76, 1.0, u)) * exp(-u * 0.35);
        // The soft outer field starts inside the physical aperture and opens only downstream.
        float aperture = 1.0 - smoothstep(envelope - mix(0.10, 0.35, entrainment), envelope, radial);
        float dilution = exit_radius / radius;
        dilution *= dilution;

        // Turbulence is fed by shear against ambient air, so it climbs the plume only as density rises.
        float onset = mix(0.46, 0.15, air);
        float turbulence = smoothstep(onset, onset + 0.34, u);
        vec4 eddies = vec4(0.5);
        vec4 coherent = vec4(0.5);

        // The continuous core needs no expensive turbulent density evaluation.
        if (turbulence > 0.0) {

            vec3 advect = vec3(p.x / radius, x / (exit_radius * 6.0) - effect_time * 20.0, p.z / radius);
            vec3 individual_flow = advect * vec3(0.65, 0.19, 0.65) + vec3(seed);
            float individual_roll = irregular_noise(individual_flow * 0.47);
            eddies = vec4(individual_roll, irregular_noise(individual_flow.zxy * 0.61),
                irregular_noise(individual_flow * 1.13 + vec3(individual_roll)), irregular_noise(individual_flow * 1.91 - vec3(effect_time * 0.17)));
            vec3 shared_position = (cluster_from_plume * vec4(p, 1.0)).xyz;
            vec3 shared_flow = shared_position / max(exit_radius * 5.0, 0.01) + vec3(0.0, effect_time * 15.0, 0.0);
            float roll_a = irregular_noise(shared_flow * 0.43);
            float roll_b = irregular_noise(shared_flow.zxy * 0.37 + vec3(8.31, 3.67, 5.19));
            vec3 warp = vec3(roll_a - 0.5, roll_b - 0.5, roll_a + roll_b - 1.0);
            coherent = vec4(roll_a, roll_b, irregular_noise(shared_flow * 0.83 + warp * 0.8 + vec3(0.0, effect_time * 0.71, 0.0)),
                irregular_noise(shared_flow * 1.67 + warp + vec3(effect_time * 0.13, 0.0, effect_time * 0.19)));
            eddies = mix(eddies, coherent, merged * 0.9);

        }

        float n = eddies.x * 0.25 + eddies.z * 0.5 + eddies.w * 0.25;
        float mixing = turbulence * (1.0 - 0.45 * vacuum) * (1.0 + cluster_mix * 0.18 + cluster_transient * 0.35);
        vec2 drift = (eddies.xy - vec2(0.5)) * mixing * 0.32;
        float edge = length((p.xz - centre.xz) / radius + drift);
        edge += (eddies.z - 0.5) * mixing * 0.16;
        float hot_width = mix(exit_radius * 0.72 + x * spread * 0.18, radius * 0.72, vacuum);
        float hot_edge = length(p.xz - centre.xz + drift * hot_width) / max(hot_width, 0.001);
        float individual_core = exp(-2.0 * hot_edge * hot_edge) * dilution;
        // A radial RMS nozzle offset contributes twice its square to this Gaussian's width.
        float mixed_width = sqrt(hot_width * hot_width + 2.0 * cluster_radius * cluster_radius);
        vec2 rolling = (coherent.xy - vec2(0.5)) * mixed_width * 1.8 * merged * turbulence;
        float mixed_edge = length(p.xz - centre.xz - cluster_pull.xz + rolling) / max(mixed_width, 0.001);
        float mixed_core = exp(-2.0 * mixed_edge * mixed_edge) * dilution * hot_width * hot_width / (mixed_width * mixed_width);
        mixed_core *= mix(1.0, mix(0.2, 2.0, smoothstep(0.35, 0.65, coherent.z * 0.6 + coherent.w * 0.4)), turbulence);
        float core = mix(individual_core, mixed_core, merged);
        float shear = exp(-3.0 * edge * edge) * dilution * mixing * 0.14;
        float stream = mix(0.96 + 0.08 * eddies.w, 0.12 + 2.05 * smoothstep(0.24, 0.76, n), min(mixing, 1.0));

        float phase = x / max(cell_length, exit_radius) * 6.2831853;
        float cells = cell_strength * exp(-x / max(cell_length * 2.0, exit_radius));
        cells *= 1.0 - smoothstep(0.12, 0.3, u);
        float neck = 0.56 + 0.30 * cos(phase);
        float shock_edge = length(p.xz - crossflow.xz * (u * u)) / max(hot_width, 0.001);
        float contained = 1.0 - smoothstep(0.55, 0.85, shock_edge);
        float diamond = bell((shock_edge - neck * 0.6) / 0.12) * cells * contained;
        float disc = pow(max(-cos(phase), 0.0), 12.0) * bell(shock_edge * 3.5) * cells * contained;

        float temperature = exp(-x / (exit_radius * mix(10.0, 5.0, vacuum))) * (0.8 + 0.4 * eddies.z);
        vec3 colour = mix(tail_colour, core_colour, temperature);
        light = (core * (0.85 + stream * 0.3) + shear * stream) * colour * (1.5 + 1.8 * temperature);
        light += individual_core * (1.0 - merged) * (diamond * 0.35 + disc * 0.9) * core_colour;

        // Combustion is confined to the mixing layer; ambient oxygen vanishes continuously with density.
        float flame = bell((mix(hot_edge, mixed_edge, merged) - 0.95) / 0.38) * afterburn * air;
        flame *= mix(1.0, hot_width * hot_width / (mixed_width * mixed_width), merged);
        flame *= smoothstep(0.01, 0.12, u) * exp(-u * 3.0) * stream * dilution;
        light += flame * flame_colour * 0.85;
        extinction = (core * 0.035 + flame * 0.07) * luminosity;

        light *= aperture * end * luminosity * brightness * throttle;
        extinction *= aperture * end * throttle;

    }

    if (ship_enabled && x >= 0.0) {

        vec2 uv = p.xz / max((x + obstacle_apex) * obstacle_slope, 0.001) * 0.5 + 0.5;
        if (all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0)))) {

            vec4 hit = textureLod(obstacle_mask, uv, 0.0);
            float gap = hit.r - x;
            if (gap < 0.0) { return vec4(0.0); }
            if (hit.r < plume_length) {

                vec3 normal = normalize(hit.gba);
                float layer = max(exit_radius * 0.9, 0.04);
                vec3 tangent = vec3(0.0, -1.0, 0.0) + normal * normal.y;
                float churn = eddy(p / max(exit_radius * 2.0, 0.02) - tangent * effect_time * 5.0);
                float sheet = exp(-gap / layer) * (0.4 + 1.8 * churn);
                light *= 1.0 + sheet * 2.5;
                extinction *= 1.0 + sheet;

            }

        }

    }

    return vec4(light, extinction) / exit_radius;

}

vec4 wall_at(vec3 p) {

    float h = max(dot(p, ground_normal) + ground_offset, 0.0);

    vec3 from_impact = p - ground_impact;
    float lateral = length(from_impact - ground_normal * dot(from_impact, ground_normal));
    float wall_height = exit_radius * 0.18 + lateral * 0.045;
    float wall = exp(-h / wall_height) * exp(-lateral / max(ground_radius * 0.3, 0.001));
    wall *= 1.0 - smoothstep(ground_radius * 0.65, ground_radius, lateral);
    float grain_wall = eddy(p / (exit_radius * 5.0) - ground_normal * effect_time * 1.5);
    wall *= (0.35 + 1.4 * grain_wall) * ground_strength;

    return vec4(wall * mix(tail_colour, flame_colour, air) * luminosity * throttle * 0.55,
        wall * air * 0.04) / exit_radius;

}

void fragment() {

    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);

    if (interval.y <= interval.x) {

        discard;

    }

    // Concentrate samples around the small, bright core instead of spending them on the empty envelope.
    float transverse = dot(ray.xz, ray.xz);
    float closest = transverse > 0.00001 ? -dot(volume_camera.xz, ray.xz) / transverse : -dot(volume_camera, ray);
    closest = clamp(closest, interval.x, interval.y);
    float jitter = mix(0.35, 0.65, grain(FRAGCOORD.xy));
    float transmission = 1.0;
    vec3 total = vec3(0.0);
    vec3 outside = max(max(bounds_min - volume_camera, volume_camera - bounds_max), vec3(0.0));
    float proxy_width = max(bounds_max.x - bounds_min.x, bounds_max.z - bounds_min.z);
    // Screen-filling overlapping volumes need fewer samples than a distant, subpixel core.
    float detail = smoothstep(0.0, max(proxy_width, exit_radius), length(outside));
    int steps = int(mix(min(float(sample_count), 12.0), float(sample_count), detail));

    for (int i = 0; i < 64; i++) {

        if (i >= steps) { break; }

        float a = float(i) / float(steps);
        float b = float(i + 1) / float(steps);
        float ta = a < 0.5 ? closest - (closest - interval.x) * pow(1.0 - 2.0 * a, 2.0) : closest + (interval.y - closest) * pow(2.0 * a - 1.0, 2.0);
        float tb = b < 0.5 ? closest - (closest - interval.x) * pow(1.0 - 2.0 * b, 2.0) : closest + (interval.y - closest) * pow(2.0 * b - 1.0, 2.0);
        float ds = tb - ta;
        vec4 gas = exhaust_at(volume_camera + ray * mix(ta, tb, jitter));
        float absorb = exp(-gas.a * ds);
        total += transmission * gas.rgb * ds * (0.5 + 0.5 * absorb);
        transmission *= absorb;

    }

    // The wall jet is much thinner than the exhaust: integrate its own slab to avoid skipped bands.
    if (ground_strength > 0.001) {

        float slope = dot(ray, ground_normal);
        float height = dot(volume_camera, ground_normal) + ground_offset;
        vec2 wall_interval = interval;

        if (abs(slope) > 0.00001) {

            float a = -height / slope;
            float b = (ground_radius * 0.35 - height) / slope;
            wall_interval = vec2(max(interval.x, min(a, b)), min(interval.y, max(a, b)));

        }
        else if (height < 0.0 || height > ground_radius * 0.35) {

            wall_interval.y = wall_interval.x;

        }

        for (int i = 0; i < 20; i++) {

            float a = float(i) / 20.0;
            float b = float(i + 1) / 20.0;
            a = slope > 0.0 ? a * a : 1.0 - (1.0 - a) * (1.0 - a);
            b = slope > 0.0 ? b * b : 1.0 - (1.0 - b) * (1.0 - b);
            float ta = mix(wall_interval.x, wall_interval.y, a);
            float tb = mix(wall_interval.x, wall_interval.y, b);
            float ds = max(tb - ta, 0.0);
            vec4 wall = wall_at(volume_camera + ray * mix(ta, tb, jitter));
            total += transmission * wall.rgb * ds;
            transmission *= exp(-wall.a * ds);

        }

    }

    ALBEDO = total;
    ALPHA = 1.0 - transmission;

}
