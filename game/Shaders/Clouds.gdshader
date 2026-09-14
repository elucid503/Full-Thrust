shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled;

uniform sampler2D depth_buffer : hint_depth_texture, filter_nearest;
uniform vec3 planet_centre = vec3(0.0);
uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0);
uniform float planet_radius = 1274200.0;
uniform float extinction = 0.009;
uniform float sun_power = 2.0;
uniform float ambient_gain = 0.28;
uniform float view_steps = 128.0;
uniform sampler2D local_cloud_lighting : filter_linear, repeat_disable;

#include "res://Shaders/CloudShadowSampling.gdshaderinc"

#include "res://Shaders/CloudRayGeometry.gdshaderinc"

vec2 sphere_span(vec3 origin, vec3 ray, float radius) {

    float b = dot(origin, ray);
    float radial_distance = length(origin);
    float h = b * b + (radius - radial_distance) * (radius + radial_distance);
    if (h < 0.0) { return vec2(1.0, -1.0); }
    return vec2(-b - sqrt(h), -b + sqrt(h));

}

#include "res://Shaders/CloudField.gdshaderinc"

float phase(float mu, float g) {

    return (1.0 - g * g) / pow(max(1.0 + g * g - 2.0 * g * mu, 0.01), 1.5);

}

float cloud_sun_optical(vec3 p, float footprint, vec2 conditions) {

    // Orbital views cannot use the local cache; skip its projection and texture read.
    if (shadow_ready < 0.001) {

        return density_in_weather(p + sun_direction * 325.0, max(footprint, 550.0), conditions) * 550.0 * extinction;

    }

    vec3 up = normalize(shadow_centre - planet_centre);
    float solar = dot(up, sun_direction);
    vec3 offset = p + planet_centre - shadow_centre;
    offset -= sun_direction * dot(offset, up) / max(solar, 0.03);
    vec2 uv = vec2(dot(offset, shadow_east), dot(offset, shadow_north)) / 64000.0 + 0.5;
    float coverage = (1.0 - smoothstep(0.38, 0.49, max(abs(uv.x - 0.5), abs(uv.y - 0.5))))
        * shadow_ready * smoothstep(0.03, 0.12, solar);
    float height = clamp((length(p) - base_radius) / (top_radius - base_radius), 0.0, 1.0) * 3.0;
    vec3 columns = textureLod(local_cloud_lighting, uv, 0.0).rgb * 16.0;
    float cached = height < 1.0 ? mix(columns.r, columns.g, height)
        : (height < 2.0 ? mix(columns.g, columns.b, height - 1.0) : columns.b * (3.0 - height));
    if (coverage > 0.999) { return cached; }
    float fallback = density_in_weather(p + sun_direction * 325.0, max(footprint, 550.0), conditions) * 550.0 * extinction;
    return mix(fallback, cached, coverage);

}

vec3 illumination(vec3 p, float scattering, float density, float sun_optical) {

    vec3 up = normalize(p);
    float solar = dot(up, sun_direction);
    float day = smoothstep(-0.09, 0.08, solar);
    float optical = density * 50.0 * extinction + sun_optical;
    float transmission = exp(-optical);
    float multiple = 0.32 * exp(-optical * 0.35) + 0.08 * exp(-optical * 0.08);
    vec3 sunlight = mix(vec3(1.0, 0.38, 0.13), vec3(1.0, 0.96, 0.9), smoothstep(-0.04, 0.12, solar));
    float height = clamp((length(p) - base_radius) / (top_radius - base_radius), 0.0, 1.0);
    vec3 ambient = vec3(0.43, 0.58, 0.83) * ambient_gain * mix(0.4, 1.0, height) * exp(-density * 0.5);
    return (sunlight * sun_power * (transmission * scattering * 0.24 + multiple) + ambient) * day
        + vec3(0.0007, 0.001, 0.0018);

}

