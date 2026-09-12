shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_draw_never, depth_test_disabled, cull_front;

#include "res://Shaders/Volume.gdshaderinc"

uniform int puff_count = 0;
uniform vec4 puff_centres[56];
uniform vec4 puff_states[56];
uniform vec4 ground_plane = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec3 field_origin = vec3(0.0);
uniform float detail_scale = 0.03;
uniform float erosion = 0.66;
uniform float column_height = 60.0;

uniform vec3 sun_direction = vec3(0.0, 1.0, 0.0);
uniform float daylight = 1.0;
uniform vec3 sun_light : source_color = vec3(1.0, 0.95, 0.88);
uniform vec3 sky_light : source_color = vec3(0.38, 0.50, 0.68);
uniform vec3 bounce_light : source_color = vec3(0.36, 0.31, 0.24);
uniform vec3 dust_albedo : source_color = vec3(0.62, 0.56, 0.47);
uniform vec3 steam_albedo : source_color = vec3(0.92, 0.93, 0.95);

float remap(float v, float low, float high, float target_low, float target_high) {

    return target_low + (v - low) * (target_high - target_low) / max(high - low, 0.0001);

}

// Normalised so the mean over the sphere is one, which keeps the light rig independent of the lobe.
float henyey_greenstein(float mu, float g) {

    float gg = g * g;

    return (1.0 - gg) / pow(max(1.0 + gg - 2.0 * g * mu, 0.0001), 1.5);

}

// Inverted Worley reads as billows; two warped octaves give the cauliflower its own cauliflower.
float billows(vec3 w) {

    float coarse = 1.0 - eddy(w);
    vec3 warped = w * 2.7 + vec3(coarse * 0.4);
    float medium = 1.0 - eddy(warped);
    float fine = 1.0 - eddy(warped * 2.9 + vec3(0.0, effect_time * 0.04, 0.0));

    return coarse * 0.58 + medium * 0.29 + fine * 0.13;

}

// The metaball falloff integrates analytically along a chord, so shadowing needs no second march.
float sun_depth(vec3 p) {

    float depth = 0.0;

    for (int j = 0; j < puff_count; j++) {

        vec3 offset = puff_centres[j].xyz - p;
        float along = dot(offset, sun_direction);
        float radius = puff_centres[j].w;

        if (along + radius <= 0.0) { continue; }

        float across = dot(offset, offset) - along * along;
        float support = radius * radius;

        if (across >= support) { continue; }

        float half_chord = sqrt(support - across);
        float path = max(along + half_chord, 0.0) - max(along - half_chord, 0.0);
        depth += path * puff_states[j].x * (1.0 - across / support) * 0.667;

        if (depth > 8.0) { break; }

    }

    return depth;

}

void fragment() {

    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);

    if (interval.y <= interval.x) {

        discard;

    }

    // A constant world stride keeps grazing rays cheap instead of spending a full march on a clipped corner.
    float span = interval.y - interval.x;
    float coarse = max(length(bounds_max - bounds_min), 0.001) / 44.0;
    int steps = int(clamp(span / coarse, 8.0, 48.0));
    float stride = span / float(steps);
    float jitter = grain(FRAGCOORD.xy);
    float mu = dot(ray, sun_direction);
    float transmission = 1.0;
    vec3 light = vec3(0.0);
    float shadow = 0.0;
    int stale = 3;

    for (int i = 0; i < 48; i++) {

        if (i >= steps) { break; }

        vec3 p = volume_camera + ray * (interval.x + (float(i) + jitter) * stride);
        float height = dot(p, ground_plane.xyz) + ground_plane.w;

        if (height < 0.0) { continue; }

        float cover = 0.0;
        float mass = 0.0;
        float wet = 0.0;

        for (int j = 0; j < puff_count; j++) {

            vec4 state = puff_states[j];
            vec3 q = (p - puff_centres[j].xyz) / puff_centres[j].w;
            q += ground_plane.xyz * dot(q, ground_plane.xyz) * mix(0.34, 0.0, state.y);
            float r2 = dot(q, q);

            if (r2 >= 1.0) { continue; }

            float shape = 1.0 - r2;
            cover += shape;
            mass += shape * state.x;
            wet += shape * state.y;

        }

        if (cover < 0.002) { continue; }

        // Remapping the envelope against the erosion field pushes the boundary inward without dimming
        // the interior, which is what turns a union of spheres into billows rather than smaller spheres.
        float envelope = min(cover, 1.0);
        float carve = billows((p - field_origin) * detail_scale) * erosion;
        float density = clamp(remap(envelope, carve, 1.0, 0.0, 1.0), 0.0, 1.0);

        if (density < 0.002) { continue; }

        float extinction = mass / cover * density;

        // Beyond this the sample barely reaches the camera, so the last shadow is close enough.
        if (stale >= 3 && transmission > 0.1) {

            shadow = sun_depth(p);
            stale = 0;

        }
        else {

            stale++;

        }

        // Wrenninge's octave approximation: each pass is a dimmer, blurrier, more isotropic bounce.
        float energy = 0.0;
        float attenuation = 1.0;
        float contribution = 1.0;
        float eccentricity = 1.0;

        for (int n = 0; n < 3; n++) {

            float phase = mix(henyey_greenstein(mu, 0.72 * eccentricity),
                henyey_greenstein(mu, -0.24 * eccentricity), 0.28);
            energy += contribution * exp(-shadow * attenuation) * phase;
            attenuation *= 0.48;
            contribution *= 0.56;
            eccentricity *= 0.62;

        }

        // The powdered-sugar term: edges facing the sun darken because little has scattered there yet.
        float powder = 1.0 - exp(-density * 3.0);
        float buried = exp(-cover * 1.2);
        vec3 ambient = mix(bounce_light, sky_light, clamp(height / column_height, 0.0, 1.0))
            * (0.25 + 0.75 * buried);

        vec3 albedo = mix(dust_albedo, steam_albedo, wet / cover);
        vec3 scattering = albedo * (sun_light * daylight * energy * mix(0.45, 1.0, powder) + ambient);
        float absorb = exp(-extinction * stride);
        light += transmission * scattering * (1.0 - absorb);
        transmission *= absorb;

        if (transmission < 0.02) { break; }

    }

    ALPHA = 1.0 - transmission;
    ALBEDO = light;

}
