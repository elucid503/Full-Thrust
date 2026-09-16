shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

uniform float sample_phase = 0.0;
uniform vec3 bounds_min;
uniform vec3 bounds_max;
uniform mat4 local_from_view;
uniform mat4 inverse_projection;
uniform vec2 buffer_size;

#include "res://Shaders/SurfaceCloud.gdshaderinc"

void fragment() {

    vec2 uv = FRAGCOORD.xy / buffer_size;
    vec4 view = inverse_projection * vec4(uv * vec2(2.0, -2.0) + vec2(-1.0, 1.0), 1.0, 1.0);
    vec3 ray = normalize(mat3(local_from_view) * (view.xyz / view.w));
    vec3 origin = local_from_view[3].xyz;
    vec3 safe_ray = mix(vec3(1.0), sign(ray), greaterThan(abs(ray), vec3(0.000001))) * max(abs(ray), vec3(0.000001));
    vec3 a = (bounds_min - origin) / safe_ray;
    vec3 b = (bounds_max - origin) / safe_ray;
    vec3 lo = min(a, b), hi = max(a, b);
    vec2 interval = vec2(max(0.0, max(lo.x, max(lo.y, lo.z))), min(hi.x, min(hi.y, hi.z)));
    COLOR = march_cloud(origin, ray, interval, FRAGCOORD.xy);

}
