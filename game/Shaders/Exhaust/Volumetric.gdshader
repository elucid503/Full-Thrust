shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_front, depth_test_disabled, shadows_disabled, fog_disabled;

// Waterfall's Additive (Volumetric): the exact chord through a quadric plume, integrated with
// four-point Gauss-Legendre quadrature and textured where the ray enters and leaves the gas.
// With a receiving hull nearby, the stretch of the ray around the hull and its lee is marched
// through the contact field, so the jet wraps around it; the rest stays analytic and exact.
// Ordinary surface fog adds its blue ambient colour repeatedly across additive layers.

#include "res://Shaders/Exhaust/Volume.gdshaderinc"
#include "res://Shaders/Exhaust/Contact.gdshaderinc"
#include "res://Shaders/Exhaust/Waterfall.gdshaderinc"

uniform float expand_linear = 0.0;
uniform float expand_square = 0.0;
uniform float fresnel_fade_in = 0.0;
uniform float exit_radius = 0.5;

// (lowest height of the surface, end of its closing cap) and the cap's own quadric.
uniform vec2 plume_ends = vec2(-1.0, -1.5);
uniform vec3 cap_quadric = vec3(0.0, 0.0, 1.0);

// Sunlight scattered by expanded exhaust: 0 is pure emission, 1 is Henyey-Greenstein scattering.
uniform float sun_phase = 0.0;
uniform vec3 sun_local = vec3(0.0, 1.0, 0.0);

const vec4 NODES = vec4(0.069431844, 0.330009478, 0.669990522, 0.930568156);
const vec4 WEIGHTS = vec4(0.1739274226, 0.3260725774, 0.3260725774, 0.1739274226);
const int CONTACT_STEPS = 48;
const float FORWARD = 0.45;

// Waterfall's integrand at one point: emission per unit of plume-space path, before fade-in.
vec3 gas(vec3 q, vec3 ray, float a, float b, float texel) {

    float y = q.y;
    float reciprocal = 1.0 / max(1.0 + y * (a * y + b), 0.0001);

    // Path length measured in the plume's own proportions, lengthwise weighted.
    float value = sqrt(4.0 * length_brightness * length_brightness * ray.y * ray.y + dot(ray.xz, ray.xz) * reciprocal);
    float rr = min(dot(q.xz, q.xz) * reciprocal, 1.0);

    // Rounded tail: the rim fades out before the axis does.
    float tail = plume_ends.y;
    float fade_from = min(fade_out + tail, -1.0);
    value *= smoothstep(fade_from, max(fade_out - 1.0, fade_from + 0.001),
        y - max(fade_out, -1.0 - tail) * (1.0 - sqrt(1.0 - rr)));

    float f = min(0.0, y + falloff_start);
    f *= -f;
    float decay = exp2(f * 5.0 * falloff);
    value *= decay;
    vec3 colour = mix(end_tint, start_tint, clamp(exp2(f * tint_falloff * 5.0 - rr * rr * tint_fresnel * 5.0), 0.0, 1.0));

    float rim = rr - 1.0;
    value *= exp2(-10.0 * fresnel_invert * rim * rim);

    float edge = (fresnel + 0.01) * (1.0 - exp2(2.0 * min(0.0, fresnel_fade_in * y + fresnel_fade_in - 1.0)));
    edge *= edge;
    edge *= rr / (rr - (0.15 * edge + 1.0));
    decay *= exp2(0.5 * noise_fresnel * edge);
    value *= exp2(edge);
    value *= mix(1.0, texel, noise_amount * (1.0 - decay) * rr);

    return colour * value;

}

vec3 integrate(vec3 origin, vec3 ray, vec2 span, float a, float b, float lod) {

    vec2 texel = vec2(plume_texture(origin + ray * span.x, lod), plume_texture(origin + ray * span.y, lod));
    vec3 total = vec3(0.0);

    for (int k = 0; k < 4; k++) {

        vec3 q = origin + ray * mix(span.x, span.y, NODES[k]);
        total += WEIGHTS[k] * gas(q, ray, a, b, mix(texel.x, texel.y, NODES[k]));

    }

    float softness = fade_in + 0.0001;
    vec2 heights = origin.y + span * ray.y;
    float rise = heights.y - heights.x;
    float entrance = abs(rise) > 0.0001
        ? -smoothstep_integral(-heights / softness) * softness / rise
        : smoothstep(0.0, 1.0, -heights.x / softness);

    return total * (span.y - span.x) * entrance;

}

