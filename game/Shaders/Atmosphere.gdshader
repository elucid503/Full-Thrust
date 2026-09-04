shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_front;

// Single-scatter Rayleigh/Mie; integral bounds from ray-sphere hits and the depth buffer, not the
// mesh. The shell composites rather than adds: what is behind it is dimmed by the air in front of
// it before the scattered light is laid over the top, which is the only way a distant ridge fades
// into the haze and the only way a star field goes out at sunrise.

uniform sampler2D screen_texture : hint_screen_texture, filter_linear;
uniform sampler2D depth_buffer : hint_depth_texture, filter_nearest;

uniform vec3 planet_centre = vec3(0.0, 0.0, 0.0);
uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0);

uniform float planet_radius = 1274200.0;
uniform float atmosphere_radius = 1334200.0;

uniform float rayleigh_height = 5600.0;
uniform float mie_height = 1200.0;

uniform vec3 rayleigh_coefficients = vec3(9.3e-6, 2.17e-5, 5.30e-5);
uniform float mie_coefficient = 2.1e-5;
uniform float mie_direction : hint_range(0.0, 0.99) = 0.76;

uniform float sun_intensity = 8.0;

// Single scattering alone leaves a daytime sky too dark and too directional: most of what reaches
// the eye from overhead has bounced more than once. An isotropic term on the Rayleigh phase is the
// cheapest honest shape for that - it grows with the same optical depth and has no direction.
uniform float multiscatter : hint_range(0.0, 0.2) = 0.035;

const int view_samples = 24;
const int light_samples = 6;

// Returns the near and far ray parameters of a sphere hit, or a reversed pair when the ray misses.
vec2 sphere_span(vec3 origin, vec3 direction, float radius) {

    float half_b = dot(origin, direction);
    float c = dot(origin, origin) - radius * radius;

    float discriminant = half_b * half_b - c;

    if (discriminant < 0.0) {

        return vec2(1.0, -1.0);

    }

    float root = sqrt(discriminant);

    return vec2(-half_b - root, -half_b + root);

}

vec2 densities(vec3 position) {

    float height = length(position) - planet_radius;

    return vec2(exp(-height / rayleigh_height), exp(-height / mie_height));

}

void fragment() {

    vec3 camera = INV_VIEW_MATRIX[3].xyz;
    vec3 ray = normalize((INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz - camera);

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

    vec2 view_optical_depth = vec2(0.0);

    float span = far - near;

    for (int index = 0; index < view_samples; index++) {

        float low = pow(float(index) / float(view_samples), bunching);
        float high = pow(float(index + 1) / float(view_samples), bunching);

        float step_length = span * (high - low);

        vec3 position = origin + ray * (near + span * (low + high) * 0.5);

        vec2 density = densities(position) * step_length;

        view_optical_depth += density;

        vec2 sunward = sphere_span(position, sun_direction, atmosphere_radius);

        // A sample the planet already occludes contributes nothing, so skip the light march for it.
        vec2 blocked = sphere_span(position, sun_direction, planet_radius);

        if (blocked.y > blocked.x && blocked.y > 0.0) {

            continue;

        }

        float light_step = sunward.y / float(light_samples);

        vec2 light_optical_depth = vec2(0.0);

        for (int sample = 0; sample < light_samples; sample++) {

            light_optical_depth += densities(position + sun_direction * (light_step * (float(sample) + 0.5))) * light_step;

        }

        vec3 attenuation = exp(-(rayleigh_coefficients * (view_optical_depth.x + light_optical_depth.x) + mie_coefficient * 1.1 * (view_optical_depth.y + light_optical_depth.y)));

        rayleigh_total += density.x * attenuation;
        mie_total += density.y * attenuation;

    }

    float cosine = dot(ray, sun_direction);

    float rayleigh_phase = 3.0 / (16.0 * PI) * (1.0 + cosine * cosine) + multiscatter;

    float g = mie_direction;
    float mie_phase = 3.0 / (8.0 * PI) * ((1.0 - g * g) * (1.0 + cosine * cosine)) / ((2.0 + g * g) * pow(1.0 + g * g - 2.0 * g * cosine, 1.5));

    vec3 inscatter = sun_intensity * (rayleigh_total * rayleigh_coefficients * rayleigh_phase + mie_total * mie_coefficient * mie_phase);

    vec3 transmittance = exp(-(rayleigh_coefficients * view_optical_depth.x + mie_coefficient * 1.1 * view_optical_depth.y));

    ALBEDO = texture(screen_texture, SCREEN_UV).rgb * transmittance + inscatter;
    ALPHA = 1.0;

}
