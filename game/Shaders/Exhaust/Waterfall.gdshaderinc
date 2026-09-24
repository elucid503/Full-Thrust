// Waterfall's plume space: the exit at y = 0, the authored length at y = -1, the layer radius at r = 1.
// Local metres map in by a scale and a linear crossflow shear, so every surface stays a quadric.
// Parameters keep Waterfall's names, ranges and time base, so its templates translate directly.

uniform vec3 start_tint : source_color = vec3(1.0);
uniform vec3 end_tint : source_color = vec3(1.0);
uniform float tint_falloff = 0.0;
uniform float tint_fresnel = 0.0;
uniform float falloff = 0.0;
uniform float falloff_start = 0.0;
uniform float fresnel = 0.0;
uniform float fresnel_invert = 0.0;
uniform float brightness = 1.0;
uniform float length_brightness = 1.0;
uniform float clip_brightness = 50.0;
uniform float noise_amount = 0.0;
uniform float noise_fresnel = 0.0;
uniform float fade_in = 0.0;
uniform float fade_out = 0.0;
uniform vec2 tile = vec2(1.0);
uniform vec2 speed = vec2(0.0, 1.0);
uniform sampler2D noise_texture : filter_linear_mipmap, repeat_enable;

uniform vec3 plume_scale = vec3(1.0);
uniform vec2 plume_shear = vec2(0.0);
uniform float plume_floor = -4.0;

vec3 plume_map(vec3 v) {

    v.xz += plume_shear * (v.y / plume_scale.y);
    return v / plume_scale;

}

float plume_lod(vec3 position) {

    float footprint = max(length(dFdx(position)), length(dFdy(position))) / max(plume_scale.x, 0.001);
    return log2(max(footprint * max(tile.x, tile.y) * float(textureSize(noise_texture, 0).x) * 0.5, 1.0));

}

// Unity's _Time.x runs at a twentieth of a second, and Waterfall's scroll speeds assume it.
float waterfall_time() {

    return effect_time / 20.0;

}

// Cylindrical lookup, mirrored around the circumference so the texture has no seam.
float plume_texture(vec3 p, float lod) {

    float time = waterfall_time();
    float mirror = speed.x * time;
    float u = 0.1591549431 * atan(p.z, p.x) + step(p.z, 0.0);
    u = 2.0 * abs(u - mirror - floor(u - mirror + 0.5)) + mirror;
    vec2 uv = (vec2(u, mix(0.5, 1.0, 1.0 + p.y)) + seed) * tile + vec2(-speed.x, speed.y * tile.y) * time;
    return textureLod(noise_texture, uv, lod).r * 1.7320508;

}

// Integral of smoothstep(0, 1, x) between x.x and x.y.
float smoothstep_integral(vec2 x) {

    x = clamp(x * x * x, 0.0, 1.0) * (max(x, 1.0) - 0.5 * min(x, 1.0));
    return x.y - x.x;

}

// Up to two spans of the ray where r² <= q.z + q.y·y + q.x·y², between two heights and inside the
// depth-clipped range. A hyperboloid seen along its axis is entered, left and re-entered.
int plume_chord(vec3 origin, vec3 ray, vec3 q, vec2 heights, vec2 range, out vec4 spans) {

    spans = vec4(0.0);

    if (abs(ray.y) > 0.000001) {

        vec2 t = (heights - origin.y) / ray.y;
        range = vec2(max(range.x, min(t.x, t.y)), min(range.y, max(t.x, t.y)));

    }
    else if (origin.y < heights.x || origin.y > heights.y) {

        return 0;

    }

    if (range.y <= range.x) {

        return 0;

    }

    float a = dot(ray.xz, ray.xz) - q.x * ray.y * ray.y;
    float b = 2.0 * dot(origin.xz, ray.xz) - q.y * ray.y - 2.0 * q.x * origin.y * ray.y;
    float c = dot(origin.xz, origin.xz) - q.z - q.y * origin.y - q.x * origin.y * origin.y;

    if (abs(a) < 0.0000001) {

        if (abs(b) < 0.0000001) {

            spans.xy = range;
            return c <= 0.0 ? 1 : 0;

        }

        float root = -c / b;
        spans.xy = b > 0.0 ? vec2(range.x, min(range.y, root)) : vec2(max(range.x, root), range.y);
        return spans.y > spans.x ? 1 : 0;

    }

    float discriminant = b * b - 4.0 * a * c;

    if (discriminant < 0.0) {

        spans.xy = range;
        return a < 0.0 ? 1 : 0;

    }

    float root = sqrt(discriminant);
    vec2 roots = vec2(-b - root, -b + root) / (2.0 * a);
    roots = vec2(min(roots.x, roots.y), max(roots.x, roots.y));

    if (a > 0.0) {

        spans.xy = vec2(max(range.x, roots.x), min(range.y, roots.y));
        return spans.y > spans.x ? 1 : 0;

    }

    int count = 0;
    vec2 first = vec2(range.x, min(range.y, roots.x));
    vec2 second = vec2(max(range.x, roots.y), range.y);

    if (first.y > first.x) {

        spans.xy = first;
        count = 1;

    }

    if (second.y > second.x) {

        if (count == 0) {

            spans.xy = second;

        }
        else {

            spans.zw = second;

        }

        count++;

    }

    return count;

}
