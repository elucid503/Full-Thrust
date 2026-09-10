shader_type spatial;
render_mode unshaded, blend_premul_alpha, cull_front, depth_draw_never, depth_test_disabled, shadows_disabled, fog_disabled;

#include "res://Shaders/Volume.gdshaderinc"

uniform float throttle = 0.0;
uniform bool discharge_enabled = false;
uniform float discharge[32];
uniform int discharge_cursor = 0;
uniform float transport_speed = 180.0;

uniform float exit_radius = 0.6;
uniform float plume_length = 20.0;
uniform float spread = 0.3;
uniform float vacuum = 1.0;
uniform float air = 0.0;
uniform float cell_length = 0.0;
uniform float cell_strength = 0.0;
uniform vec3 crossflow = vec3(0.0);
uniform int nozzle_count = 0;
uniform vec4 nozzles[32];
uniform vec3 nozzle_axes[32];

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
uniform sampler2D ship_field : filter_linear, repeat_disable;
uniform mat4 ship_from_plume;
uniform vec4 ship_domain;
uniform vec3 ship_min;
uniform vec3 ship_max;

float delivered(float x) {

    if (!discharge_enabled) { return throttle; }
    float delay = clamp(x / transport_speed * (1.0 + x * spread / max(exit_radius, 0.01) * 0.2) * 100.0, 0.0, 30.0);
    int low = (discharge_cursor - int(floor(delay)) + 32) % 32;
    int high = (low + 31) % 32;
    return mix(discharge[low], discharge[high], fract(delay));

}


vec2 ship_interval(vec3 origin, vec3 direction, float reach) {

    vec3 safe = mix(vec3(1.0), sign(direction), greaterThan(abs(direction), vec3(0.000001)))
        * max(abs(direction), vec3(0.000001));
    vec3 a = (ship_min - origin) / safe;
    vec3 b = (ship_max - origin) / safe;
    vec3 low = min(a, b);
    vec3 high = max(a, b);
    return vec2(max(0.0, max(low.x, max(low.y, low.z))), min(reach, min(high.x, min(high.y, high.z))));

}

vec3 ship_sample(vec3 point) {

    vec2 radial = vec2(length(point.xz), point.y);
    vec2 uv = (radial - ship_domain.xy) / ship_domain.zw;

    if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) {

        return vec3(length(max(max(ship_min - point, point - ship_max), vec3(0.0))), 0.0, 0.0);

    }

    return textureLod(ship_field, uv, 0.0).rgb;

}

float ship_visibility(vec3 point) {

    vec3 origin = ship_from_plume[3].xyz;
    vec3 offset = point - origin;
    float reach = length(offset);
    vec3 direction = offset / max(reach, 0.000001);
    vec2 interval = ship_interval(origin, direction, reach);
    float at = interval.x;
    float grazing = max(exit_radius * 0.08, 0.006);

    // The jet is a spreading cone rather than a point source, so its shadow carries a penumbra that
    // widens downstream. Fewer marches are needed once the result is soft.
    float softness = spread + 0.22;
    float shade = 1.0;

    for (int step = 0; step < 14; step++) {

        if (at >= interval.y) { break; }
        float distance = ship_sample(origin + direction * at).x;
        if (distance < grazing) { return 0.0; }
        shade = min(shade, distance / max(at * softness, grazing));
        at += max(distance * 0.95, grazing);

    }

    return clamp(shade, 0.0, 1.0);

}

