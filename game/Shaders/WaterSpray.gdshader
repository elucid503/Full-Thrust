shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_draw_never, depth_test_disabled, cull_front;

#include "res://Shaders/Volume.gdshaderinc"

uniform float spray_radius = 3.0;
uniform float spray_height = 1.5;
uniform float spray_strength = 0.0;
uniform vec3 spray_wind = vec3(0.0);
uniform float daylight = 1.0;

void fragment() {

    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);
    if (interval.y <= interval.x || spray_strength < 0.002) { discard; }
    float stride = (interval.y - interval.x) / 24.0;
    float transmission = 1.0;
    vec3 light = vec3(0.0);
    for (int i = 0; i < 24; i++) {

        vec3 p = volume_camera + ray * (interval.x + (float(i) + 0.5) * stride);
        if (p.y <= 0.0) { continue; }
        vec2 swept = p.xz - spray_wind.xz * min(p.y * 0.07, 0.3);
        float radial = length(swept) / max(spray_radius, 0.5);
        float height = p.y / spray_height;
        float sheet = exp(-height * 2.8) * smoothstep(0.0, 0.15, height);
        sheet *= smoothstep(0.15, 0.65, radial) * exp(-radial * radial * 0.55);
        vec4 noise = flow_eddies(vec3(swept.x / spray_radius, height * 1.7, swept.y / spray_radius) * 2.5);
        float wisps = smoothstep(0.32, 0.72, noise.z * 0.65 + noise.w * 0.35);
        float density = sheet * wisps * spray_strength * 0.55;
        float absorb = exp(-density * stride);
        vec3 colour = mix(vec3(0.08, 0.12, 0.17), vec3(0.60, 0.68, 0.72), daylight);
        light += transmission * (1.0 - absorb) * colour;
        transmission *= absorb;

    }
    ALBEDO = light;
    ALPHA = 1.0 - transmission;

}
