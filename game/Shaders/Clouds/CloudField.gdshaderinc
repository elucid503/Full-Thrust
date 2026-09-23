
#ifndef CLOUD_COMPUTE
uniform sampler2D cloud_map : filter_linear_mipmap, repeat_enable;
uniform sampler3D shape_noise : filter_linear_mipmap, repeat_enable;
uniform sampler3D detail_noise : filter_linear_mipmap, repeat_enable;
uniform float base_radius = 1275400.0;
uniform float top_radius = 1281200.0;
uniform mat3 cloud_frame = mat3(1.0);
uniform float weather_coverage = 0.0;
uniform vec3 coastal_weather_direction = vec3(0.143, 0.478, 0.867);
#endif

vec3 cloud_unspin(vec3 p) {

    return cloud_frame * p;

}

// Two texels under a footprint at most: any finer, and the march strides past a distant cloud
// on one frame and through it the next, while the map's jitter flickers it under TAA.
float noise_lod(float footprint, float frequency, float texels) {

    return log2(max(1.0, footprint * frequency * texels * 3.0));

}

float shape_lod_at(float footprint, float frequency) {

    return noise_lod(footprint, frequency, float(textureSize(shape_noise, 0).x));

}

float detail_lod_at(float footprint, float frequency) {

    return noise_lod(footprint, frequency, float(textureSize(detail_noise, 0).x));

}

vec2 weather_at(vec3 p, float footprint) {

    vec3 fixed_p = cloud_unspin(p);
    vec3 up = normalize(fixed_p);
    vec2 uv = vec2(atan(-up.z, up.x) / TAU + 0.5, 0.5 - asin(clamp(up.y, -1.0, 1.0)) / PI);
    float cover = pow(textureLod(cloud_map, uv, 3.0).r, 0.65);
    float weather = textureLod(shape_noise, up * base_radius * 0.000007, shape_lod_at(footprint, 0.000007)).r;
    float coastal_weather = 1.0 - smoothstep(30000.0, 100000.0, length(up - coastal_weather_direction) * base_radius);
    float coverage = clamp(mix((cover - 0.22) * 1.35, 0.28 + cover * 0.64, coastal_weather)
        + (weather - 0.5) * 0.5, 0.0, 0.92);
    return vec2(clamp(coverage + weather_coverage, 0.0, 0.98), weather);

}

vec4 cloud_base_sample(vec3 p, float footprint, vec2 conditions, float height, bool envelope_only) {

    if (height <= 0.0 || height >= 1.0) { return vec4(-1.0, 0.0, 0.0, 0.0); }
    vec3 fixed_p = cloud_unspin(p);
    float coverage = conditions.x;
    float weather = conditions.y;
    float convection = smoothstep(0.35, 0.7, weather);
    float ceiling = mix(0.38, 0.96, convection);
    float floor_height = 0.035 + 0.035 * weather;
    float profile = smoothstep(floor_height, floor_height + 0.07, height)
        * (1.0 - smoothstep(ceiling * 0.45, ceiling, height));
    if (coverage < 0.015 || profile < 0.001) { return vec4(-1.0, 0.0, 0.0, 0.0); }

    // Different world-space wavelengths bend the tiled volume into coherent, non-grid-aligned systems.
    vec3 regional = fixed_p * 0.000017;
    vec3 warp = sin(regional.yzx * vec3(1.31, 0.91, 1.73) + sin(regional.zxy * 0.67)) * 6500.0;
    warp += (weather - 0.5) * 11000.0 * vec3(0.63, -0.51, 0.58);

    float shape_lod = shape_lod_at(footprint, 0.00010);
    float shape = textureLod(shape_noise, (fixed_p + warp) * 0.00010, shape_lod).r;
    float billow_resolved = 1.0 - smoothstep(120.0, 600.0, footprint);
    float billow = envelope_only ? 0.0 : 0.5;
    if (!envelope_only && billow_resolved > 0.001) { billow = textureLod(detail_noise, (fixed_p + warp * 0.3) * 0.00043, detail_lod_at(footprint, 0.00043)).r; }
    float meso_resolved = 1.0 - smoothstep(45.0, 160.0, footprint);
    float meso = envelope_only ? 0.0 : 0.5;
    if (!envelope_only && meso_resolved > 0.001) { meso = textureLod(detail_noise, fixed_p * 0.0014, detail_lod_at(footprint, 0.0014)).r; }
    shape += (0.5 - billow) * 0.28 * billow_resolved + (0.5 - meso) * 0.20 * meso_resolved;
    float sheet = smoothstep(0.35, 0.72, coverage) * (1.0 - convection * 0.45);
    shape = mix(shape, shape * 0.45 + 0.36, sheet);

    // Billows taper the underside as well as the silhouette.
    float threshold = 0.65 - coverage * 0.36;
    float body = (shape - threshold - (1.0 - profile) * 0.18) / 0.18;
    return vec4(body, profile, convection, billow);

}