vec4 exhaust_at(vec3 p) {

    float h = dot(p, ground_normal) + ground_offset;

    if (h < 0.0) {

        return vec4(0.0);

    }

    float x = max(-p.y, 0.0);
    float width = exit_radius + x * spread;
    float u = x / plume_length;
    float entrainment = smoothstep(2.0, 22.0, x / exit_radius) * air;

    // Convected eddies gently perturb the mixing layer without pulling the tail into strands.
    // Coordinates are in texture periods, not metres.
    float travel = log(1.0 + x * spread / exit_radius) / max(spread, 0.02);
    vec3 flow = vec3(p.x / width, travel * 0.16 - effect_time * transport_speed / (exit_radius * 38.0), p.z / width) * 0.12;
    vec4 eddies = flow_eddies(flow);
    vec3 distortion = vec3(eddies.x - 0.5, (eddies.w - 0.5) * 0.25, eddies.y - 0.5);
    vec3 sample_point = p + distortion * width * entrainment * 0.45;
    float fuel_density = 0.0;
    float hot_density = 0.0;
    float shocks = 0.0;
    int count = max(nozzle_count, 1);

    for (int nozzle = 0; nozzle < count; nozzle++) {

        vec3 origin = nozzle_count > 0 ? nozzles[nozzle].xyz : vec3(0.0);
        vec3 axis = nozzle_count > 0 ? nozzle_axes[nozzle] : vec3(0.0, -1.0, 0.0);
        float active = nozzle_count > 0 ? nozzles[nozzle].w : 1.0;
        if (active < 0.001) { continue; }
        vec3 relative = sample_point - origin;
        float axial = dot(relative, axis);
        if (axial < 0.0 || axial > plume_length) { continue; }
        float along = axial / plume_length;
        vec3 lateral = relative - axis * axial - crossflow * (along * along);
        float jet_width = exit_radius + axial * spread;
        float radial = length(lateral) / jet_width;
        // Area dilution keeps overlapping jets additive, instead of clamping their density.
        float dilution = exit_radius / jet_width;
        float packet = delivered(axial) * active;
        float scalar = exp(-2.0 * radial * radial) * dilution * dilution * packet;
        fuel_density += scalar;
        hot_density += scalar * exp(-axial / (exit_radius * mix(26.0, 7.0, vacuum)));

        // A nozzle owns its shock cells. Their strength disappears as the potential core mixes;
        // no coherent horizontal discs may be stamped across the already merged cluster.
        float phase = axial / max(cell_length, exit_radius) * 6.2831853;
        float damping = exp(-axial / max(exit_radius * 7.0, cell_length * 1.2));
        float shock_radius = 0.48 + 0.28 * cos(phase);
        shocks += scalar * cell_strength * damping
            * (bell((radial - shock_radius) / 0.13) * 0.3
            + pow(max(-cos(phase), 0.0), 10.0) * bell(radial * 4.0) * 0.45);

    }

    vec3 light = vec3(0.0);
    float extinction = 0.0;
    if (fuel_density > 0.00001) {

        // A bounded mixture fraction from the sum of jet concentrations. Closely spaced jets
        // share a fuel-rich core; atmospheric oxygen burns along the folded outer mixing layer.
        float concentration = fuel_density * mix(1.0, 0.85 + 0.30 * eddies.z, entrainment);
        float mixture = 1.0 - exp(-concentration * 4.0);
        float reaction = 4.0 * mixture * (1.0 - mixture) * afterburn * air;
        reaction *= smoothstep(1.5, 9.0, x / exit_radius);
        float intermittency = smoothstep(0.27, 0.65, eddies.z * 0.65 + eddies.w * 0.35);
        reaction *= mix(1.0, 0.85 + 0.30 * intermittency, entrainment);

        // Hot soot radiates strongly while cool exhaust rapidly becomes invisible. The visual
        // tail ends through cooling and local mixing, not a uniformly rounded opacity cap.
        float hot_fraction = hot_density / max(fuel_density, 0.00001);
        float cooling = exp(-pow(x / (exit_radius * 46.0), 2.0));
        float heat = clamp(hot_fraction + reaction * 0.35 * cooling, 0.0, 1.0);
        float radiance = heat * heat * heat * heat;
        vec3 colour = mix(tail_colour, core_colour, smoothstep(0.15, 0.85, heat));
        float filaments = mix(1.0, 0.90 + 0.20 * intermittency, entrainment);
        light = colour * (concentration * radiance * 3.0 + reaction * radiance * 1.4) * filaments;
        light += core_colour * shocks * 1.8;
        extinction = concentration * (0.025 + afterburn * air * 0.10) * luminosity;
        // Keep the finite volume boundary beyond the luminous flow, with a final safety fade.
        float end = 1.0 - smoothstep(0.82, 1.0, u);
        light *= end * luminosity * brightness;
        extinction *= end;

    }

    if (ship_enabled) {

        vec3 point = (ship_from_plume * vec4(p, 1.0)).xyz;
        vec3 surface = ship_sample(point);

        if (surface.x < 0.0) { return vec4(0.0); }

        float visibility = ship_visibility(point);
        vec3 radial_normal = vec3(point.x, 0.0, point.z) / max(length(point.xz), 0.00001);
        vec3 normal = radial_normal * surface.y + vec3(0.0, surface.z, 0.0);
        vec3 incoming = normalize(ship_from_plume[3].xyz - point);
        float incidence = dot(normal, incoming);
        float layer = max(exit_radius * 0.38, 0.018);
        float wall = exp(-surface.x / layer) * max(incidence, 0.0);
        wall *= 1.0 - smoothstep(layer, layer * 2.0, surface.x);
        vec3 tangent = incoming - normal * incidence;
        float turbulence = eddy(point / max(exit_radius * 2.0, 0.02) + tangent * effect_time * 4.0);
        float churn = eddy(point / max(exit_radius * 6.0, 0.06) - normal * (effect_time * 1.7));

        // Gas the hull stops does not disappear: it stays attached as a sheet over the windward skin
        // and past the shoulder, then separates into a recirculating wake on the lee side.
        float blocked = 1.0 - visibility;
        vec3 stopped = light * blocked;
        float held = extinction * blocked;
        float attached = smoothstep(-0.7, 0.15, incidence);
        float sheet = exp(-surface.x / (layer * 3.5));
        float wake = exp(-surface.x / (layer * 9.0)) * (1.0 - attached);

        light *= visibility;
        extinction *= visibility;
        light += stopped * sheet * mix(0.30, 0.85, attached) * (0.30 + 1.7 * turbulence);
        light += stopped * wake * (0.12 + 0.9 * churn) * 0.55;
        extinction += held * sheet * 0.8;
        light += mix(light, core_colour * length(light), 0.35) * wall * (1.2 + 2.0 * turbulence);

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
    vec3 outward = from_impact - ground_normal * dot(from_impact, ground_normal);
    float travel = lateral / max(exit_radius * 12.0, 1.0);
    float grain_wall = eddy(vec3(outward.x, outward.y, outward.z) / (exit_radius * 2.5)
        - outward / max(lateral, 0.01) * (effect_time * 3.0 - travel));
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
