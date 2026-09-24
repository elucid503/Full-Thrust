shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_front, depth_test_disabled, shadows_disabled, fog_disabled;

// The far field is marched rather than layered: gas cools from flame through dull red, tears into
// eddies that grow and slow as the jet spreads, and turns along the ground it strikes.

#include "res://Shaders/Exhaust/Volume.gdshaderinc"
#include "res://Shaders/Exhaust/Contact.gdshaderinc"

uniform int stream_count = 0;
uniform vec4 stream_exits[32];
uniform vec4 stream_directions[32];
uniform float jet_radius = 1.0;
uniform float jet_length = 60.0;
uniform float spread = 0.08;
uniform float merge_distance = 0.0;
uniform float handover = 8.0;
uniform vec3 crossflow = vec3(0.0);
uniform float flow_speed = 30.0;
uniform float turbulence = 1.0;
uniform float emission = 1.0;
uniform vec3 hot_colour : source_color = vec3(1.0, 0.55, 0.2);
uniform vec3 cool_colour : source_color = vec3(0.55, 0.12, 0.03);
uniform vec3 ground_point = vec3(0.0);
uniform vec3 ground_normal = vec3(0.0, 1.0, 0.0);
uniform float ground_distance = -1.0;

const int STEPS = 56;
const float SMOKE_REACH = 1.0;
const float EDDY_SCALE = 0.3;
const float PERIOD = 1.6;

struct Gas {

    float shape;
    float amount;
    float heat;
    float width;
    float path;
    vec3 coord;
    vec3 velocity;

};

float width_at(float x) {

    return jet_radius + spread * max(x, 0.0);

}

// Gaussian streams superpose, so separate nozzles merge as they spread rather than switching shape.
// Returns the union of their profiles and their mean centreline concentration.
vec2 streams(vec3 p, float x, float widen) {

    float merged = merge_distance > 0.0 ? smoothstep(merge_distance * 0.5, merge_distance, x) : 1.0;
    float width = width_at(x) * widen;
    float r = length(p.xz) / width;
    vec2 single = vec2(exp(-2.0 * r * r), jet_radius / width);

    if (merged >= 1.0) {

        return single;

    }

    float clear = 1.0;
    float concentration = 0.0;
    float power = 0.0;

    for (int j = 0; j < stream_count; j++) {

        vec4 mouth = stream_exits[j];
        vec4 axis = stream_directions[j];
        float along = dot(p - mouth.xyz, axis.xyz);
        float own = (mouth.w + spread * max(along, 0.0)) * widen;
        float off = length(p - mouth.xyz - axis.xyz * along) / own;
        float profile = exp(-2.0 * off * off) * smoothstep(-mouth.w, mouth.w, along) * clamp(axis.w, 0.0, 1.0);
        clear *= 1.0 - profile;
        concentration += axis.w * mouth.w / own;
        power += axis.w;

    }

    vec2 separate = vec2(1.0 - clear, concentration / max(power, 0.0001));
    return mix(separate, single, merged);

}

Gas gas_at(vec3 p) {

    Gas gas;
    float x = max(-p.y, 0.0);
    float along = x / max(jet_length, 0.001);
    vec3 bent = p;
    bent.xz -= crossflow.xz * along * along;

    float widen = 1.0;
    float grounded = 1.0;
    float wall = 0.0;
    float wall_path = 0.0;
    vec3 wall_coord = vec3(0.0);
    vec3 wall_velocity = vec3(0.0);

    if (ground_distance > 0.0) {

        float height = dot(p - ground_point, ground_normal);
        float impact = width_at(ground_distance);

        // The free jet splays over its last few widths instead of passing into the plate.
        float approach = 1.0 - smoothstep(0.0, impact * 2.0, height);
        widen = 1.0 + 1.4 * approach * approach;
        grounded = smoothstep(-impact * 0.1, impact * 0.25, height);

        vec3 across = p - ground_point - ground_normal * height;
        float radial = length(across);
        vec3 outward = across / max(radial, 0.001);
        float thickness = impact * 0.45 + 0.08 * radial;

        // Hot gas slows and lifts, so the wall jet peels off the ground towards its edge.
        float lift = thickness * 0.35 + radial * radial / max(jet_length, 0.001) * 0.25;
        float offset = (height - lift) / thickness;
        wall = exp(-2.0 * offset * offset) * impact / (impact + radial) * grounded;
        wall_path = ground_distance + radial;
        wall_coord = (p - ground_point) / impact;
        wall_velocity = outward * flow_speed * jet_radius / (impact + radial) / impact;

    }

    vec2 free = streams(bent, x, widen);
    free.x *= grounded;
    float width = width_at(x);
    float blend = wall / max(free.x + wall, 0.0001);
    float path = mix(x, wall_path, blend);

    gas.shape = max(free.x, wall);
    gas.width = mix(width * widen, width_at(ground_distance), blend);

    // Eddies grow with the jet: s counts widths travelled, so structures stay round as they swell.
    float travelled = spread > 0.0001 ? log(width / jet_radius) / spread : x / jet_radius;
    vec3 jet_coord = vec3(bent.x / width, travelled, bent.z / width);
    vec3 jet_velocity = vec3(0.0, flow_speed * jet_radius / (width * width), 0.0);
    gas.coord = mix(jet_coord, wall_coord, blend) * EDDY_SCALE;
    gas.velocity = mix(jet_velocity, wall_velocity, blend) * EDDY_SCALE;

    // Emission follows the path the gas has travelled, not its height below the nozzle.
    float reach = path / max(jet_length, 0.001);
    gas.heat = exp(-3.0 * reach * reach);
    gas.path = path;
    gas.amount = mix(free.y, jet_radius / width_at(wall_path), blend) * smoothstep(0.0, handover, path)
        * (1.0 - smoothstep(0.6, 1.0, reach / SMOKE_REACH));

    return gas;

}

