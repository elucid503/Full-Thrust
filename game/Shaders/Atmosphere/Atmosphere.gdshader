shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled;

// Single-scatter Rayleigh/Mie; integral bounds from ray-sphere hits and the depth buffer, not the
// mesh. The shell composites rather than adds: what is behind it is dimmed by the air in front of
// it before the scattered light is laid over the top, which is the only way a distant ridge fades
// into the haze and the only way a star field goes out at sunrise.

uniform sampler2D screen_texture : hint_screen_texture, filter_linear;
uniform sampler2D depth_buffer : hint_depth_texture, filter_nearest;
uniform sampler2D sun_optical_depth : filter_linear, repeat_disable;

uniform vec3 planet_centre = vec3(0.0, 0.0, 0.0);
uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0);

uniform float planet_radius = 1274200.0;
uniform float atmosphere_radius = 1334200.0;

uniform float rayleigh_height = 5600.0;
uniform float mie_height = 1200.0;

uniform vec3 rayleigh_coefficients = vec3(5.8e-6, 1.35e-5, 3.31e-5);
uniform float mie_coefficient = 7.0e-6;
uniform float mie_direction : hint_range(0.0, 0.99) = 0.76;

uniform vec3 ozone_coefficients = vec3(0.650e-6, 1.881e-6, 0.085e-6);
uniform float ozone_height = 22000.0;
uniform float ozone_half_width = 15000.0;

uniform float sun_intensity = 3.4;
uniform float sun_shafts = 1.0;

#include "res://Shaders/Clouds/CloudShadowSampling.gdshaderinc"
#include "res://Shaders/Atmosphere/AtmosphereComposite.gdshaderinc"

float cloud_sunlight(vec3 position, vec3 ray, float stride) {

    if (sun_shafts < 0.001 || shadow_ready < 0.001) { return 1.0; }
    vec3 up = normalize(shadow_centre - planet_centre);
    float solar = dot(up, sun_direction);
    float height = max(length(position) - planet_radius, 0.0);
    float influence = (1.0 - smoothstep(900.0, 3200.0, height)) * smoothstep(0.03, 0.12, solar);
    if (influence < 0.001) { return 1.0; }
    vec3 offset = position + planet_centre - shadow_centre;
    offset -= sun_direction * dot(offset, up) / max(solar, 0.03);
    vec2 uv = vec2(dot(offset, shadow_east), dot(offset, shadow_north)) / shadow_span + 0.5;
    float coverage = 1.0 - smoothstep(0.38, 0.49, max(abs(uv.x - 0.5), abs(uv.y - 0.5)));
    float footprint = length(ray - sun_direction * dot(ray, up) / max(solar, 0.03)) * stride;
    float transmission = filtered_cloud_transmission(uv, footprint);
    return 1.0 - (1.0 - transmission) * influence * coverage * sun_shafts * shadow_ready;

}

// Single scattering alone leaves a daytime sky too dark and too directional: most of what reaches
// the eye from overhead has bounced more than once. An isotropic term on the Rayleigh phase is the
// cheapest honest shape for that - it grows with the same optical depth and has no direction.
uniform float multiscatter : hint_range(0.0, 0.2) = 0.035;

const int view_samples = 20;

// Returns the near and far ray parameters of a sphere hit, or a reversed pair when the ray misses.
vec2 sphere_span(vec3 origin, vec3 direction, float radius) {

    float half_b = dot(origin, direction);
    float radial_distance = length(origin);
    float c = (radial_distance - radius) * (radial_distance + radius);

    float discriminant = half_b * half_b - c;

    if (discriminant < 0.0) {

        return vec2(1.0, -1.0);

    }

    float root = sqrt(discriminant);

    return vec2(-half_b - root, -half_b + root);

}

vec3 densities(vec3 position) {

    float height = max(length(position) - planet_radius, 0.0);

    float ozone = max(1.0 - abs(height - ozone_height) / ozone_half_width, 0.0);

    return vec3(exp(-height / rayleigh_height), exp(-height / mie_height), ozone);

}

void vertex() {

    POSITION = vec4(VERTEX.xy, 1.0, 1.0);

}

