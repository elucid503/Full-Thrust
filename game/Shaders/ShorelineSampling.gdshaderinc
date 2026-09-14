float shoreline_survey(vec2 uv) {

    ivec2 size = textureSize(shoreline_map, 0);
    vec2 pixel = vec2(fract(uv.x), clamp(uv.y, 0.5 / float(size.y), 1.0 - 0.5 / float(size.y))) * vec2(size) - 0.5;
    ivec2 low = ivec2(floor(pixel));
    vec2 weight = fract(pixel);
    low.y = clamp(low.y, 0, size.y - 1);
    int left = (low.x + size.x) % size.x;
    int right = (left + 1) % size.x;
    int bottom = min(low.y + 1, size.y - 1);
    // Hardware bilinear weights quantize a large survey texel into visible shoreline steps.
    return mix(mix(texelFetch(shoreline_map, ivec2(left, low.y), 0).r,
                   texelFetch(shoreline_map, ivec2(right, low.y), 0).r, weight.x),
               mix(texelFetch(shoreline_map, ivec2(left, bottom), 0).r,
                   texelFetch(shoreline_map, ivec2(right, bottom), 0).r, weight.x), weight.y);

}

float shoreline_height(vec3 position) {

    vec3 direction = normalize(position);
    vec2 uv = vec2(atan(-direction.z, direction.x) / TAU + 0.5,
        clamp(0.5 - asin(clamp(direction.y, -1.0, 1.0)) / PI,
            0.5 / float(textureSize(shoreline_map, 0).y), 1.0 - 0.5 / float(textureSize(shoreline_map, 0).y)));
    return coastal_elevation(direction * planet_radius, shoreline_survey(uv), direction.y);

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
