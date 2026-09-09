shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_draw_never, depth_test_disabled, cull_front;

#include "res://Shaders/Volume.gdshaderinc"

uniform float strength = 0.0;
uniform float radius = 2.0;
uniform float daylight = 1.0;

void fragment() {

    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);
    if (interval.y <= interval.x) { discard; }
    float stride = (interval.y - interval.x) / 24.0;
    float transmission = 1.0;
    for (int i = 0; i < 24; i++) {

        vec3 p = volume_camera + ray * (interval.x + (float(i) + grain(FRAGCOORD.xy)) * stride);
        float height = max(p.y, 0.0) / radius;
        vec2 drift = vec2(height * 0.22, height * 0.12);
        float width = radius * (0.8 + height * 0.35);
        float envelope = exp(-dot(p.xz - drift * radius, p.xz - drift * radius) / (width * width));
        envelope *= smoothstep(-0.5, 0.5, p.y) * (1.0 - smoothstep(2.0, 9.0, height));
        float noise = eddy(p / (radius * 2.0) - vec3(0.04, 0.3, 0.02) * effect_time);
        float density = envelope * smoothstep(0.2, 0.7, noise) * strength;
        transmission *= exp(-density * stride * 0.7 / radius);
        if (transmission < 0.02) { break; }

    }
    ALPHA = 1.0 - transmission;
    ALBEDO = mix(vec3(0.035, 0.045, 0.06), vec3(0.72, 0.79, 0.84), daylight) * ALPHA;

}
