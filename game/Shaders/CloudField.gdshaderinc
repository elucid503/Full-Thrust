uniform sampler2D cloud_map : filter_linear_mipmap, repeat_enable;
uniform sampler3D shape_noise : filter_linear, repeat_enable;
uniform sampler3D detail_noise : filter_linear, repeat_enable;
uniform float base_radius = 1275400.0;
uniform float top_radius = 1281200.0;
uniform float cloud_spin = 0.0;
uniform float drift = 0.0;

vec3 cloud_unspin(vec3 p) {

    float s = sin(-cloud_spin);
    float c = cos(-cloud_spin);
    return vec3(p.x * c + p.z * s, p.y, -p.x * s + p.z * c);

}

float density_at(vec3 p, float footprint) {

    float height = (length(p) - base_radius) / (top_radius - base_radius);
    if (height <= 0.0 || height >= 1.0) { return 0.0; }

    vec3 fixed_p = cloud_unspin(p);
    vec3 up = normalize(fixed_p);
    vec2 uv = vec2(atan(-up.z, up.x) / TAU + 0.5, 0.5 - asin(clamp(up.y, -1.0, 1.0)) / PI);
    float cover = textureLod(cloud_map, uv, 3.0).r;
    vec3 wind = vec3(drift, drift * 0.18, drift * 0.43);
    float weather = texture(shape_noise, fixed_p * 0.000007 + wind * 0.2).r;
    float convection = smoothstep(0.35, 0.7, weather);
    float ceiling = mix(0.25, 0.92, convection);
    float profile = smoothstep(0.0, 0.075, height) * (1.0 - smoothstep(ceiling * 0.48, ceiling, height));
    float coverage = clamp((cover - 0.22) * 1.35 + (weather - 0.5) * 0.3, 0.0, 1.0);
    if (coverage < 0.015 || profile < 0.001) { return 0.0; }

    float shape = texture(shape_noise, fixed_p * 0.000065 + wind).r;
    float billow = texture(detail_noise, fixed_p * 0.00029 + wind * 1.7).r;
    float threshold = 1.0 - coverage * 0.8;
    float body = smoothstep(threshold - 0.09, threshold + 0.15, shape + profile * 0.16);
    float erosion = mix(0.18, 0.045, smoothstep(250.0, 2400.0, footprint));
    body = max(body - billow * erosion * (1.0 - body), 0.0);
    return body * profile * mix(0.65, 1.0, convection);

}

