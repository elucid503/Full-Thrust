shader_type spatial;
render_mode unshaded, blend_premul_alpha, cull_front, depth_draw_never, depth_test_disabled, shadows_disabled, fog_disabled;

#include "res://Shaders/EntryField.gdshaderinc"

uniform vec2 bow_extent = vec2(1.0);
uniform float bow_front = 0.0;
uniform float bow_offset = 0.2;
uniform float bow_bend = 0.22;

vec3 bow_at(vec3 p) {

    vec2 q = p.xz / bow_extent;
    float square = dot(q, q);
    float radial = sqrt(square);
    float tip = bow_front + bow_offset;
    vec2 slope = 2.0 * bow_bend * q / bow_extent;
    float distance = (p.y - tip + bow_bend * square) / sqrt(1.0 + dot(slope, slope));
    float width = body_radius * (0.08 + 0.04 * min(radial, 1.65));
    float edge = (1.0 - smoothstep(1.05, 1.65, radial)) / (1.0 + square * 0.4);

    return vec3(distance, width, edge);

}

void fragment() {

    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);

    if (interval.y <= interval.x) { discard; }

    float t = interval.x;
    float transmission = 1.0;
    vec3 total = vec3(0.0);
    float jitter = mix(0.35, 0.65, grain(FRAGCOORD.xy));

    for (int i = 0; i < 96; i++) {

        if (t >= interval.y) { break; }

        vec3 p = volume_camera + ray * t;
        vec4 hull = hull_at(p);
        vec3 front = bow_at(p);
        float facing = clamp(hull.y, -1.0, 1.0);
        float windward = max(facing, 0.0);
        float thickness = standoff * (0.70 + 0.30 * (1.0 - windward));
        float clearance = min((abs(hull.a) - standoff * 2.8) * 0.7, (abs(front.x) - front.y * 3.0) * 0.65);
        float ds = max(min(standoff * 0.45, body_radius * 0.04), clearance);
        ds = min(ds, interval.y - t);

        // The distance gradient advances the sample without another hull texture fetch.
        float distance = hull.a + dot(hull.xyz, ray) * ds * jitter;
        float wrap = smoothstep(-0.22, 0.3, facing);
        float shock = bell((distance - standoff * (0.55 + 0.45 * windward)) / thickness);
        shock *= smoothstep(-0.015 * body_radius, 0.025 * body_radius, distance);
        shock *= wrap * (0.16 + 0.84 * windward * windward);
        shock += 0.25 * windward * windward * exp(-max(distance, 0.0) / standoff)
            * smoothstep(-0.01 * body_radius, 0.02 * body_radius, distance);

        // The detached bow arches ahead of the nose and outward past the projected shoulders.
        front = bow_at(p + ray * ds * jitter);
        float bow_distance = abs(front.x) / front.y;
        float bow = bell(bow_distance) * (1.0 - smoothstep(2.0, 3.0, bow_distance)) * front.z;
        bow *= smoothstep(0.0, body_radius * 0.05, distance);

        // Gas must reach zero along the hull distance before it meets any face of the proxy box.
        float support = 1.0 - smoothstep(standoff * 2.8, standoff * 3.8, distance);
        shock *= support;

        // Keep the luminous compressed gas seated on the windward skin, beneath the detached bow.
        float surface_width = max(body_radius * 0.06, standoff * 0.45);
        float surface_glow = exp(-max(distance, 0.0) / surface_width);
        surface_glow *= smoothstep(-body_radius * 0.035, -body_radius * 0.005, distance);
        surface_glow *= wrap * (0.3 + 0.7 * windward);
        surface_glow *= 1.0 - smoothstep(surface_width * 2.5, surface_width * 4.0, distance);

        float shoulder = bell((distance - body_radius * 0.025) / (body_radius * 0.075));
        shoulder *= smoothstep(-0.8, -0.1, facing) * (1.0 - smoothstep(0.15, 0.75, facing));
        shoulder *= smoothstep(-0.01 * body_radius, 0.015 * body_radius, distance);
        shoulder *= 1.0 - smoothstep(body_radius * 0.14, body_radius * 0.25, distance);

        vec3 flow = vec3(p.x, p.y * 0.4 + effect_time * body_radius * 4.0, p.z) / body_radius;
        float ripple = 0.88 + 0.24 * eddy(flow * 0.4);
        vec3 colour = mix(air_cool_colour, air_hot_colour, windward);
        colour = mix(colour, vec3(1.0, 0.91, 0.82), heat * windward * 0.15);
        vec3 bow_colour = mix(max(air_cool_colour, air_hot_colour), vec3(1.0), 0.65);
        float density = (shock / max(standoff, 0.01) + surface_glow * 0.4 / surface_width
            + bow * 0.025 / front.y + shoulder * 0.75 / body_radius) * ripple * intensity;
        float absorb = exp(-density * ds * 0.06);

        vec3 emission = shock * colour * 0.015 / max(standoff, 0.01) + shoulder * air_cool_colour * 0.19 / body_radius;
        emission += surface_glow * colour * 0.095 / surface_width;
        emission += bow * bow_colour * 0.0065 / front.y;
        total += transmission * emission * ripple * intensity * ds * brightness;
        transmission *= absorb;
        t += ds;

    }

    ALBEDO = total;
    ALPHA = 1.0 - transmission;

}
