vec4 atmosphere_composite(vec3 background, vec3 transmittance, vec3 inscatter) {

    float remaining = min(transmittance.r, min(transmittance.g, transmittance.b));
    return vec4(background * (transmittance - vec3(remaining)) + inscatter, 1.0 - remaining);

}

vec4 cloud_fog_composite(vec4 cloud, vec4 foreground_fog, vec4 full_fog) {

    return foreground_fog * cloud.a + full_fog * (1.0 - cloud.a) + cloud * (1.0 - foreground_fog.a);

}
