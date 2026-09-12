float irregular_hash(vec3 p) {

    p = fract(p * vec3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return fract((p.x + p.y) * p.z);

}

float irregular_noise(vec3 p) {

    vec3 cell = floor(p);
    vec3 f = fract(p);
    f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    return mix(mix(mix(irregular_hash(cell), irregular_hash(cell + vec3(1, 0, 0)), f.x),
                   mix(irregular_hash(cell + vec3(0, 1, 0)), irregular_hash(cell + vec3(1, 1, 0)), f.x), f.y),
               mix(mix(irregular_hash(cell + vec3(0, 0, 1)), irregular_hash(cell + vec3(1, 0, 1)), f.x),
                   mix(irregular_hash(cell + vec3(0, 1, 1)), irregular_hash(cell + vec3(1, 1, 1)), f.x), f.y), f.z);

}
