shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_draw_never, depth_test_disabled, cull_front;

uniform sampler2D depth_buffer : hint_depth_texture, filter_nearest;
uniform vec3 planet_centre = vec3(0.0);
uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0);
uniform float planet_radius = 1274200.0;
uniform float extinction = 0.0017;
uniform float sun_power = 1.5;
uniform float ambient_gain = 0.18;
uniform float frame_jitter = 0.0;

vec2 sphere_span(vec3 origin, vec3 ray, float radius) {

    float b = dot(origin, ray);
    float h = b * b - dot(origin, origin) + radius * radius;
    if (h < 0.0) { return vec2(1.0, -1.0); }
    return vec2(-b - sqrt(h), -b + sqrt(h));

}

#include "res://Shaders/CloudField.gdshaderinc"

float phase(float mu, float g) {

    return (1.0 - g * g) / pow(max(1.0 + g * g - 2.0 * g * mu, 0.01), 1.5);

}

vec3 illumination(vec3 p, vec3 ray, float density, float footprint) {

    vec3 up = normalize(p);
    float solar = dot(up, sun_direction);
    float day = smoothstep(-0.09, 0.08, solar);
    float optical = 0.0;
    float stride = 250.0;
    float distance = 0.0;
    for (int i = 0; i < 3; i++) {

        optical += density_at(p + sun_direction * (distance + stride * 0.5), max(footprint, stride)) * stride;
        distance += stride;
        stride *= 2.8;

    }
    optical *= extinction;
    float mu = dot(ray, sun_direction);
    float scattering = 0.72 * phase(mu, 0.55) + 0.28 * phase(mu, -0.2);
    float transmission = exp(-optical);
    float multiple = 0.32 * exp(-optical * 0.23);
    vec3 sunlight = mix(vec3(1.0, 0.38, 0.13), vec3(1.0, 0.96, 0.9), smoothstep(-0.04, 0.12, solar));
    float height = clamp((length(p) - base_radius) / (top_radius - base_radius), 0.0, 1.0);
    vec3 ambient = vec3(0.43, 0.58, 0.83) * ambient_gain * mix(0.4, 1.0, height) * exp(-density * 0.5);
    return (sunlight * sun_power * (transmission * scattering * 0.24 + multiple) + ambient) * day
        + vec3(0.0007, 0.001, 0.0018);

}

void fragment() {

    vec3 camera = INV_VIEW_MATRIX[3].xyz;
    vec3 ray = normalize((INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz - camera);
    vec3 origin = camera - planet_centre;
    vec2 outer = sphere_span(origin, ray, top_radius);
    float start = max(outer.x, 0.0);
    float finish = outer.y;
    vec2 inner = sphere_span(origin, ray, base_radius);
    if (length(origin) < base_radius) {

        start = max(start, inner.y);

    } else if (inner.x > 0.0 && inner.y > inner.x) {

        finish = min(finish, inner.x);

    }
    vec2 ground = sphere_span(origin, ray, planet_radius);
    if (ground.x > 0.0 && ground.y > ground.x) { finish = min(finish, ground.x); }
    float depth = texture(depth_buffer, SCREEN_UV).r;
    if (depth > 0.0) {

        vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
        finish = min(finish, length(view.xyz / view.w));

    }
    if (finish <= start || outer.y <= outer.x) { discard; }

    // Stratification decorrelates grazing rays; temporal AA integrates the sub-step samples.
    float jitter = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))) + frame_jitter);
    float step_length = (finish - start) / 48.0;
    float transmission = 1.0;
    vec3 radiance = vec3(0.0);
    float weighted_distance = 0.0;
    for (int i = 0; i < 48; i++) {

        float distance = start + (float(i) + jitter) * step_length;
        vec3 p = origin + ray * distance;
        float density = density_at(p, step_length);
        if (density > 0.001) {

            float opacity = 1.0 - exp(-density * extinction * step_length);
            float weight = transmission * opacity;
            radiance += illumination(p, ray, density, step_length) * weight;
            weighted_distance += distance * weight;
            transmission *= 1.0 - opacity;
            if (transmission < 0.015) { break; }

        }

    }
    float alpha = 1.0 - transmission;
    if (alpha < 0.001) { discard; }

    // Clouds composite after the air pass, with air only between the eye and their optical centroid.
    float distance = weighted_distance / alpha;
    float air_depth = 0.0;
    for (int i = 0; i < 4; i++) {

        float height = max(length(origin + ray * distance * (float(i) + 0.5) / 4.0) - planet_radius, 0.0);
        air_depth += exp(-height / 5600.0) * distance * 0.25;

    }
    vec3 air_transmission = exp(-vec3(5.8e-6, 1.35e-5, 3.31e-5) * air_depth);
    float day = smoothstep(-0.12, 0.08, dot(normalize(origin + ray * distance), sun_direction));
    vec3 haze = vec3(0.12, 0.24, 0.43) * day;
    ALBEDO = radiance * air_transmission + haze * (1.0 - air_transmission) * alpha;
    ALPHA = alpha;

}
