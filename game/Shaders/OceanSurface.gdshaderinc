uniform vec4 ripple_directions[12];
uniform vec4 ripple_states[12];
uniform vec3 ripple_up;
uniform vec4 ripple_detail_origin0 = vec4(0.0, 0.0, 0.0, 0.8);
uniform vec4 ripple_detail_origin1 = vec4(0.0, 0.0, 0.0, 0.256);
uniform vec4 ripple_detail_origin2 = vec4(0.0, 0.0, 0.0, 0.08192);
uniform float ripple_detail_strength = 1.0;

float ocean_hash(ivec3 cell) {

    uvec3 c = uvec3(cell) & uvec3(4095u);
    uint h = c.x * 1597334677u ^ c.y * 3812015801u ^ c.z * 2798796415u;
    h = (h ^ (h >> 16u)) * 2246822519u;
    h = (h ^ (h >> 13u)) * 3266489917u;
    return float((h ^ (h >> 16u)) & 16777215u) / 16777216.0;

}

// Analytic derivatives avoid a normal texture's texel grid and finite-difference blur.
vec3 ocean_detail(vec3 p) {

    ivec3 cell = ivec3(floor(p));
    vec3 f = fract(p);
    vec3 w = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    vec3 dw = 30.0 * f * f * (f * (f - 2.0) + 1.0);
    float a = ocean_hash(cell);
    float b = ocean_hash(cell + ivec3(1, 0, 0));
    float c = ocean_hash(cell + ivec3(0, 1, 0));
    float d = ocean_hash(cell + ivec3(1, 1, 0));
    float e = ocean_hash(cell + ivec3(0, 0, 1));
    float f1 = ocean_hash(cell + ivec3(1, 0, 1));
    float g = ocean_hash(cell + ivec3(0, 1, 1));
    float h = ocean_hash(cell + ivec3(1, 1, 1));
    return dw * vec3(mix(mix(b-a, d-c, w.y), mix(f1-e, h-g, w.y), w.z),
        mix(mix(c-a, d-b, w.x), mix(g-e, h-f1, w.x), w.z),
        mix(mix(e-a, f1-b, w.x), mix(g-c, h-d, w.x), w.y));

}

vec4 ocean_detail_band(vec3 p, vec4 origin, float footprint, float slope) {

    float resolved = 1.0 - smoothstep(origin.w * 0.35, origin.w * 1.3, footprint);
    vec3 cell = p / max(origin.w, 0.001) + origin.xyz;
    vec3 gradient = ocean_detail(cell) * slope * resolved;
    return vec4(gradient, slope * slope * (1.0 - resolved * resolved));

}

// Phases are rebased in doubles on the CPU; only small local coordinates enter these short waves.
vec4 ocean_ripples(vec3 local_position, float footprint) {

    vec3 gradient = vec3(0.0);
    float unresolved = 0.0;
    for (int i = 0; i < 12; i++) {

        vec3 direction = ripple_directions[i].xyz;
        float k = ripple_directions[i].w;
        float wavelength = TAU / max(k, 0.0001);
        float resolved = 1.0 - smoothstep(wavelength * 0.12, wavelength * 0.45, footprint);
        float slope = ripple_states[i].y;
        vec3 across = cross(ripple_up, direction);
        float bend = dot(local_position, across) * k * 0.19 + ripple_states[i].z;
        float packet = dot(local_position, direction) * k * 0.13 + ripple_states[i].w;
        float envelope = 0.72 + 0.28 * sin(packet);
        float phase = dot(local_position, direction) * k + ripple_states[i].x + 1.4 * sin(bend);
        gradient += (direction + across * (0.266 * cos(bend))) * (slope * resolved * envelope * cos(phase));
        gradient += direction * (0.0364 * slope * resolved * cos(packet) * sin(phase));
        unresolved += 0.5 * slope * slope * (1.0 - resolved * resolved);

    }
    vec4 detail = ocean_detail_band(local_position, ripple_detail_origin0, footprint, 0.065 * ripple_detail_strength)
        + ocean_detail_band(local_position, ripple_detail_origin1, footprint, 0.047 * ripple_detail_strength)
        + ocean_detail_band(local_position, ripple_detail_origin2, footprint, 0.026 * ripple_detail_strength);
    return vec4(gradient, unresolved) + detail;

}

// A small pressure depression relaxes smoothly into the surrounding sea, without concentric trenches.
vec4 ocean_pressure(vec3 offset, vec3 up, float radius, float strength) {

    vec3 horizontal = offset - up * dot(offset, up);
    float scale = max(radius, 1.0);
    float q2 = dot(horizontal, horizontal) / (scale * scale);
    float depression = min(scale * 0.018, 0.12) * strength * exp(-q2 * 0.7);
    return vec4(-depression, horizontal * (1.4 * depression / (scale * scale)));

}
