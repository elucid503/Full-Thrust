float shoreline_height(vec3 position) {

    vec3 direction = normalize(position);
    vec2 uv = vec2(atan(-direction.z, direction.x) / TAU + 0.5,
        clamp(0.5 - asin(clamp(direction.y, -1.0, 1.0)) / PI,
            0.5 / float(textureSize(shoreline_map, 0).y), 1.0 - 0.5 / float(textureSize(shoreline_map, 0).y)));
    return coastal_elevation(direction * planet_radius, textureLod(shoreline_map, uv, 0.0).r, direction.y);

}

float shoreline_slope(vec3 direction, float elevation) {

    if (abs(elevation) >= 28.0) { return 0.1; }
    vec3 east = normalize(cross(abs(direction.y) < 0.99 ? vec3(0, 1, 0) : vec3(1, 0, 0), direction));
    vec3 north = cross(direction, east);
    vec3 point = direction * planet_radius;
    vec2 gradient = vec2(shoreline_height(point + east * 32.0) - elevation,
        shoreline_height(point + north * 32.0) - elevation) / 32.0;
    return max(length(gradient), 0.002);

}
