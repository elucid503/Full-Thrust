#ifndef CLOUD_COMPUTE
uniform float fog_enabled = 1.0;
uniform float sun_shafts = 1.0;
#endif

const float marine_top = 1200.0;
const float haze_top = 3000.0;

vec4 coastal_fog(vec3 origin, vec3 ray, float scene_distance, float cloud_distance, out vec4 foreground) {

    foreground = vec4(0.0);
    float visibility = fog_enabled * (1.0 - smoothstep(10000.0, 20000.0, eye_height));
    if (visibility < 0.001) { return vec4(0.0); }
    vec2 shell = eye_sphere_span(ray, planet_radius + haze_top);
    float start = max(shell.x, 0.0);
    float finish = min(shell.y, scene_distance);
    vec2 ground = eye_sphere_span(ray, planet_radius);
    if (ground.x > 0.0 && ground.y > ground.x) { finish = min(finish, ground.x); }
    float horizon = sqrt(max(eye_height * (2.0 * planet_radius + eye_height), 0.0));
    finish = min(finish, start + max(horizon * 1.8, 14000.0));
    if (finish <= start || shell.y <= shell.x) { return vec4(0.0); }
    float mu = dot(ray, sun_direction);
    float phase = (1.0 - 0.68 * 0.68) / (4.0 * PI * pow(1.0 + 0.68 * 0.68 - 1.36 * mu, 1.5));
    float transmission = 1.0;
    vec3 radiance = vec3(0.0);
    float front_transmission = 1.0;
    vec3 front_radiance = vec3(0.0);
    vec3 map_up = normalize(shadow_centre - planet_centre);
    float map_solar = dot(map_up, sun_direction);
    float near_height = max(ray_height(ray * start), 0.0);
    float far_height = max(ray_height(ray * finish), 0.0);
    bool from_far = far_height + 80.0 < near_height;
    for (int i = 0; i < 48; i++) {

        float a = float(i * i) * 32.0;
        float b = float((i + 1) * (i + 1)) * 32.0;
        float low;
        float high;
        if (from_far) {

            high = finish - a;
            low = finish - b;
            if (high <= start) { break; }
            low = max(low, start);

        }
        else {

            low = start + a;
            high = start + b;
            if (low >= finish) { break; }
            high = min(high, finish);

        }
        float stride = high - low;
        if (stride <= 0.0) { continue; }
        vec3 p = origin + ray * (low + high) * 0.5;
        float solar = dot(normalize(p), sun_direction);
        vec3 sunlight = sunlight_colour(p);
        float height = max(ray_height(ray * (low + high) * 0.5), 0.0);
        float marine = exp(-height / 650.0) * (1.0 - smoothstep(500.0, marine_top, height));
        float aerial = 0.40 * exp(-height / 1800.0) * smoothstep(500.0, 1100.0, height)
            * (1.0 - smoothstep(2000.0, haze_top, height));
        float density = marine + aerial;
        float opacity = 1.0 - exp(-density * stride * 0.00024);
        float light = 1.0;
        if (sun_shafts > 0.001 && shadow_ready > 0.001 && map_solar > 0.03) {

            vec3 offset = p + planet_centre - shadow_centre;
            offset -= sun_direction * dot(offset, map_up) / max(map_solar, 0.03);
            vec2 uv = vec2(dot(offset, shadow_east), dot(offset, shadow_north)) / shadow_span + 0.5;
            float coverage = 1.0 - smoothstep(0.38, 0.49, max(abs(uv.x - 0.5), abs(uv.y - 0.5)));
            vec3 levels = textureLod(local_cloud_shadow, uv, 0.0).rgb;
            float footprint = length(ray - sun_direction * dot(ray, map_up) / max(map_solar, 0.03)) * stride;
            float filtered = mix(levels.r, levels.g, smoothstep(64.0, 512.0, footprint));
            float shaft = mix(filtered, levels.r, 0.55);
            light = mix(1.0, shaft, coverage * smoothstep(0.03, 0.12, map_solar) * shadow_ready * sun_shafts);

        }
        vec3 scattered = vec3(0.07, 0.10, 0.15) * skylight(solar) + sunlight * phase * 3.4 * light;
        // Split extinction within the same bin instead of marching foreground fog a second time.
        if (cloud_distance < finish && cloud_distance > low) {

            float front_opacity = 1.0 - exp(-density * min(cloud_distance - low, stride) * 0.00024);
            front_radiance = from_far
                ? scattered * front_opacity + front_radiance * (1.0 - front_opacity)
                : front_radiance + scattered * front_transmission * front_opacity;
            front_transmission *= 1.0 - front_opacity;

        }
        if (from_far) {

            radiance = scattered * opacity + radiance * (1.0 - opacity);

        }
        else {

            radiance += scattered * transmission * opacity;

        }
        transmission *= 1.0 - opacity;
        if (!from_far && transmission < 0.003) { break; }

    }
    vec4 full = vec4(radiance, 1.0 - transmission);
    foreground = cloud_distance >= finish ? full : vec4(front_radiance, 1.0 - front_transmission);
    foreground *= visibility;
    return full * visibility;

}