vec4 cloud_base_at_height(vec3 p, float footprint, vec2 conditions, float height) {

    return cloud_base_sample(p, footprint, conditions, height, false);

}

float cloud_bound_at_height(vec3 p, float footprint, vec2 conditions, float height) {

    // Maximum omitted detail bounds occupied samples without fetching either erosion octave.
    return cloud_base_sample(p, footprint, conditions, height, true).x;

}

vec4 cloud_base(vec3 p, float footprint, vec2 conditions) {

    return cloud_base_at_height(p, footprint, conditions, (length(p) - base_radius) / (top_radius - base_radius));

}

float cloud_envelope(vec3 p, float footprint, vec2 conditions) {

    return cloud_base(p, footprint, conditions).x;

}

float density_from_base(vec3 p, float footprint, vec4 base) {

    // The body field climbs about one unit per 900 m across an edge. An edge sharper than the pixel
    // it falls in crawls and flickers under any sub-pixel motion, TAA's jitter included, so the
    // transition is never narrower than a footprint and a sub-pixel cloud fades instead of blinking.
    float width = max(0.335, footprint * 0.0017);
    if (base.x < 0.001) { return 0.0; }
    if (base.x >= max(1.0, 0.015 + width)) { return base.y * mix(0.85, 1.25, base.z); }
    float body = base.x;
    float erosion = mix(0.30, 0.08, smoothstep(250.0, 2400.0, footprint));
    body = max(body - base.w * erosion * max(1.0 - body, 0.0), 0.0);

    // Only resolve fine edge erosion when its projected footprint can retain it.
    float close = 1.0 - smoothstep(12.0, 48.0, footprint);

    if (close > 0.002) {

        vec3 fixed_p = cloud_unspin(p);
        float fray = textureLod(detail_noise, fixed_p * 0.0045, detail_lod_at(footprint, 0.0045)).r;

        body = max(body - fray * 0.35 * close * max(1.0 - body, 0.0), 0.0);

    }

    body = smoothstep(0.015, 0.015 + width, body);
    return body * base.y * mix(0.85, 1.25, base.z);

}

float density_in_weather(vec3 p, float footprint, vec2 conditions) {

    return density_from_base(p, footprint, cloud_base(p, footprint, conditions));

}

float density_at(vec3 p, float footprint) {

    float radius = length(p);
    if (radius <= base_radius || radius >= top_radius) { return 0.0; }
    return density_in_weather(p, footprint, weather_at(p, footprint));

}

float cloud_transmittance(vec3 p, vec3 sun, float extinction_coefficient) {

    float b = dot(p, sun);
    float radial_distance = length(p);
    float outer = b * b + (top_radius - radial_distance) * (top_radius + radial_distance);
    if (outer <= 0.0) { return 1.0; }
    float finish = -b + sqrt(outer);
    float start = 0.0;
    float inner = b * b + (base_radius - radial_distance) * (base_radius + radial_distance);
    if (length(p) < base_radius && inner > 0.0) { start = max(0.0, -b + sqrt(inner)); }
    float stride = max(finish - start, 0.0) / 6.0;
    float optical = 0.0;
    vec2 conditions = weather_at(p + sun * (start + stride * 3.0), max(stride, 1200.0));
    for (int i = 0; i < 6; i++) {

        vec3 sample_position = p + sun * (start + (float(i) + 0.5) * stride);
        optical += (stride < 3000.0 ? density_in_weather(sample_position, max(stride, 1200.0), conditions)
            : density_at(sample_position, stride)) * stride;

    }
    return exp(-optical * extinction_coefficient);

}