void fragment() {

    vec3 camera = INV_VIEW_MATRIX[3].xyz;
    vec4 view_ray = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, 1.0, 1.0);
    vec3 ray = normalize(mat3(INV_VIEW_MATRIX) * view_ray.xyz);

    vec3 origin = camera - planet_centre;

    vec2 shell = sphere_span(origin, ray, atmosphere_radius);

    if (shell.y <= shell.x || shell.y <= 0.0) {

        discard;

    }

    float near = max(shell.x, 0.0);
    float far = shell.y;

    vec2 ground = sphere_span(origin, ray, planet_radius);

    if (ground.y > ground.x && ground.x > 0.0) {

        far = min(far, ground.x);

    }

    // Anything already drawn - the ground, the vehicle - caps the integral where it sits.
    float depth = texture(depth_buffer, SCREEN_UV).x;

    if (depth > 0.0) {

        vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);

        far = min(far, length(view.xyz / view.w));

    }

    if (far <= near) {

        discard;

    }

    // From orbit the air on the ray is a band in the middle of it and even spacing finds it. From
    // inside, almost all of it is in the first few kilometres, and even spacing walks straight past
    // the part that matters. The samples are bunched towards the eye as the eye descends.
    float altitude = length(origin) - planet_radius;
    float bunching = mix(3.0, 1.0, clamp(altitude / (atmosphere_radius - planet_radius), 0.0, 1.0));

    vec3 rayleigh_total = vec3(0.0);
    vec3 mie_total = vec3(0.0);

    vec3 view_optical_depth = vec3(0.0);

    float span = far - near;

    for (int index = 0; index < view_samples; index++) {

        float low = pow(float(index) / float(view_samples), bunching);
        float high = pow(float(index + 1) / float(view_samples), bunching);

        float step_length = span * (high - low);

        vec3 position = origin + ray * (near + span * (low + high) * 0.5);

        vec3 density = densities(position) * step_length;

        view_optical_depth += density;

        // A sample the planet already occludes contributes nothing, so skip the light march for it.
        vec2 blocked = sphere_span(position, sun_direction, planet_radius);

        if (blocked.y > blocked.x && blocked.y > 0.0) {

            continue;

        }

        float radial_distance = length(position);
        float solar_cosine = dot(position, sun_direction) / radial_distance;
        vec2 lookup_uv = vec2(0.5 + 0.5 * sign(solar_cosine) * sqrt(abs(solar_cosine)),
            sqrt(clamp((radial_distance - planet_radius) / (atmosphere_radius - planet_radius), 0.0, 1.0)));
        lookup_uv = (lookup_uv * vec2(255.0, 127.0) + 0.5) / vec2(256.0, 128.0);
        vec3 light_optical_depth = textureLod(sun_optical_depth, lookup_uv, 0.0).rgb
            * vec3(rayleigh_height, mie_height, ozone_half_width);

        vec3 attenuation = exp(-(rayleigh_coefficients * (view_optical_depth.x - density.x * 0.5 + light_optical_depth.x)
            + vec3(mie_coefficient * 1.1 * (view_optical_depth.y - density.y * 0.5 + light_optical_depth.y))
            + ozone_coefficients * (view_optical_depth.z - density.z * 0.5 + light_optical_depth.z)));

        float sunlight = cloud_sunlight(position, ray, step_length);
        rayleigh_total += density.x * attenuation * mix(1.0, sunlight, 0.18);
        mie_total += density.y * attenuation * sunlight;

    }

    float cosine = dot(ray, sun_direction);

    float rayleigh_phase = 3.0 / (16.0 * PI) * (1.0 + cosine * cosine) + multiscatter;

    float g = mie_direction;
    float mie_phase = 3.0 / (8.0 * PI) * ((1.0 - g * g) * (1.0 + cosine * cosine)) / ((2.0 + g * g) * pow(1.0 + g * g - 2.0 * g * cosine, 1.5));

    vec3 inscatter = sun_intensity * (rayleigh_total * rayleigh_coefficients * rayleigh_phase + mie_total * mie_coefficient * mie_phase);

    vec3 transmittance = exp(-(rayleigh_coefficients * view_optical_depth.x
        + vec3(mie_coefficient * 1.1 * view_optical_depth.y)
        + ozone_coefficients * view_optical_depth.z));

    // Preserve RGB extinction without declaring nearby opaque geometry fully reactive to FSR.
    vec4 air = atmosphere_composite(texture(screen_texture, SCREEN_UV).rgb, transmittance, inscatter);
    ALBEDO = air.rgb;
    ALPHA = air.a;

}
