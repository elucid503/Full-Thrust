shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_draw_never, depth_test_disabled, cull_front, fog_disabled;

#include "res://Shaders/Volume.gdshaderinc"
#include "res://Shaders/SurfaceCloud.gdshaderinc"

uniform sampler2D cloud_screen : filter_linear, repeat_disable;
uniform bool screen_enabled = false;

void fragment() {

    if (!cloud_enabled) { discard; }
    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);
    if (interval.y <= interval.x) { discard; }
    vec3 safe_ray = mix(vec3(1.0), sign(ray), greaterThan(abs(ray), vec3(0.000001))) * max(abs(ray), vec3(0.000001));
    vec3 ends = max((bounds_min - volume_camera) / safe_ray, (bounds_max - volume_camera) / safe_ray);
    float far_hit = min(ends.x, min(ends.y, ends.z));
    float height = dot(volume_camera, ground_plane.xyz) + ground_plane.w;
    float slope = dot(ray, ground_plane.xyz);
    if (slope < -0.00001) { far_hit = min(far_hit, -height / slope); }
    vec4 cloud;
    // Foreground geometry inside the volume retains exact depth-clipped integration.
    if (screen_enabled && interval.y >= far_hit - 0.01) {

        cloud = textureLod(cloud_screen, SCREEN_UV, 0.0);

    } else {

        cloud = march_cloud(volume_camera, ray, interval, FRAGCOORD.xy);

    }
    ALBEDO = cloud.rgb;
    ALPHA = cloud.a;

}