vec4 cloud_radiance(vec3 origin, vec3 ray, float scene_distance, float angular_footprint, float visibility) {
    if (visibility < 0.005) { return vec4(0.0); }
    float residual = min(0.01 / max(visibility, 0.01), 0.5);
    float inner_radius = base_radius + (top_radius - base_radius) * 0.035;
    float outer_radius = base_radius + (top_radius - base_radius) * 0.96;
    vec2 outer = eye_sphere_span(ray, outer_radius);
    float start = max(outer.x, 0.0);
    float finish = min(outer.y, scene_distance);
    vec2 inner = eye_sphere_span(ray, inner_radius);
    if (planet_radius + eye_height < inner_radius) {

        start = max(start, inner.y);

    }
    vec2 ground = eye_sphere_span(ray, planet_radius);
    if (ground.x > 0.0 && ground.y > ground.x) {

        finish = min(finish, ground.x);

    }
    else if (dot(eye_up, ray) < 0.0) {

        float horizon = sqrt(max(eye_height * (2.0 * planet_radius + eye_height), 0.0));
        finish = min(finish, max(horizon, start));

    }
    if (finish <= start || outer.y <= outer.x) { return vec4(0.0); }

    // Skip the clear cavity while retaining both sides of a grazing cloud-deck intersection.
    float gap_start = inner.y > inner.x ? clamp(inner.x, start, finish) : finish;
    float gap_end = inner.y > inner.x ? clamp(inner.y, start, finish) : finish;
    float gap = max(gap_end - gap_start, 0.0);
    float span = finish - start - gap;
    if (span <= 0.0) { return vec4(0.0); }
    float wake_start = finish;
    float wake_finish = start;
    for (int i = 0; i < wake_count; i++) {

        vec3 centre = wake_centres[i].xyz + wake_axes[i].xyz * wake_axes[i].w * 0.5;
        float bound = wake_axes[i].w * 0.5 + (wake_centres[i].w + wake_axes[i].w * wake_states[i].y) * 1.5;
        vec2 interval = sphere_span(origin - centre, ray, bound);
        if (interval.y > max(interval.x, 0.0)) {

            wake_start = min(wake_start, max(interval.x, start));
            wake_finish = max(wake_finish, min(interval.y, finish));

        }

    }
    float mu = dot(ray, sun_direction);
    float scattering = 0.72 * phase(mu, 0.55) + 0.28 * phase(mu, -0.2);
    float transmission = 1.0;
    vec3 radiance = vec3(0.0);
    float weighted_distance = 0.0;
    float cursor = start;
    vec2 previous_weather = weather_at(origin + ray * cursor);
    float previous_envelope = cloud_base_at_height(origin + ray * cursor, max(1.0, cursor * angular_footprint), previous_weather,
        (ray_height(ray * cursor) - (base_radius - planet_radius)) / (top_radius - base_radius)).x;
    float quality = 128.0 / max(view_steps, 64.0);
    for (int i = 0; i < 640; i++) {

        if (cursor >= finish) { break; }
        if (cursor >= gap_start && cursor < gap_end) {

            cursor = gap_end;
            previous_weather = weather_at(origin + ray * cursor);
            previous_envelope = cloud_base_at_height(origin + ray * cursor, max(1.0, cursor * angular_footprint), previous_weather,
                (ray_height(ray * cursor) - (base_radius - planet_radius)) / (top_radius - base_radius)).x;

        }
        float step_length = max(192.0 * quality, cursor * angular_footprint * 8.0);
        float boundary = cursor < gap_start ? gap_start : finish;
        step_length = min(step_length, boundary - cursor);
        if (step_length <= 0.0) { break; }
        float sample_footprint = max(1.0, (cursor + step_length) * angular_footprint);
        vec2 next_weather = weather_at(origin + ray * (cursor + step_length));
        float next_envelope = cloud_base_at_height(origin + ray * (cursor + step_length), sample_footprint, next_weather,
            (ray_height(ray * (cursor + step_length)) - (base_radius - planet_radius)) / (top_radius - base_radius)).x;
        float middle_envelope = cloud_base_at_height(origin + ray * (cursor + step_length * 0.5), sample_footprint,
            (previous_weather + next_weather) * 0.5,
            (ray_height(ray * (cursor + step_length * 0.5)) - (base_radius - planet_radius)) / (top_radius - base_radius)).x;
        if (max(max(previous_envelope, next_envelope), middle_envelope) > -0.75) {

            int samples = cursor + step_length > wake_start && cursor < wake_finish ? 16 : 8;
            float stride = step_length / float(samples);
            for (int j = 0; j < 16; j++) {

                if (j >= samples) { break; }
                float distance = cursor + stride * (float(j) + 0.5);
                vec3 p = origin + ray * distance;
                float fraction = (float(j) + 0.5) / float(samples);
                vec2 conditions = mix(previous_weather, next_weather, fraction);
                float height = (ray_height(ray * distance) - (base_radius - planet_radius)) / (top_radius - base_radius);
                float density = density_from_base(p, sample_footprint, cloud_base_at_height(p, sample_footprint, conditions, height));
                if (density < 0.001) { continue; }
                float opacity = 1.0 - exp(-density * extinction * stride);
                float weight = transmission * opacity;
                float edge_fill = mix(0.72, 1.08, smoothstep(0.04, 0.65, density));
                radiance += illumination(p, scattering, density, cloud_sun_optical(p, sample_footprint, conditions)) * weight * edge_fill;
                weighted_distance += distance * weight;
                transmission *= 1.0 - opacity;
                if (transmission < residual) { break; }

            }

        }
        if (transmission < residual) { break; }
        previous_envelope = next_envelope;
        previous_weather = next_weather;
        cursor += step_length;

    }
    float alpha = 1.0 - transmission;
    if (alpha < 0.001) { return vec4(0.0); }

    // Clouds composite after the air pass, with air only between the eye and their optical centroid.
    float distance = weighted_distance / alpha;
    float air_depth = 0.0;
    for (int i = 0; i < 4; i++) {

        float height = max(ray_height(ray * distance * (float(i) + 0.5) / 4.0), 0.0);
        air_depth += exp(-height / 5600.0) * distance * 0.25;

    }
    vec3 air_transmission = exp(-vec3(5.8e-6, 1.35e-5, 3.31e-5) * air_depth);
    float day = smoothstep(-0.12, 0.08, dot(normalize(origin + ray * distance), sun_direction));
    vec3 haze = vec3(0.12, 0.24, 0.43) * day;
    return vec4(radiance * air_transmission + haze * (1.0 - air_transmission) * alpha, alpha);

}