vec3 analytic(vec3 origin, vec3 direction, vec2 range, float a, float b, float lod) {

    vec3 total = vec3(0.0);

    if (range.y <= range.x) {

        return total;

    }

    vec4 spans;
    int count = plume_chord(origin, direction, vec3(a, b, 1.0), vec2(max(plume_ends.x, plume_floor), 0.0), range, spans);
    if (count > 0) { total += integrate(origin, direction, spans.xy, a, b, lod); }
    if (count > 1) { total += integrate(origin, direction, spans.zw, a, b, lod); }

    count = plume_chord(origin, direction, cap_quadric, vec2(max(plume_ends.y, plume_floor), min(-1.0, plume_ends.x)), range, spans);
    if (count > 0) { total += integrate(origin, direction, spans.xy, a, b, lod); }
    if (count > 1) { total += integrate(origin, direction, spans.zw, a, b, lod); }

    return total;

}

vec2 box_hit(vec3 origin, vec3 ray, vec3 low, vec3 high) {

    vec3 safe = mix(vec3(1.0), sign(ray), greaterThan(abs(ray), vec3(0.000001))) * max(abs(ray), vec3(0.000001));
    vec3 a = (low - origin) / safe;
    vec3 b = (high - origin) / safe;
    vec3 near = min(a, b);
    vec3 far = max(a, b);
    return vec2(max(near.x, max(near.y, near.z)), min(far.x, min(far.y, far.z)));

}

bool inside(vec3 q, float a, float b) {

    if (q.y > 0.0 || q.y < max(plume_ends.y, plume_floor)) { return false; }

    float r2 = dot(q.xz, q.xz);
    if (q.y >= min(-1.0, plume_ends.x)) { return q.y >= plume_ends.x && r2 <= 1.0 + q.y * (a * q.y + b); }
    return r2 <= cap_quadric.z + q.y * (cap_quadric.y + cap_quadric.x * q.y);

}

void fragment() {

    // Derivatives before any fragment leaves the quad.
    float lod = plume_lod(volume_position);
    float pixel_width = length(dFdx(volume_position)) + length(dFdy(volume_position));
    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);
    if (interval.y <= interval.x) { discard; }

    vec3 origin = plume_map(volume_camera);
    vec3 direction = plume_map(ray);
    float stretch = length(direction);
    direction /= stretch;

    float a = expand_linear * expand_linear - 10.0 * expand_square;
    float b = -2.0 * expand_linear - 10.0 * expand_square;
    vec3 total = vec3(0.0);

    vec2 range = interval * stretch;
    vec2 marched = vec2(1.0, 0.0);

    if (contact_count > 0) {

        // Contact only acts beside the receivers and in their lee, downstream along the jet.
        float margin = max(exit_radius * 0.95, 0.12) * 6.0;
        vec3 low = contact_bounds_min0 - vec3(margin);
        vec3 high = contact_bounds_max0 + vec3(margin);

        if (contact_count > 1) {

            low = min(low, contact_bounds_min1 - vec3(margin));
            high = max(high, contact_bounds_max1 + vec3(margin));

        }

        low.y = bounds_min.y;
        marched = box_hit(volume_camera, ray, low, high);
        marched = vec2(max(marched.x, interval.x), min(marched.y, interval.y));

    }

    if (marched.y > marched.x) {

        total += analytic(origin, direction, vec2(interval.x, marched.x) * stretch, a, b, lod);
        total += analytic(origin, direction, vec2(marched.y, interval.y) * stretch, a, b, lod);

        // Fine steps over the short marched stretch; jitter trades banding for fine grain.
        float ds = (marched.y - marched.x) / float(CONTACT_STEPS);
        float jitter = 0.35 + 0.3 * grain(FRAGCOORD.xy + vec2(seed));
        float softness = fade_in + 0.0001;

        for (int i = 0; i < CONTACT_STEPS; i++) {

            vec3 p = volume_camera + ray * (marched.x + (float(i) + jitter) * ds);
            float mask;
            float wake;
            p = contact_flow(p, exit_radius, max(ds, pixel_width), mask, wake);
            if (mask <= 0.001) { continue; }

            vec3 q = plume_map(p);
            if (!inside(q, a, b)) { continue; }

            float entrance = smoothstep(0.0, 1.0, -q.y / softness);
            total += gas(q, direction, a, b, plume_texture(q, lod)) * entrance * mask * (1.0 + wake) * ds * stretch;

        }

    }
    else {

        total += analytic(origin, direction, range, a, b, lod);

    }

    // Light travels away from the sun and towards the eye; forward scattering peaks looking sunward.
    float cosine = dot(ray, sun_local);
    float phase = (1.0 - FORWARD * FORWARD) / pow(1.0 + FORWARD * FORWARD - 2.0 * FORWARD * cosine, 1.5);
    total *= mix(1.0, phase, sun_phase);

    ALBEDO = clamp(0.5 * brightness * total, vec3(0.0), vec3(clip_brightness));
    ALPHA = 1.0;

}
