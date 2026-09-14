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
        float phase = ocean_modes[i].x * angle + ocean_modes[i].w;
        float k = ocean_modes[i].x / ocean_radius;
        float wavelength = TAU / max(k, 0.000001);
        float resolved = 1.0 - smoothstep(wavelength * 0.12, wavelength * 0.35, spacing);
        float polar = smoothstep(0.0025, 0.04, radius_squared);
        float amplitude = min(ocean_modes[i].y, depth * 0.08) * polar * resolved;
        vec3 gradient = (ocean_along[i] * x - ocean_origin * y) * (k / max(radius_squared, 0.0025));
        wave.x += amplitude * sin(phase);
        wave.yzw += gradient * (amplitude * cos(phase));
    }
    return wave;

}
