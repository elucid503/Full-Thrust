shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled;

// Single-scatter Rayleigh/Mie; integral bounds from ray-sphere hits and the depth buffer, not the
// mesh. The shell composites rather than adds: what is behind it is dimmed by the air in front of
// it before the scattered light is laid over the top, which is the only way a distant ridge fades
// into the haze and the only way a star field goes out at sunrise.

uniform sampler2D screen_texture : hint_screen_texture, filter_linear;
uniform sampler2D depth_buffer : hint_depth_texture, filter_nearest;

uniform vec3 planet_centre = vec3(0.0, 0.0, 0.0);
uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0);

uniform float planet_radius = 1274200.0;

uniform float mie_direction : hint_range(0.0, 0.99) = 0.76;

uniform float ozone_height = 22000.0;

uniform float sun_intensity = 3.4;
uniform float sun_shafts = 1.0;

// The weather deck's floor and roof above the datum; cloud shadow only holds for air below it.
uniform float cloud_floor = 900.0;
uniform float cloud_ceiling = 3800.0;

#include "res://Shaders/Atmosphere/Sunlight.gdshaderinc"
#include "res://Shaders/Clouds/CloudShadowSampling.gdshaderinc"
#include "res://Shaders/Atmosphere/AtmosphereComposite.gdshaderinc"

// Single scattering alone leaves a daytime sky too dark and too directional: most of what reaches
// the eye from overhead has bounced more than once. An isotropic term on the Rayleigh phase is the
// cheapest honest shape for that - it grows with the same optical depth and has no direction.
uniform float multiscatter : hint_range(0.0, 0.2) = 0.035;

const int view_samples = 20;

// View samples sit kilometres apart; a crepuscular ray is the width of a gap between two clouds.
const int shaft_samples = 24;

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

// Rayleigh and Mie weights of the sunlit air the deck shades, extinguished both ways; TAA resolves the jitter.
void cloud_shaded_air(vec3 origin, vec3 ray, float near, float far, vec2 pixel, float time, out vec3 rayleigh, out vec3 mie) {

    rayleigh = vec3(0.0);
    mie = vec3(0.0);

    if (sun_shafts < 0.001 || shadow_ready < 0.001) { return; }

    vec3 up = normalize(shadow_centre - planet_centre);
    float solar = dot(up, sun_direction);
    float daylight = smoothstep(0.03, 0.12, solar);

    if (daylight < 0.001) { return; }

    vec2 roof = sphere_span(origin, ray, planet_radius + cloud_ceiling);
    float start = max(near, roof.x);
    // The cached map covers a little under half its span either side of centre.
    float finish = min(min(far, roof.y), start + shadow_span);

    if (roof.y <= roof.x || finish <= start) { return; }

    vec3 projection = sun_direction / max(solar, 0.03);
    vec3 projected_ray = ray - projection * dot(ray, up);

    float noise = fract(52.9829189 * fract(dot(pixel, vec2(0.06711056, 0.00583715))));
    float jitter = fract(noise + fract(time * 60.0) * 0.618034);

    // Air above the roof, when the eye is up there, taken at its midpoint density.
    vec3 lead = densities(origin + ray * start * 0.5) * start;
    vec3 view_depth = rayleigh_coefficients * lead.x + vec3(mie_coefficient * 1.1 * lead.y);

    float span = finish - start;
    float previous = 0.0;

    for (int index = 0; index < shaft_samples; index++) {

        // Bunched towards the eye, where a shaft covers the most screen.
        float fraction = pow((float(index) + jitter) / float(shaft_samples), 1.6);
        float next = pow((float(index) + 1.0) / float(shaft_samples), 1.6);
        float step_length = span * (next - previous);
        previous = next;

        vec3 position = origin + ray * (start + span * fraction);
        float height = length(position) - planet_radius;
        vec3 density = densities(position) * step_length;

        vec3 offset = position + planet_centre - shadow_centre;
        offset -= projection * dot(offset, up);
        vec2 uv = vec2(dot(offset, shadow_east), dot(offset, shadow_north)) / shadow_span + 0.5;
        float coverage = 1.0 - smoothstep(0.38, 0.49, max(abs(uv.x - 0.5), abs(uv.y - 0.5)));
        float footprint = length(projected_ray) * step_length;
        float shade = (1.0 - filtered_cloud_transmission(uv, footprint)) * coverage
            * (1.0 - smoothstep(cloud_floor, cloud_ceiling, height));

        vec3 carried = exp(-view_depth) * sun_transmittance(position) * shade;
        rayleigh += carried * density.x;
        mie += carried * density.y;

        view_depth += rayleigh_coefficients * density.x + vec3(mie_coefficient * 1.1 * density.y);

    }

    rayleigh *= daylight * shadow_ready * sun_shafts;
    mie *= daylight * shadow_ready * sun_shafts;

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
        vec3 light_optical_depth = sun_optical_depth_at(radial_distance, dot(position, sun_direction) / radial_distance);

        vec3 attenuation = exp(-(rayleigh_coefficients * (view_optical_depth.x - density.x * 0.5 + light_optical_depth.x)
            + vec3(mie_coefficient * 1.1 * (view_optical_depth.y - density.y * 0.5 + light_optical_depth.y))
            + ozone_coefficients * (view_optical_depth.z - density.z * 0.5 + light_optical_depth.z)));

        rayleigh_total += density.x * attenuation;
        mie_total += density.y * attenuation;

    }

    float cosine = dot(ray, sun_direction);

    float rayleigh_phase = 3.0 / (16.0 * PI) * (1.0 + cosine * cosine) + multiscatter;

    float g = mie_direction;
    float mie_phase = 3.0 / (8.0 * PI) * ((1.0 - g * g) * (1.0 + cosine * cosine)) / ((2.0 + g * g) * pow(1.0 + g * g - 2.0 * g * cosine, 1.5));

    vec3 inscatter = sun_intensity * (rayleigh_total * rayleigh_coefficients * rayleigh_phase + mie_total * mie_coefficient * mie_phase);

    // The isotropic multiple-scatter share comes from the whole sky, so cloud shade leaves it.
    vec3 shaded_rayleigh;
    vec3 shaded_mie;
    cloud_shaded_air(origin, ray, near, far, FRAGCOORD.xy, TIME, shaded_rayleigh, shaded_mie);
    inscatter = max(inscatter - sun_intensity * (shaded_rayleigh * rayleigh_coefficients * (rayleigh_phase - multiscatter)
        + shaded_mie * mie_coefficient * mie_phase), vec3(0.0));

    vec3 transmittance = exp(-(rayleigh_coefficients * view_optical_depth.x
        + vec3(mie_coefficient * 1.1 * view_optical_depth.y)
        + ozone_coefficients * view_optical_depth.z));

    // Preserve RGB extinction without declaring nearby opaque geometry fully reactive to FSR.
    vec4 air = atmosphere_composite(texture(screen_texture, SCREEN_UV).rgb, transmittance, inscatter);
    ALBEDO = air.rgb;
    ALPHA = air.a;

}
