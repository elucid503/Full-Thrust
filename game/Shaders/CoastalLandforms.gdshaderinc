float coastal_elevation(vec3 p, float surveyed, float latitude) {

    float influence = 1.0 - smoothstep(8.0, 28.0, abs(surveyed));
    if (influence <= 0.0) { return surveyed; }
    float region = landscape_noise(p / 6000.0);
    vec3 q = p / 700.0 + vec3(region * 3.0, -region * 2.0, region);
    float coves = (landscape_noise(q) - 0.5) * 7.0 + (landscape_noise(p / 180.0) - 0.5) * 2.2;
    float wetland = (1.0 - smoothstep(0.38, 0.62, region)) * (1.0 - smoothstep(0.55, 0.80, abs(latitude)));
    float creek = 1.0 - smoothstep(0.02, 0.10, abs(landscape_noise(p / 310.0 + vec3(region * 2.0, 0.0, region)) - 0.5));
    float tidal = -creek * wetland * 2.8 * (1.0 - smoothstep(3.0, 9.0, abs(surveyed)));
    float bars = max(0.0, sin(-surveyed * 1.4 + landscape_noise(p / 240.0) * 4.0));
    bars *= smoothstep(-9.0, -5.0, surveyed) * (1.0 - smoothstep(-1.0, 1.0, surveyed)) * 1.3;
    return surveyed + (coves + tidal + bars) * influence;

}
