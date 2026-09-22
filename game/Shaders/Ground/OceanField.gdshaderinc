uniform vec3 ocean_origin;
uniform vec3 ocean_along[6];
// Integer angular cycles close the field around the planet without a phase seam.
uniform vec4 ocean_modes[6];
uniform float ocean_radius = 1274200.0;
uniform float ocean_wind = 10.0;

// Mirrors Ocean.Evaluate for the particle resting at p: its rise in x and height slope in yzw,
// its horizontal shift, and the rest-to-surface Jacobian that gathers water into the crests.
vec4 ocean_wave(vec3 p, float spacing, float elevation, out vec3 shift, out mat3 jacobian) {

    vec3 up = normalize(p);
    vec4 wave = vec4(0.0);
    shift = vec3(0.0);
    jacobian = mat3(1.0);
    float depth = max(-elevation, 0.0);
    float x = dot(up, ocean_origin);
    for (int i = 0; i < 6; i++) {

        float y = dot(up, ocean_along[i]);
        float radius_squared = x * x + y * y;
        float angle = radius_squared > 0.000001 ? atan(y, x) : 0.0;
        vec3 across = cross(ocean_origin, ocean_along[i]);
        float z = dot(up, across);
        vec3 across_gradient = (across - up * z) / ocean_radius;
        vec3 along_gradient = (ocean_along[i] - up * y) / ocean_radius;
        float bend_cycles = round(ocean_modes[i].x * 0.23);
        float packet_along = round(ocean_modes[i].x * 0.17);
        float packet_across = round(ocean_modes[i].x * 0.13);
        float bend = z * bend_cycles + float(i) * 2.31;
        float packet = y * packet_along + z * packet_across + float(i) * 4.17;
        float envelope = 0.6 + 0.4 * sin(packet);
        float phase = ocean_modes[i].x * angle + ocean_modes[i].w + 1.8 * sin(bend);
        float k = ocean_modes[i].x / ocean_radius;
        float wavelength = TAU / max(k, 0.000001);
        float fade = smoothstep(0.0025, 0.04, radius_squared) * (1.0 - smoothstep(wavelength * 0.12, wavelength * 0.35, spacing));
        float scale = min(ocean_modes[i].y, depth * 0.08) * fade;
        float amplitude = scale * envelope;
        vec3 gradient = (ocean_along[i] * x - ocean_origin * y) * (k / max(radius_squared, 0.0025));
        gradient += across_gradient * (1.8 * bend_cycles * cos(bend));
        vec3 envelope_gradient = (along_gradient * packet_along + across_gradient * packet_across) * (0.4 * cos(packet));
        vec3 direction = gradient / max(length(gradient), 1e-12);
        wave.x += amplitude * sin(phase);
        wave.yzw += gradient * (amplitude * cos(phase)) + envelope_gradient * (scale * sin(phase));
        shift += direction * (amplitude * cos(phase));
        jacobian -= outerProduct(direction, gradient) * (amplitude * sin(phase));

    }
    return wave;

}

// Mean square slope of the swell bands a footprint no longer resolves, for the roughness to carry.
float ocean_lost_variance(float spacing, float elevation) {

    float depth = max(-elevation, 0.0);
    float variance = 0.0;
    for (int i = 0; i < 6; i++) {

        float k = ocean_modes[i].x / ocean_radius;
        float wavelength = TAU / max(k, 0.000001);
        float slope = min(ocean_modes[i].y, depth * 0.08) * k;
        variance += 0.5 * slope * slope * smoothstep(wavelength * 0.12, wavelength * 0.35, spacing);

    }
    return variance;

}

// Crest amplitude the swell can reach at this depth, to normalise crest-driven effects.
float ocean_reach(float elevation) {

    float depth = max(-elevation, 0.0);
    float reach = 0.0;
    for (int i = 0; i < 6; i++) {

        reach += min(ocean_modes[i].y, depth * 0.08);

    }
    return reach;

}
