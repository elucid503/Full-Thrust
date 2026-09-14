vec4 atmosphere_composite(vec3 background, vec3 transmittance, vec3 inscatter) {

    float remaining = min(transmittance.r, min(transmittance.g, transmittance.b));
    return vec4(background * (transmittance - vec3(remaining)) + inscatter, 1.0 - remaining);

}
