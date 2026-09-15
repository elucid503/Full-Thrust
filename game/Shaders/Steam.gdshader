shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_draw_never, depth_test_disabled, cull_front, fog_disabled;

#include "res://Shaders/Volume.gdshaderinc"

uniform sampler3D cloud_field : filter_linear, repeat_disable;
uniform bool cloud_enabled = false;
uniform vec3 field_min = vec3(-1.0);
uniform vec3 field_max = vec3(1.0);
uniform vec3 source_point = vec3(0.0);
uniform float source_radius = 1.0;
uniform float source_power = 0.0;
uniform float source_wet = 0.0;
uniform vec4 ground_plane = vec4(0.0, 1.0, 0.0, 0.0);
uniform float column_height = 60.0;

uniform vec3 sun_direction = vec3(0.0, 1.0, 0.0);
uniform float daylight = 1.0;
uniform vec3 sun_light : source_color = vec3(1.0, 0.95, 0.88);
uniform vec3 sky_light : source_color = vec3(0.38, 0.50, 0.68);
uniform vec3 bounce_light : source_color = vec3(0.36, 0.31, 0.24);
uniform vec3 dust_albedo : source_color = vec3(0.62, 0.56, 0.47);
uniform vec3 steam_albedo : source_color = vec3(0.92, 0.93, 0.95);

// Normalised so the mean over the sphere is one, which keeps the light rig independent of the lobe.
float henyey_greenstein(float mu, float g) {

    float gg = g * g;

    return (1.0 - gg) / pow(max(1.0 + gg - 2.0 * g * mu, 0.0001), 1.5);

}

vec4 cloud_at(vec3 p) {

    float height = dot(p, ground_plane.xyz) + ground_plane.w;
    vec3 extent = max(field_max - field_min, vec3(0.01));
    vec3 uv = (p - field_min) / extent;
    vec4 field = vec4(0.0);
    if (all(greaterThan(uv, vec3(0.0))) && all(lessThan(uv, vec3(1.0)))) {

        field = textureLod(cloud_field, uv, 0.0);

    }
    vec3 offset = p - source_point;
    vec3 lateral = offset - ground_plane.xyz * dot(offset, ground_plane.xyz);
    float r = max(source_radius, 0.1);
    float source = 0.0;
    float radial = dot(lateral, lateral) / (r * r);
    if (source_power > 0.0001 && radial < 4.0 && height < r * 1.5) {

        source = exp(-2.4 * radial) * exp(-max(height, 0.0) / (r * 0.22));
        source *= source_power * 0.6 / r * (1.0 - smoothstep(2.5, 4.0, radial));

    }
    field += vec4(source, source * source_wet, source, source * 0.87);
    return field;

}

void fragment() {

    if (!cloud_enabled) { discard; }
    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);
    float height = dot(volume_camera, ground_plane.xyz) + ground_plane.w;
    float slope = dot(ray, ground_plane.xyz);
    if (abs(slope) > 0.00001) {

        float ground = -height / slope;
        if (slope < 0.0) { interval.y = min(interval.y, ground); }
        else { interval.x = max(interval.x, ground); }

    } else if (height < 0.0) { discard; }
    if (interval.y <= interval.x) { discard; }

    float span = interval.y - interval.x;
    float voxel = max(length(field_max - field_min) / 40.0, 0.25);
    int steps = int(clamp(span / voxel, 12.0, 32.0));
    float stride = span / float(steps);
    float jitter = fract(grain(FRAGCOORD.xy) + sample_phase);
    float mu = dot(ray, sun_direction);
    float phase = mix(henyey_greenstein(mu, 0.62), henyey_greenstein(mu, -0.18), 0.3);
    float transmission = 1.0;
    vec3 light = vec3(0.0);
    for (int i = 0; i < 32; i++) {

        if (i >= steps) { break; }
        vec3 p = volume_camera + ray * (interval.x + (float(i) + jitter) * stride);
        vec4 field = cloud_at(p);
        if (field.x < 0.0001) { continue; }
        float density = field.x;
        float wet = clamp(field.y / field.x, 0.0, 1.0);
        float energy = (phase * field.z + field.w) / field.x;
        float h = max(dot(p, ground_plane.xyz) + ground_plane.w, 0.0);
        vec3 ambient = mix(bounce_light, sky_light, clamp(h / column_height, 0.0, 1.0)) * 0.8;
        vec3 albedo = mix(dust_albedo, steam_albedo, wet);
        vec3 scattering = albedo * (sun_light * daylight * energy * 1.25 + ambient);
        float absorb = exp(-density * stride);
        light += transmission * scattering * (1.0 - absorb);
        transmission *= absorb;
        if (transmission < 0.025) { break; }

    }
    ALPHA = 1.0 - transmission;
    ALBEDO = light;

}
