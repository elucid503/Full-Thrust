uniform float fog_enabled = 1.0;
uniform float sun_shafts = 1.0;

vec4 coastal_fog(vec3 origin, vec3 ray, float scene_distance) {

    if (fog_enabled < 0.5) { return vec4(0.0); }
    vec2 shell = eye_sphere_span(ray, planet_radius + 900.0);
    float start = max(shell.x, 0.0);
    float finish = min(min(shell.y, start + 32768.0), scene_distance);
    vec2 ground = eye_sphere_span(ray, planet_radius);
    if (ground.x > 0.0 && ground.y > ground.x) { finish = min(finish, ground.x); }
    if (finish <= start || shell.y <= shell.x) { return vec4(0.0); }
    float mu = dot(ray, sun_direction);
    float phase = (1.0 - 0.68 * 0.68) / (4.0 * PI * pow(1.0 + 0.68 * 0.68 - 1.36 * mu, 1.5));
    float transmission = 1.0;
    vec3 radiance = vec3(0.0);
    vec3 map_up = normalize(shadow_centre - planet_centre);
    float map_solar = dot(map_up, sun_direction);
    for (int i = 0; i < 32; i++) {

        float low = start + float(i * i) * 32.0;
        float high = min(start + float((i + 1) * (i + 1)) * 32.0, finish);
        if (low >= finish) { break; }
        float stride = high - low;
        vec3 p = origin + ray * (low + high) * 0.5;
        float solar = dot(normalize(p), sun_direction);
        float day = smoothstep(-0.02, 0.12, solar);
        vec3 sunlight = mix(vec3(1.0, 0.48, 0.24), vec3(1.0, 0.97, 0.91), smoothstep(0.02, 0.25, solar));
        float height = max(ray_height(ray * (low + high) * 0.5), 0.0);
        float density = exp(-height / 650.0) * (1.0 - smoothstep(400.0, 900.0, height));
        float opacity = 1.0 - exp(-density * stride * 0.00024);
        float light = 1.0;
        if (sun_shafts > 0.001 && shadow_ready > 0.001 && map_solar > 0.03) {

            vec3 offset = p + planet_centre - shadow_centre;
            offset -= sun_direction * dot(offset, map_up) / map_solar;
            vec2 uv = vec2(dot(offset, shadow_east), dot(offset, shadow_north)) / shadow_span + 0.5;
            float coverage = 1.0 - smoothstep(0.38, 0.49, max(abs(uv.x - 0.5), abs(uv.y - 0.5)));
            float footprint = length(ray - sun_direction * dot(ray, map_up) / map_solar) * stride;
            light = mix(1.0, filtered_cloud_transmission(uv, footprint),
                coverage * smoothstep(0.03, 0.12, map_solar) * shadow_ready);

        }
        vec3 scattered = vec3(0.07, 0.10, 0.15) + sunlight * phase * 3.4 * light;
        radiance += scattered * day * transmission * opacity;
        transmission *= 1.0 - opacity;

    }
    return vec4(radiance, 1.0 - transmission);

}
