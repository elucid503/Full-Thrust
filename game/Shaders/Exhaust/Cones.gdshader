shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_front, depth_test_disabled, shadows_disabled, fog_disabled;

// Waterfall's Additive Cones (Volumetric): the shock train as a chain of cells whose radius swells
// and pinches, each cell an ellipsoid fitted through both pinches and one flank. Cell length,
// growth, phase and amplitude are what throttle, back pressure and combustion noise move.

#include "res://Shaders/Exhaust/Volume.gdshaderinc"
#include "res://Shaders/Exhaust/Waterfall.gdshaderinc"

uniform float cone_length = 0.5;
uniform float cone_stretch = 0.0;
uniform float exit_length = 1.0;
uniform float exit_start = 0.5;
uniform float cone_expansion = 0.0;
uniform float cone_fade = 0.0;
uniform float cone_fade_start = 0.0;
uniform float smoothness = 0.1;
uniform float asymmetry = 0.0;

const int MAX_CONES = 12;

float amplitude(float y) {

    return cone_expansion * smoothstep(-1.0 / (cone_fade + 0.001), 0.0, y + cone_fade_start);

}

// One wavelength's profile over local -1..1: 0 at the pinches, 1 at the swell, rounded by smoothness.
float wave(float local) {

    float soft = clamp(smoothness, 0.001, 0.499);
    float skew = clamp(asymmetry, -0.3, 0.3);
    float side = sign(skew - local);
    local = (local - skew) / (1.0 + skew * side);
    float inner = step(abs(local), 1.0 - soft);
    local += side * (1.0 - inner);
    return local * local / (soft - inner) + inner;

}

float cone_radius(float y, float local, float base) {

    return amplitude(y) * (wave(local) - 1.0) + base;

}

float intensity(float mean, float height, float rr, float outer, float texel, out float decay) {

    decay = min(1.0, exp2(falloff * (height + falloff_start)));
    float edge = 1.0 - pow(rr, 0.6 + 1.0 / (fresnel * (1.5 - decay) + 0.0001));
    float fadeout = max(0.0, 2.0 * smoothstep(-0.0001 - fade_out, fade_out, mean + fade_out * (sqrt(1.0 - rr) - 1.0) + 1.0) - 1.0);
    float mottle = max(0.0, mix(mix(1.0, texel, (0.6 * rr + 0.4) * (noise_amount * (1.3 - 0.3 * fadeout) + noise_fresnel * (1.0 - edge))), 1.0, decay));
    float shell = max((rr + 0.2 * outer - 0.2) / (0.2 * outer + 0.8), 0.0);
    float hollow = 1.0 + fresnel_invert / (fresnel_invert + 0.3) * (pow(shell, 0.3 * fresnel_invert + 1.0) - 1.0);
    return decay * edge * mottle * fadeout * smoothstep(-0.0001, fade_in, -mean) * hollow;

}

void fragment() {

    // Derivatives before any fragment leaves the quad.
    float lod = plume_lod(volume_position);
    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);
    if (interval.y <= interval.x) { discard; }

    vec3 origin = plume_map(volume_camera);
    vec3 direction = plume_map(ray);
    float stretch = length(direction);
    direction /= stretch;
    vec2 range = interval * stretch;

    // Lay the cells out exactly as Waterfall's cone mesh does.
    float start = fract(exit_start);
    float growth = abs(cone_stretch) < 0.001 ? 0.0 : cone_stretch;
    float wavelength;
    int last;

    if (growth == 0.0) {

        wavelength = max(cone_length, 1.0 / (float(MAX_CONES - 1) + exit_length * (1.0 - start)));
        last = int(max(0.0, ceil(1.0 + 1.0 / wavelength - exit_length * (1.0 - start)) - 1.0));

    }
    else {

        float shortest = 1.0 / ((1.0 - exp2(growth * float(MAX_CONES - 1))) / (1.0 - exp2(growth)) + exit_length * (1.0 - start));
        wavelength = max(cone_length, shortest);
        float remaining = max(1.0 + (1.0 - exp2(growth)) * (-1.0 / wavelength + exit_length * (1.0 - start)), 0.000001);
        last = cone_length <= shortest ? MAX_CONES - 1 : int(max(0.0, ceil(log2(remaining) / growth + 1.0) - 1.0));

    }

    last = min(last, MAX_CONES - 1);
    float base = 1.0 + amplitude(0.0) * (1.0 - wave(1.0 - 2.0 * start));
    float first = exit_length * wavelength;
    float probe = -0.9 + clamp(smoothness, 0.001, 0.499);
    vec3 total = vec3(0.0);

    for (int i = 0; i <= last; i++) {

        float cell;
        float top;

        if (i == 0) {

            cell = first;
            top = first * start;

        }
        else {

            cell = wavelength * exp2(growth * float(i - 1));
            top = growth == 0.0
                ? -first * (1.0 - start) - wavelength * float(i - 1)
                : -first * (1.0 - start) - (wavelength - cell) / (1.0 - exp2(growth));

        }

        float bottom = top - cell;
        float lowest = max(max(bottom, -1.0), plume_floor);
        if (top <= lowest) { break; }

        float centre = top - 0.5 * cell;
        float upper = base - amplitude(top);
        float lower = base - amplitude(bottom);
        float flank = cone_radius(centre + 0.5 * cell * probe, probe, base);
        upper *= upper;
        lower *= lower;
        flank *= flank;
        float slope = 0.5 * (upper - lower);
        float curve = (flank - slope * probe - 0.5 * (upper + lower)) / (probe * probe - 1.0);
        float level = 0.5 * (upper + lower) - curve;

        // The same ellipsoid in height rather than in the cell's local coordinate.
        float k = 2.0 / cell;
        vec3 q = vec3(curve * k * k, slope * k - 2.0 * curve * k * k * centre,
            curve * k * k * centre * centre - slope * k * centre + level);

        vec4 spans;
        int count = plume_chord(origin, direction, q, vec2(lowest, min(top, 0.0)), range, spans);

        for (int j = 0; j < count; j++) {

            vec2 span = j == 0 ? spans.xy : spans.zw;
            vec3 deepest = origin + direction * (0.5 * (span.x + span.y));
            float local = clamp((deepest.y - centre) * k, -1.0, 1.0);
            float widest = cone_radius(deepest.y, local, base);
            widest *= widest;
            float rr = min(dot(deepest.xz, deepest.xz) / max(widest, 0.000001), 1.0);
            vec3 entry = origin + direction * span.x;
            float decay;
            float glow = intensity(deepest.y, entry.y, rr, base * base / max(widest, 0.000001), plume_texture(entry, lod), decay);
            vec3 colour = mix(end_tint, start_tint, pow(decay, tint_falloff) * pow(1.001 - rr, tint_fresnel));
            total += glow * colour * (span.y - span.x);

        }

    }

    float weight = length(vec3(4.0 * length_brightness * direction.y, direction.xz));
    ALBEDO = clamp(0.5 * brightness * weight * total, vec3(0.0), vec3(clip_brightness));
    ALPHA = 1.0;

}
