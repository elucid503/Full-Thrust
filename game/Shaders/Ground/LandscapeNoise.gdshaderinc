float landscape_hash(vec3 p) {

    // Integer lattice hashing keeps shared corners identical after GPU expression optimisation.
    uvec3 cell = uvec3(ivec3(floor(p)));
    uint hash = cell.x * 1597334677u ^ cell.y * 3812015801u ^ cell.z * 2798796415u;
    hash = (hash ^ (hash >> 16u)) * 2246822519u;
    hash = (hash ^ (hash >> 13u)) * 3266489917u;
    hash ^= hash >> 16u;
    return float(hash & 16777215u) / 16777216.0;

}

float landscape_noise(vec3 p) {

    vec3 cell = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(landscape_hash(cell), landscape_hash(cell + vec3(1,0,0)), f.x),
                   mix(landscape_hash(cell + vec3(0,1,0)), landscape_hash(cell + vec3(1,1,0)), f.x), f.y),
               mix(mix(landscape_hash(cell + vec3(0,0,1)), landscape_hash(cell + vec3(1,0,1)), f.x),
                   mix(landscape_hash(cell + vec3(0,1,1)), landscape_hash(cell + vec3(1,1,1)), f.x), f.y), f.z);

}

