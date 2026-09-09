// A planet-wide field keeps wave phase continuous across cube faces.
vec4 ocean_wave(vec3 p, float spacing) {

    vec4 wave = vec4(0.0);
    for (int i = 0; i < 4; i++) {

        float wavelength = 128.0 / exp2(float(i));
        vec3 direction = normalize(i == 0 ? vec3(1.0, 0.2, 0.3) : (i == 1 ? vec3(-0.3, 0.4, 1.0) :
            (i == 2 ? vec3(0.5, 1.0, 0.2) : vec3(-0.7, 0.3, 0.8))));
        float k = TAU / wavelength;
        float amplitude = 0.65 / exp2(float(i) * 0.8);
        amplitude *= 1.0 - smoothstep(wavelength * 0.12, wavelength * 0.35, spacing);
        float phase = dot(p, direction) * k - sqrt(9.81 * k * length(direction)) * environment_time;
        wave.x += amplitude * sin(phase);
        wave.yzw += amplitude * k * direction * cos(phase);

    }
    return wave;

}
