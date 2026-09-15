shader_type spatial;
render_mode unshaded, blend_premul_alpha, cull_front, depth_draw_never, depth_test_disabled, shadows_disabled, fog_disabled;

#include "res://Shaders/Volume.gdshaderinc"


uniform float throttle = 0.0;
uniform bool reaction_control = false;
uniform float purge_radius = 0.6;
uniform float soot_yield = 0.0;
uniform float purge = 0.0;
uniform float burnoff = 0.0;
uniform float opposing = 0.0;
uniform float standoff = 10000.0;
uniform float blanket_radius = 1.0;
uniform float daylight = 1.0;
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

// The jet and return flow share the same displaced compression surface.
vec3 retro_boundary(vec3 p) {

    float br = max(blanket_radius * 0.65, exit_radius * 2.0);
    float r = length(p.xz);
    float x = -p.y;
    float normalizer = sqrt(1.0 + pow(2.0 * r / (br * 2.8), 2.0));
    float distance = (x - standoff + r * r / (br * 2.8)) / normalizer;
    float width = max(exit_radius * 0.45, br * 0.065) * (1.0 + r / br * 0.22);
    float fold = eddy(vec3(p.x / br, p.z / br, x / br - effect_time * 0.8) * 0.38);
    return vec3(distance + (fold - 0.5) * width * 4.0, width, fold);

}

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
    vec3 centre = crossflow * (u * u * u);
    float r = length(p.xz - centre.xz);
    float radius = exit_radius + max(x, 0.0) * spread;
    float support_radius = mix(radius, max(radius, cluster_radius * 1.5 + length(cluster_pull)), merged);
    float radial = r / support_radius;
    float entrainment = smoothstep(0.0, exit_radius * 4.0, x);
    float envelope = mix(1.0, 1.45, entrainment);
    vec3 light = vec3(0.0);
    float extinction = 0.0;

    if (throttle > 0.00001 && x >= 0.0 && x < plume_length && radial < envelope) {

        float end = (1.0 - smoothstep(0.76, 1.0, u)) * exp(-u * 0.35);
        if (opposing > 0.001) {

            vec3 front = retro_boundary(p);
            float termination = 1.0 - smoothstep(-front.y, 0.0, front.x);
            end *= mix(1.0, termination, smoothstep(0.02, 0.18, opposing));

        }
        // The soft outer field starts inside the physical aperture and opens only downstream.
        float aperture = 1.0 - smoothstep(envelope - mix(0.10, 0.35, entrainment), envelope, radial);
        float dilution = exit_radius / radius;
        dilution *= dilution;

        // Turbulence is fed by shear against ambient air, so it climbs the plume only as density rises.
        float turbulence = smoothstep(exit_radius * mix(32.0, 4.0, air), exit_radius * mix(70.0, 16.0, air), x);
        turbulence *= 1.0 - smoothstep(0.45, 0.75, vacuum);
        vec4 eddies = vec4(0.5);
        vec4 coherent = vec4(0.5);

        // The continuous core needs no expensive turbulent density evaluation.
        if (turbulence > 0.0 && !reaction_control) {

            vec3 shared_position = (cluster_from_plume * vec4(p, 1.0)).xyz;
            // Use one downstream direction throughout merging. The old domains ran in opposite
            // directions and had almost no transverse variation, producing cross-section-wide bands.
            vec3 flow = mix(p, shared_position, merged);
            float width = max(radius, cluster_radius * merged);
            flow = vec3(flow.x / width, flow.y / max(exit_radius * 8.0, width * 3.0), flow.z / width);
            flow.y += effect_time * 1.2;
            // Rotate the stretched eddies off the texture axes; retain variation across the jet.
            flow = vec3(flow.x * 0.8 - flow.y * 0.6, flow.x * 0.6 + flow.y * 0.8, flow.z);
            eddies = flow_eddies(flow * 0.35);
            coherent = eddies;

        }

        float n = eddies.x * 0.25 + eddies.z * 0.5 + eddies.w * 0.25;
        float mixing = (reaction_control ? 0.0 : turbulence) * (1.0 - 0.45 * vacuum) * (1.0 + cluster_mix * 0.18 + cluster_transient * 0.35);
        vec2 drift = (eddies.xy - vec2(0.5)) * mixing * 1.15;
        float edge = length((p.xz - centre.xz) / radius + drift);
        edge += (eddies.z - 0.5) * mixing * 0.7;
        float phase = x / max(cell_length, exit_radius) * 6.2831853;
        float cells = cell_strength * exp(-x / max(cell_length * 3.0, exit_radius));
        cells *= 1.0 - smoothstep(0.16, 0.38, u);
        float compression = cells * 0.34 * (0.5 - 0.5 * cos(phase));
        float hot_width = mix(exit_radius * 0.72 + x * spread * 0.18, radius * 0.72, vacuum) * (1.0 - compression);
        float hot_edge = length(p.xz - centre.xz + drift * hot_width) / max(hot_width, 0.001);
        float individual_core = exp(-2.0 * hot_edge * hot_edge) * dilution;
        individual_core *= mix(1.0, 0.3 + 1.7 * smoothstep(0.28, 0.7, n), turbulence * (1.0 - vacuum));
        // A radial RMS nozzle offset contributes twice its square to this Gaussian's width.
        float mixed_width = sqrt(hot_width * hot_width + 2.0 * cluster_radius * cluster_radius);
        vec2 rolling = (coherent.xy - vec2(0.5)) * mixed_width * 1.8 * merged * turbulence;
        float mixed_edge = length(p.xz - centre.xz - cluster_pull.xz + rolling) / max(mixed_width, 0.001);
        float mixed_core = exp(-2.0 * mixed_edge * mixed_edge) * dilution * hot_width * hot_width / (mixed_width * mixed_width);
        mixed_core *= mix(1.0, mix(0.3, 1.9, smoothstep(0.28, 0.7, coherent.z * 0.6 + coherent.w * 0.4)), turbulence);
        float core = mix(individual_core, mixed_core, merged);
        float shear = exp(-3.0 * edge * edge) * dilution * mixing * 0.14;
        float stream = mix(0.96 + 0.08 * eddies.w, 0.3 + 1.7 * smoothstep(0.24, 0.76, n), min(mixing, 1.0));

        float neck = 0.56 + 0.30 * cos(phase);
        float shock_edge = length(p.xz - crossflow.xz * (u * u)) / max(hot_width, 0.001);
        float contained = 1.0 - smoothstep(0.55, 0.85, shock_edge);
        float diamond = bell((shock_edge - neck * 0.6) / 0.12) * cells * contained;
        float disc = pow(max(-cos(phase), 0.0), 12.0) * bell(shock_edge * 3.5) * cells * contained;

        float temperature = exp(-x / (exit_radius * mix(20.0, 5.0, vacuum))) * (0.8 + 0.4 * eddies.z);
        vec3 colour = mix(tail_colour, core_colour, temperature);
        light = (core * (0.85 + stream * 0.3) + shear * stream) * colour * (1.5 + 1.8 * temperature);
        light += individual_core * (1.0 - merged) * (diamond * 2.8 + disc * 5.0) * core_colour;

        // Combustion is confined to the mixing layer; ambient oxygen vanishes continuously with density.
        float flame = bell((mix(hot_edge, mixed_edge, merged) - 0.95) / 0.38) * afterburn * air;
        flame *= mix(1.0, hot_width * hot_width / (mixed_width * mixed_width), merged);
        flame *= smoothstep(0.01, 0.12, u) * exp(-u * 3.0) * stream * dilution;
        light += flame * flame_colour * 0.85;
        float soot = soot_yield * dilution * exp(-2.0 * edge * edge) * (0.15 + 1.5 * stream);
        soot *= mix(1.0, 0.24, vacuum) * (1.0 - 0.55 * smoothstep(0.08, 0.6, u) * air);
        extinction = soot * 1.7 + (core * 0.025 + flame * 0.035) * luminosity;
        light += soot * mix(vec3(0.055, 0.041, 0.028) * (0.2 + daylight), colour * 2.8, exp(-u * 9.0));

        light *= aperture * end * luminosity * brightness * throttle;
        extinction *= aperture * end * throttle;

        // Retarded axial coordinates carry density structure out along the expanding streamlines.
        float free_flow = smoothstep(0.25, 0.7, vacuum) * smoothstep(exit_radius * 0.5, exit_radius * 4.0, x);
        if (free_flow > 0.001) {

            vec2 angle = (p.xz - centre.xz) / radius;
            float advected = x / max(exit_radius * 18.0 + plume_length * 0.12, 0.01) - effect_time * 8.0;
            float ribbons = eddy(vec3(angle * 0.42, advected * 0.12));
            float wisps = reaction_control ? ribbons : eddy(vec3(angle.y * 0.71 + 0.37, advected * 0.083, angle.x * 0.63 + 0.19));
            float structure = smoothstep(0.25, 0.72, ribbons * 0.65 + wisps * 0.35);
            float transport = mix(1.0, mix(0.45, 1.75, structure), free_flow);
            light *= transport;
            extinction *= transport;

        }

    }

    // The cold puff has a fixed physical aperture, independent of the collapsing chamber pressure.
    if ((purge + burnoff) > 0.00001 && x >= 0.0 && x < purge_radius * 16.0) {

        float puff_radius = purge_radius + x * 0.25;
        float puff = exp(-2.4 * r * r / (puff_radius * puff_radius)) * exp(-x / (purge_radius * 3.0));
        puff *= (1.0 - smoothstep(purge_radius * 8.0, purge_radius * 16.0, x));
        puff *= (1.0 - smoothstep(1.5, 2.2, r / puff_radius));
        float ragged = 0.5 + eddy(p / purge_radius * 0.07 + vec3(0.0, effect_time * 0.6, 0.0));
        float gas = puff * purge * ragged * 0.7;
        light += gas * vec3(0.22, 0.24, 0.27) * (0.25 + daylight);
        light += puff * burnoff * ragged * flame_colour * afterburn * air * 3.5;
        extinction += gas + puff * burnoff * soot_yield * 0.8;

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

    return vec4(light, extinction) / max(exit_radius, purge_radius * step(throttle, 0.00001));

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

vec4 retro_at(vec3 p) {

    float br = max(blanket_radius * 0.65, exit_radius * 2.0);
    float r = length(p.xz);
    float x = -p.y;
    float outward = r / br;
    vec3 front = retro_boundary(p);
    vec3 w = vec3(p.x / br, p.z / br, x / br - effect_time * 0.8);
    float tendril = eddy(w * vec3(1.1, 1.1, 0.12) + vec3(front.z * 0.15));
    float sheet = exp(-pow(front.x / front.y, 2.0));
    sheet *= 0.25 + 1.5 * smoothstep(0.25, 0.72, tendril);
    sheet *= 1.0 - smoothstep(1.2, 2.3, outward);
    sheet *= 1.0 - smoothstep(standoff + br * 0.4, standoff + br * 0.8, x);
    float dilution = pow(exit_radius / max(exit_radius + standoff * spread, exit_radius), 1.4);
    float density = sheet * dilution * opposing * throttle / max(exit_radius, 0.01);
    // The compressed hot mixing layer cools rapidly as it turns and is swept behind the vehicle.
    float hot = exp(-outward * outward * 1.5);
    density *= 0.12 + 0.88 * hot;
    vec3 colour = mix(tail_colour * 0.12, core_colour, hot);
    float jet_radius = exit_radius + standoff * spread;
    float hot_radius = mix(exit_radius * 0.72 + standoff * spread * 0.18, jet_radius * 0.72, vacuum);
    float footprint = sqrt(hot_radius * hot_radius + 2.0 * cluster_radius * cluster_radius);
    float compression = exp(-2.0 * r * r / max(footprint * footprint, 0.001));
    vec3 impact = core_colour * compression * brightness * 0.9;
    return vec4(density * (colour * 0.58 + impact) * luminosity, density * (0.012 + soot_yield * 0.08));

}

void fragment() {

    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);

    if (interval.y <= interval.x) {

        discard;

    }

    vec2 full_interval = interval;
    float core_length = mix(plume_length, min(plume_length, standoff * 1.3), opposing);
    float cone_base = exit_radius * 1.5 + cluster_radius * 2.0;
    float cone_spread = spread * 1.5 + length(crossflow.xz) / max(plume_length, 0.01);
    if (purge + burnoff > 0.00001) {

        core_length = max(core_length, purge_radius * 16.0);
        cone_base = max(cone_base, purge_radius * 2.2);
        cone_spread = max(cone_spread, 0.55);

    }
    interval = cone_interval(volume_camera, ray, interval, core_length, cone_base, cone_spread);
    // Concentrate samples around the small, bright core instead of spending them on the empty envelope.
    float transverse = dot(ray.xz, ray.xz);
    float closest = transverse > 0.00001 ? -dot(volume_camera.xz, ray.xz) / transverse : -dot(volume_camera, ray);
    closest = clamp(closest, interval.x, interval.y);
    float jitter = fract(grain(FRAGCOORD.xy) + sample_phase);
    float transmission = 1.0;
    vec3 total = vec3(0.0);
    vec3 outside = max(max(bounds_min - volume_camera, volume_camera - bounds_max), vec3(0.0));
    float proxy_width = max(bounds_max.x - bounds_min.x, bounds_max.z - bounds_min.z);
    // Screen-filling overlapping volumes need fewer samples than a distant, subpixel core.
    float detail = smoothstep(0.0, max(proxy_width, exit_radius), length(outside));
    int steps = reaction_control ? 12 : int(mix(min(float(sample_count), 20.0), float(sample_count), detail));

    for (int i = 0; i < 64; i++) {

        if (i >= steps || interval.y <= interval.x) { break; }

        float a = float(i) / float(steps);
        float b = float(i + 1) / float(steps);
        float ta = a < 0.5 ? closest - (closest - interval.x) * pow(1.0 - 2.0 * a, 2.0) : closest + (interval.y - closest) * pow(2.0 * a - 1.0, 2.0);
        float tb = b < 0.5 ? closest - (closest - interval.x) * pow(1.0 - 2.0 * b, 2.0) : closest + (interval.y - closest) * pow(2.0 * b - 1.0, 2.0);
        float ds = tb - ta;
        vec4 gas = exhaust_at(volume_camera + ray * mix(ta, tb, jitter));
        float absorb = exp(-gas.a * ds);
        total += transmission * gas.rgb * ds * (0.5 + 0.5 * absorb);
        transmission *= absorb;
        if (transmission < 0.015) { break; }

    }

    if (opposing * throttle > 0.001) {

        float br = max(blanket_radius * 0.65, exit_radius * 2.0);
        float scale = br * 2.8;
        float a = dot(ray.xz, ray.xz) / scale;
        float b = -ray.y + 2.0 * dot(volume_camera.xz, ray.xz) / scale;
        float c = -volume_camera.y - standoff + dot(volume_camera.xz, volume_camera.xz) / scale;
        float discriminant = b * b - 4.0 * a * c;
        if (discriminant >= -a * br * 1.5) {

            // Fade the finite tangent-ray support instead of revealing a circular clipping edge.
            float grazing = a > 0.000001 ? smoothstep(-a * br * 1.5, 0.0, discriminant) : 1.0;
            vec2 roots = abs(a) < 0.000001 ? vec2(-c / (abs(b) > 0.000001 ? b : 0.000001))
                : vec2(-b - sqrt(max(discriminant, 0.0)), -b + sqrt(max(discriminant, 0.0))) / (2.0 * a);
            for (int root = 0; root < 2; root++) {

                if (root == 1 && abs(roots.y - roots.x) < 0.001) { break; }
                float centre = roots[root];
                vec3 p = volume_camera + ray * centre;
                vec3 gradient = vec3(2.0 * p.x / scale, -1.0, 2.0 * p.z / scale);
                float width = max(exit_radius * 0.45, br * 0.065) * (1.0 + length(p.xz) / br * 0.22);
                float reach = min(width * 5.0 / max(abs(dot(normalize(gradient), ray)), 0.12), br * 1.8);
                float start = max(centre - reach, full_interval.x);
                float end = min(centre + reach, full_interval.y);
                if (root == 0 && roots.y - roots.x > 0.001) { end = min(end, (roots.x + roots.y) * 0.5); }
                if (root == 1) { start = max(start, (roots.x + roots.y) * 0.5); }
                float stride = max(end - start, 0.0) / 10.0;
                for (int j = 0; j < 10; j++) {

                    vec4 gas = retro_at(volume_camera + ray * (start + (float(j) + jitter) * stride)) * grazing;
                    float absorb = exp(-gas.a * stride);
                    total += transmission * gas.rgb * stride * (0.5 + 0.5 * absorb);
                    transmission *= absorb;

                }

            }

        }

    }
    interval = full_interval;

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
