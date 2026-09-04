shader_type spatial;
render_mode unshaded, blend_premul_alpha, cull_front, depth_draw_never, depth_test_disabled, shadows_disabled, fog_disabled;

#include "res://Shaders/Volume.gdshaderinc"

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

vec4 exhaust_at(vec3 p) {

    float h = dot(p, ground_normal) + ground_offset;

    if (h < 0.0) {

        return vec4(0.0);

    }

    float x = -p.y;
    float u = clamp(x / plume_length, 0.0, 1.0);
    vec3 centre = crossflow * (u * u);
    float r = length(p.xz - centre.xz);
    float radius = exit_radius + max(x, 0.0) * spread;
    float radial = r / radius;
    float entrainment = smoothstep(0.0, exit_radius * 4.0, x);
    float envelope = mix(1.0, 2.0, entrainment);
    vec3 light = vec3(0.0);
    float extinction = 0.0;

    if (x >= 0.0 && x < plume_length && radial < envelope) {

        float end = 1.0 - smoothstep(0.65, 1.0, u);
        // The soft outer field starts inside the physical aperture and opens only downstream.
        float aperture = 1.0 - smoothstep(envelope - mix(0.10, 0.35, entrainment), envelope, radial);
        float dilution = exit_radius / radius;
        dilution *= dilution;

        vec3 advect = vec3(p.x / radius, x / (exit_radius * 6.0) - effect_time * 5.0, p.z / radius);
        vec4 eddies = flow_eddies(advect * vec3(0.65, 0.19, 0.65));
        float n = eddies.x * 0.25 + eddies.z * 0.5 + eddies.w * 0.25;
        float mixing = smoothstep(0.015, 0.38, u) * (1.0 - 0.45 * vacuum);
        vec2 drift = (eddies.xy - vec2(0.5)) * mixing * 0.85;
        float edge = length((p.xz - centre.xz) / radius + drift);
        edge += (eddies.z - 0.5) * mixing * 0.45;
        float core = exp(-2.5 * edge * edge) * dilution;
        float stream = mix(0.78 + 0.40 * eddies.w, 0.12 + 2.05 * smoothstep(0.24, 0.76, n), mixing);

        float phase = x / max(cell_length, exit_radius) * 6.2831853;
        float cells = cell_strength * exp(-x / max(cell_length * 3.0, exit_radius));
        float neck = 0.56 + 0.30 * cos(phase);
        float diamond = bell((radial - neck) / 0.17) * cells;
        float disc = pow(max(-cos(phase), 0.0), 12.0) * bell(radial * 3.5) * cells;

        float temperature = exp(-x / (exit_radius * mix(10.0, 5.0, vacuum))) * (0.8 + 0.4 * eddies.z);
        vec3 colour = mix(tail_colour, core_colour, temperature);
        light = core * stream * colour * (1.5 + 1.8 * temperature);
        light += (diamond * 0.28 + disc * 1.1) * dilution * core_colour;

        // Combustion is confined to the mixing layer; ambient oxygen vanishes continuously with density.
        float flame = bell((edge - 0.82) / 0.3) * afterburn * air;
        flame *= smoothstep(0.01, 0.12, u) * exp(-u * 3.0) * stream * dilution;
        light += flame * flame_colour * 2.4;
        extinction = (core * 0.035 + flame * 0.07) * luminosity;

        light *= aperture * end * luminosity * brightness * throttle;
        extinction *= aperture * end * throttle;

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

    return vec4(wall * mix(tail_colour, flame_colour, air) * luminosity * throttle * 0.8, wall * air * 0.08) / exit_radius;

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

    for (int i = 0; i < 64; i++) {

        if (i >= sample_count) { break; }

        float a = float(i) / float(sample_count);
        float b = float(i + 1) / float(sample_count);
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
