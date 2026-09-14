uniform float eye_height = 0.0;
uniform vec3 eye_up = vec3(0.0, 1.0, 0.0);

float ray_height(vec3 offset) {

    // Preserve small altitude changes without subtracting two planet-sized floats.
    float radius = planet_radius + eye_height;
    float difference = eye_height * (2.0 * planet_radius + eye_height)
        + 2.0 * radius * dot(eye_up, offset) + dot(offset, offset);
    return difference / (sqrt(max(planet_radius * planet_radius + difference, 0.0)) + planet_radius);

}

vec2 eye_sphere_span(vec3 ray, float radius) {

    float b = (planet_radius + eye_height) * dot(eye_up, ray);
    float difference = radius - planet_radius - eye_height;
    float h = b * b + difference * (radius + planet_radius + eye_height);
    if (h < 0.0) { return vec2(1.0, -1.0); }
    return vec2(-b - sqrt(h), -b + sqrt(h));

}
