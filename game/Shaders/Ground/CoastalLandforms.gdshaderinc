// Footprint is metres of ground per pixel. Each landform fades once it is sub-resolution noise, so
// from altitude low islands keep their surveyed outline instead of dissolving into ponds.
float coastal_elevation_at(vec3 p, float surveyed, float latitude, float footprint) {

    float influence = 1.0 - smoothstep(8.0, 28.0, abs(surveyed));
    if (influence <= 0.0) { return surveyed; }
    float broad = 1.0 - smoothstep(20.0, 80.0, footprint);
    float fine = 1.0 - smoothstep(8.0, 30.0, footprint);
    if (broad <= 0.0) { return surveyed; }
    float region = landscape_noise(p / 6000.0);
    vec3 q = p / 700.0 + vec3(region * 3.0, -region * 2.0, region);
    float coves = (landscape_noise(q) - 0.5) * 2.4 * broad;
    if (fine > 0.0) { coves += (landscape_noise(p / 180.0) - 0.5) * 0.6 * fine; }
    float wetland = (1.0 - smoothstep(0.38, 0.62, region)) * (1.0 - smoothstep(0.55, 0.80, abs(latitude)));
    float tidal = -wetland * 0.45 * (1.0 - smoothstep(0.5, 2.0, abs(surveyed))) * broad;
    float bars = 0.0;
    if (fine > 0.0) {

        bars = max(0.0, sin(-surveyed * 1.4 + landscape_noise(p / 240.0) * 4.0));
        bars *= smoothstep(-9.0, -5.0, surveyed) * (1.0 - smoothstep(-1.0, 1.0, surveyed)) * 0.35 * fine;

    }
    return surveyed + (coves + tidal + bars) * influence;

}

float coastal_elevation(vec3 p, float surveyed, float latitude) {

    return coastal_elevation_at(p, surveyed, latitude, 0.0);

}