#include "res://Shaders/CoastalFog.gdshaderinc"

void vertex() {

    POSITION = vec4(VERTEX.xy, 1.0, 1.0);

}

void fragment() {

    vec3 camera = INV_VIEW_MATRIX[3].xyz;
    vec4 view_ray = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, 1.0, 1.0);
    vec3 ray = normalize(mat3(INV_VIEW_MATRIX) * view_ray.xyz);
    vec3 origin = camera - planet_centre;
    float scene_distance = 4000000.0;
    float depth = texture(depth_buffer, SCREEN_UV).r;
    if (depth > 0.0) {

        vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
        scene_distance = length(view.xyz / view.w);

    }
    float footprint = max(length(dFdx(ray)), length(dFdy(ray)));
    vec4 fog = coastal_fog(origin, ray, scene_distance);
    bool fog_in_front = eye_height < haze_top;
    vec4 cloud = cloud_radiance(origin, ray, scene_distance, footprint, fog_in_front ? 1.0 - fog.a : 1.0);
    vec4 combined = fog_in_front
        ? fog + cloud * (1.0 - fog.a) : cloud + fog * (1.0 - cloud.a);
    if (combined.a < 0.0001) { discard; }
    ALBEDO = combined.rgb;
    ALPHA = combined.a;

}
