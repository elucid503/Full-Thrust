shader_type spatial;
render_mode unshaded, blend_premul_alpha, cull_front, depth_draw_never, depth_test_disabled, shadows_disabled, fog_disabled;

#include "res://Shaders/EntryField.gdshaderinc"

void fragment() {

    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);

    if (interval.y <= interval.x) { discard; }

    int count = 48;
    float ds = (interval.y - interval.x) / float(count);
    float jitter = mix(0.25, 0.75, grain(FRAGCOORD.xy));
    vec3 total = vec3(0.0);
    float transmission = 1.0;

    for (int i = 0; i < 48; i++) {

        vec3 p = volume_camera + ray * (interval.x + (float(i) + jitter) * ds);
        float downstream = max(-p.y, 0.0);
        float expansion = 1.0 + downstream * 0.075 / body_radius;
        vec3 advect = vec3(p.x, p.y * 0.24 + effect_time * body_radius * 5.0, p.z) / body_radius;
        vec4 eddies = flow_eddies(advect * vec3(0.52, 0.35, 0.52));
        float n = eddies.x * 0.5 + eddies.z * 0.35 + eddies.w * 0.15;
        vec2 warp = (eddies.xy - vec2(0.5)) * 2.0;
        float churn = smoothstep(0.0, body_radius * 3.0, downstream);
        vec3 shape = footprint_at((p.xz + warp * min(downstream * 0.15, body_radius * 1.1)) / expansion);
        float behind_front = shape.r - p.y;
        float behind_body = shape.g - p.y;
        float travel = max(behind_front, 0.0) / wake_length;
        float edge = shape.b * expansion + (eddies.y - 0.5) * body_radius * churn;
        float width = body_radius * 0.13 + max(behind_front, 0.0) * 0.065;
        float shear = bell((edge - standoff * 0.7) / width);
        float start = smoothstep(-standoff * 0.7, body_radius * 0.22, behind_front);
        float finish = 1.0 - smoothstep(0.55, 1.0, travel);
        float tail = exp(-travel * 3.4) * finish;

        // The perimeter feeds continuous streamlines; the recirculating core only appears behind the hull.
        float core = (1.0 - smoothstep(-width, width, edge)) * smoothstep(0.0, body_radius * 1.5, behind_body) * 0.14;
        float outside = smoothstep(0.0, body_radius * 0.1, hull_at(p).a);
        float turbulence = mix(0.7 + 0.5 * n, 0.08 + 1.9 * smoothstep(0.27, 0.72, n), churn);
        float density = (shear * 0.4 + core) * start * tail * outside * turbulence * intensity / body_radius;
        float streaks = pow(eddies.w, 4.0) * ablation * 2.0;
        vec3 colour = mix(air_cool_colour, air_hot_colour, exp(-travel * cooling_rate) * (0.5 + 0.5 * eddies.z));
        colour = mix(colour, ablation_colour, ablation * (0.2 + 0.4 * eddies.z));
        vec3 emission = density * (colour + streaks * ablation_colour);
        float absorb = exp(-density * ds * (0.08 + ablation * 0.06));

        total += transmission * emission * ds * brightness;
        transmission *= absorb;

    }

    ALBEDO = total;
    ALPHA = 1.0 - transmission;

}
