uniform vec3 bounds_min = vec3(-1.0);
uniform vec3 bounds_max = vec3(1.0);
uniform sampler2D depth_buffer : hint_depth_texture, filter_nearest;
uniform sampler3D flow_noise : filter_linear, repeat_enable;
uniform float seed = 0.0;
uniform float effect_time = 0.0;
uniform float sample_phase = 0.0;

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

// Ray clipping against a finite circular cone. Its convex support leaves one occupied interval.
vec2 cone_interval(vec3 o, vec3 d, vec2 interval, float reach, float radius, float spread) {

    if (abs(d.y) > 0.000001) {

        vec2 ends = vec2(-o.y, -reach - o.y) / d.y;
        interval = vec2(max(interval.x, min(ends.x, ends.y)), min(interval.y, max(ends.x, ends.y)));

    } else if (o.y > 0.0 || o.y < -reach) { return vec2(0.0); }
    float r = radius - spread * o.y;
    float a = dot(d.xz, d.xz) - spread * spread * d.y * d.y;
    float b = 2.0 * (dot(o.xz, d.xz) + r * spread * d.y);
    float c = dot(o.xz, o.xz) - r * r;
    float discriminant = b * b - 4.0 * a * c;
    if (abs(a) < 0.000001) {

        if (abs(b) > 0.000001) {

            if (b > 0.0) { interval.y = min(interval.y, -c / b); }
            else { interval.x = max(interval.x, -c / b); }

        } else if (c > 0.0) { return vec2(0.0); }

    } else if (discriminant >= 0.0) {

        vec2 roots = vec2(-b - sqrt(discriminant), -b + sqrt(discriminant)) / (2.0 * a);
        roots = vec2(min(roots.x, roots.y), max(roots.x, roots.y));
        if (a > 0.0) { interval = vec2(max(interval.x, roots.x), min(interval.y, roots.y)); }
        else {

            float middle = (interval.x + interval.y) * 0.5;
            if (middle < (roots.x + roots.y) * 0.5) { interval.y = min(interval.y, roots.x); }
            else { interval.x = max(interval.x, roots.y); }

        }

    } else if (a > 0.0) { return vec2(0.0); }
    return interval;

}
