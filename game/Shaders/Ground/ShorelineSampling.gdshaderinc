// Fragment-only: a 10800x5400 vertex texture fetch of this survey crashes NVIDIA drivers near water.
float shoreline_curve(float a, float b, float c, float d, float t) {

    float before = b - a;
    float span = c - b;
    float after = d - c;
    float low = before * span > 0.0 ? 2.0 * before * span / (before + span) : 0.0;
    float high = span * after > 0.0 ? 2.0 * span * after / (span + after) : 0.0;
    return b + t * (low + t * (3.0 * span - 2.0 * low - high + t * (low + high - 2.0 * span)));

}

float shoreline_row(int before, int left, int right, int after, int row, float t) {

    return shoreline_curve(texelFetch(shoreline_map, ivec2(before, row), 0).r,
        texelFetch(shoreline_map, ivec2(left, row), 0).r,
        texelFetch(shoreline_map, ivec2(right, row), 0).r,
        texelFetch(shoreline_map, ivec2(after, row), 0).r, t);

}

float shoreline_survey(vec2 uv) {

    ivec2 size = textureSize(shoreline_map, 0);
    if (size.x < 2 || size.y < 2) { return 0.0; }
    vec2 pixel = vec2(fract(uv.x), clamp(uv.y, 0.5 / float(size.y), 1.0 - 0.5 / float(size.y))) * vec2(size) - 0.5;
    ivec2 low = ivec2(floor(pixel));
    vec2 weight = fract(pixel);
    low.y = clamp(low.y, 0, size.y - 1);
    int left = (low.x + size.x) % size.x;
    int right = (left + 1) % size.x;
    int bottom = min(low.y + 1, size.y - 1);
    // Hardware bilinear weights quantize a large survey texel into visible shoreline steps.
    vec4 middle = vec4(texelFetch(shoreline_map, ivec2(left, low.y), 0).r,
        texelFetch(shoreline_map, ivec2(right, low.y), 0).r,
        texelFetch(shoreline_map, ivec2(left, bottom), 0).r,
        texelFetch(shoreline_map, ivec2(right, bottom), 0).r);
    float linear = mix(mix(middle.x, middle.y, weight.x), mix(middle.z, middle.w, weight.x), weight.y);
    float smoothing = 1.0 - smoothstep(8.0, 28.0, abs(linear));
    if (smoothing <= 0.0 || size.x < 4 || size.y < 4) { return linear; }
    int before = (left + size.x - 1) % size.x;
    int after = (right + 1) % size.x;
    float curved = shoreline_curve(
        shoreline_row(before, left, right, after, max(low.y - 1, 0), weight.x),
        shoreline_curve(texelFetch(shoreline_map, ivec2(before, low.y), 0).r, middle.x, middle.y,
            texelFetch(shoreline_map, ivec2(after, low.y), 0).r, weight.x),
        shoreline_curve(texelFetch(shoreline_map, ivec2(before, bottom), 0).r, middle.z, middle.w,
            texelFetch(shoreline_map, ivec2(after, bottom), 0).r, weight.x),
        shoreline_row(before, left, right, after, min(low.y + 2, size.y - 1), weight.x), weight.y);
    return mix(linear, curved, smoothing);

}

float shoreline_height_at(vec3 position, float footprint) {

    vec3 direction = normalize(position);
    vec2 uv = vec2(atan(-direction.z, direction.x) / TAU + 0.5,
        clamp(0.5 - asin(clamp(direction.y, -1.0, 1.0)) / PI,
            0.5 / float(textureSize(shoreline_map, 0).y), 1.0 - 0.5 / float(textureSize(shoreline_map, 0).y)));
    return coastal_elevation_at(direction * planet_radius, shoreline_survey(uv), direction.y, footprint);

}

float shoreline_height(vec3 position) {

    return shoreline_height_at(position, 0.0);

}
