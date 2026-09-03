shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, depth_test_disabled, cull_front;

// Single-scatter Rayleigh/Mie; integral bounds from ray-sphere hits and the depth buffer, not the mesh.

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

uniform sampler2D depth_buffer : hint_depth_texture, filter_nearest;

const int view_samples = 20;
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

    // Anything already drawn - the vessel, most obviously - caps the integral where it sits.
    float depth = texture(depth_buffer, SCREEN_UV).x;

    if (depth > 0.0) {

        vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);

        far = min(far, length(view.xyz / view.w));

    }

    if (far <= near) {

        discard;

    }

    float step_length = (far - near) / float(view_samples);

    vec3 rayleigh_total = vec3(0.0);
    vec3 mie_total = vec3(0.0);

    vec2 view_optical_depth = vec2(0.0);

    for (int index = 0; index < view_samples; index++) {

        vec3 position = origin + ray * (near + step_length * (float(index) + 0.5));

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

    float rayleigh_phase = 3.0 / (16.0 * PI) * (1.0 + cosine * cosine);

    float g = mie_direction;
    float mie_phase = 3.0 / (8.0 * PI) * ((1.0 - g * g) * (1.0 + cosine * cosine)) / ((2.0 + g * g) * pow(1.0 + g * g - 2.0 * g * cosine, 1.5));

    ALBEDO = sun_intensity * (rayleigh_total * rayleigh_coefficients * rayleigh_phase + mie_total * mie_coefficient * mie_phase);

}
