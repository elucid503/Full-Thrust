uniform vec3 ocean_origin;
uniform vec3 ocean_along[4];
// Integer angular cycles close the field around the planet without a phase seam.
uniform vec4 ocean_modes[4];
uniform float ocean_radius = 1274200.0;
uniform float ocean_wind = 10.0;

vec4 ocean_wave(vec3 p, float spacing, float elevation) {

    vec3 up = normalize(p);
    vec4 wave = vec4(0.0);
    float depth = max(-elevation, 0.0);
    for (int i = 0; i < 4; i++) {

        float x = dot(up, ocean_origin);
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
        float resolved = 1.0 - smoothstep(wavelength * 0.12, wavelength * 0.35, spacing);
        float polar = smoothstep(0.0025, 0.04, radius_squared);
        float amplitude = min(ocean_modes[i].y, depth * 0.08) * polar * resolved;
        vec3 gradient = (ocean_along[i] * x - ocean_origin * y) * (k / max(radius_squared, 0.0025));
        gradient += across_gradient * (1.8 * bend_cycles * cos(bend));
        vec3 envelope_gradient = (along_gradient * packet_along + across_gradient * packet_across) * (0.4 * cos(packet));
        wave.x += amplitude * envelope * sin(phase);
        wave.yzw += (gradient * (envelope * cos(phase)) + envelope_gradient * sin(phase)) * amplitude;
    }
    return wave;

}
