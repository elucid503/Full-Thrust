shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_draw_never, depth_test_disabled, cull_front, fog_disabled;

#include "res://Shaders/Volume.gdshaderinc"

uniform sampler3D density_volume : filter_linear, repeat_disable;
uniform float daylight = 1.0;
uniform vec3 sun_local = vec3(0.0, 1.0, 0.0);

vec2 condensate(vec3 p) {

    vec3 uv = (p - bounds_min) / (bounds_max - bounds_min);
    if (any(lessThan(uv, vec3(0.0))) || any(greaterThan(uv, vec3(1.0)))) { return vec2(0.0); }
    return textureLod(density_volume, uv, 0.0).rg;

}

void fragment() {

    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);
    if (interval.y <= interval.x) { discard; }
    float stride = (interval.y - interval.x) / 40.0;
    float transmission = 1.0;
    vec3 radiance = vec3(0.0);
    float jitter = grain(FRAGCOORD.xy + vec2(TIME * 17.0));
    for (int i = 0; i < 40; i++) {

        vec3 p = volume_camera + ray * (interval.x + (float(i) + jitter) * stride);
        vec2 material = condensate(p);
        float density = material.x + material.y;
        if (density < 0.001) { continue; }
        float dust = material.y / density;
        float extinction = density * mix(0.7, 1.1, dust);
        float shade = dot(condensate(p + sun_local * 3.0) + condensate(p + sun_local * 8.0), vec2(1.0));
        float phase = 0.75 + 0.75 * pow(max(dot(ray, sun_local), 0.0), 4.0);
        vec3 colour = mix(vec3(0.78, 0.84, 0.9), vec3(0.35, 0.26, 0.16), dust);
        vec3 lighting = vec3(0.11, 0.16, 0.23) + vec3(1.0, 0.92, 0.80) * exp(-shade * 2.2) * phase;
        float opacity = 1.0 - exp(-extinction * stride);
        radiance += transmission * opacity * colour * lighting * (0.03 + daylight);
        transmission *= 1.0 - opacity;
        if (transmission < 0.02) { break; }

    }
    ALPHA = 1.0 - transmission;
    ALBEDO = radiance;

}
