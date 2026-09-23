uniform vec3 bounds_min = vec3(-1.0);
uniform vec3 bounds_max = vec3(1.0);
uniform sampler2D depth_buffer : hint_depth_texture, filter_nearest;
uniform sampler3D flow_noise : filter_linear, repeat_enable;
uniform float seed = 0.0;
uniform float effect_time = 0.0;

varying vec3 volume_position;
varying flat vec3 volume_camera;

// Stay in view/local space: subtracting world positions loses detail after an origin rebase.
void vertex() {

    VERTEX = mix(bounds_min, bounds_max, VERTEX + vec3(0.5));
    volume_position = VERTEX;
    volume_camera = (inverse(MODELVIEW_MATRIX) * vec4(0.0, 0.0, 0.0, 1.0)).xyz;

}

vec2 volume_interval(vec3 origin, vec3 ray, vec2 uv, mat4 inverse_projection) {

    vec3 safe_ray = mix(vec3(1.0), sign(ray), greaterThan(abs(ray), vec3(0.000001))) * max(abs(ray), vec3(0.000001));
    vec3 a = (bounds_min - origin) / safe_ray;
    vec3 b = (bounds_max - origin) / safe_ray;
    vec3 lo = min(a, b);
    vec3 hi = max(a, b);

    float near_hit = max(0.0, max(lo.x, max(lo.y, lo.z)));
    float far_hit = min(hi.x, min(hi.y, hi.z));
    float depth = textureLod(depth_buffer, uv, 0.0).r;

    if (depth > 0.0) {

        vec4 view = inverse_projection * vec4(uv * 2.0 - 1.0, depth, 1.0);
        far_hit = min(far_hit, length(view.xyz / view.w));

    }

    return vec2(near_hit, far_hit);

}

float grain(vec2 pixel) {

    return fract(52.9829189 * fract(dot(pixel, vec2(0.06711056, 0.00583715))));

}

float eddy(vec3 p) {

    return textureLod(flow_noise, p + vec3(seed * 0.013), 0.0).r;

}

// Incommensurate drift and domain warping prevent the tiled field from repeating as a moving stamp.
vec4 flow_eddies(vec3 p) {

    vec3 drift = effect_time * vec3(0.071, 0.113, 0.053);
    float a = eddy(p * 0.43 + drift);
    float b = eddy(p.zxy * 0.37 - drift.yzx + vec3(0.31, 0.67, 0.19));
    vec3 warped = p + vec3(a - 0.5, b - 0.5, a + b - 1.0) * 0.55;
    float middle = eddy(warped * 0.83 - drift * 0.73);
    float fine = eddy(warped * 1.67 + drift.zxy);

    return vec4(a, b, middle, fine);

}

float bell(float x) {

    return exp(-x * x);

}