float billow(vec3 q) {

    float coarse = eddy(q);
    float fine = eddy(q * 2.37 + vec3(coarse * 0.5) + vec3(0.19, 0.53, 0.71));
    float finest = eddy(q * 5.31 + vec3(fine * 0.4) + vec3(0.61, 0.07, 0.37));
    return coarse * 0.55 + fine * 0.3 + finest * 0.15;

}

// The stretch of the ray inside the cone that bounds the free jet, so every step lands in gas.
// Without it most steps of a long, wide box fall in clear air and the billows smear.
vec2 jet_interval(vec3 origin, vec3 ray, vec2 range) {

    float c0 = 1.8 * jet_radius + length(crossflow.xz);
    float c1 = 1.8 * spread;
    float u0 = c0 - c1 * origin.y;
    float u1 = -c1 * ray.y;
    float a = dot(ray.xz, ray.xz) - u1 * u1;
    float b = 2.0 * (dot(origin.xz, ray.xz) - u0 * u1);
    float c = dot(origin.xz, origin.xz) - u0 * u0;
    float discriminant = b * b - 4.0 * a * c;

    if (abs(a) < 0.0000001 || discriminant < 0.0) {

        return a < 0.0 || c <= 0.0 ? range : vec2(1.0, 0.0);

    }

    float root = sqrt(discriminant);
    vec2 roots = vec2(-b - root, -b + root) / (2.0 * a);
    roots = vec2(min(roots.x, roots.y), max(roots.x, roots.y));

    // Looking along the axis the cone is entered and never left; the box bounds that case.
    if (a < 0.0) {

        return c <= 0.0 || roots.x > range.x ? range : vec2(max(range.x, roots.y), range.y);

    }

    return vec2(max(range.x, roots.x), min(range.y, roots.y));

}

// Flow-map advection: two phases of the field, each carried by the local velocity and reset while
// the other is fully visible, so eddies move at their own speed without stretching into streaks.
float churn(vec3 coord, vec3 velocity) {

    float t = effect_time / PERIOD;
    float phase_a = fract(t);
    float phase_b = fract(t + 0.5);
    float blend = abs(1.0 - 2.0 * phase_a);
    vec3 shift_a = vec3(0.37, 0.61, 0.23) * floor(t);
    vec3 shift_b = vec3(0.37, 0.61, 0.23) * floor(t + 0.5) + vec3(0.5);
    float a = billow(coord - velocity * phase_a * PERIOD + shift_a);
    float b = billow(coord - velocity * phase_b * PERIOD + shift_b);

    // Blending two independent fields lowers their contrast mid-cycle; restore the variance.
    return 0.5 + (mix(a, b, blend) - 0.5) / sqrt(blend * blend + (1.0 - blend) * (1.0 - blend));

}

vec3 flame(float temperature) {

    vec3 colour = mix(cool_colour, hot_colour, smoothstep(0.2, 0.75, temperature));
    float peak = max(hot_colour.r, max(hot_colour.g, hot_colour.b));
    return mix(colour, vec3(1.0, 0.93, 0.8) * peak, smoothstep(0.85, 1.15, temperature) * 0.5);

}

void fragment() {

    float pixel_width = length(dFdx(volume_position)) + length(dFdy(volume_position));
    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);

    // The wall jet and flow around a hull both leave the cone, so they keep the whole box.
    if (ground_distance <= 0.0 && contact_count == 0) {

        interval = jet_interval(volume_camera, ray, interval);

    }

    if (interval.y <= interval.x) { discard; }

    float ds = (interval.y - interval.x) / float(STEPS);
    // Footprint-filtered contact boundaries need midpoints; elsewhere jitter hides the strata.
    float offset = contact_count > 0 ? 0.5 : 0.35 + 0.3 * grain(FRAGCOORD.xy + vec2(seed));
    vec3 radiance = vec3(0.0);

    for (int i = 0; i < STEPS; i++) {

        vec3 p = volume_camera + ray * (interval.x + (float(i) + offset) * ds);
        float contact_mask;
        float contact_wake;
        p = contact_flow(p, jet_radius, max(ds, pixel_width), contact_mask, contact_wake);
        if (contact_mask <= 0.001) { continue; }

        Gas gas = gas_at(p);
        if (gas.shape * gas.amount * contact_mask <= 0.0005) { continue; }

        float n = churn(gas.coord, gas.velocity);
        // The jet leaves the nozzle whole and tears up gradually, so the Waterfall layers have no seam to meet.
        float mixing = clamp(turbulence * smoothstep(0.0, handover * 2.5, gas.path) + contact_wake, 0.0, 1.0);

        // Eddies tear the edge into billows while the core stays whole; laminar gas keeps its profile.
        float torn = smoothstep(0.0, 0.25, gas.shape * 1.4 - (1.0 - n) * 0.8);
        float density = mix(gas.shape, torn, mixing) * gas.amount * contact_mask;

        // Fresh eddies from the core run hotter than the air they entrain at the edge.
        float temperature = clamp(gas.heat * mix(0.55, 1.0, gas.shape) * (1.0 + mixing * 0.8 * (n - 0.5)), 0.0, 1.2);
        vec3 emitted = flame(temperature) * temperature * temperature * temperature * density * emission / gas.width;

        radiance += emitted * ds;

    }

    ALBEDO = radiance;
    ALPHA = 1.0;

}
