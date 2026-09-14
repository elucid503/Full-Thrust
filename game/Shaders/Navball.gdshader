shader_type canvas_item;
render_mode unshaded;

uniform vec3 local_up;
uniform vec3 local_east;
uniform vec3 local_north;

float gradient(vec3 axis, vec2 p, float z) {

    vec2 change = vec2(axis.x - axis.z * p.x / z, -axis.y - axis.z * p.y / z) / 76.0;
    return length(change);

}

float edge(float distance, float slope, float thickness) {

    return slope > 0.0 ? clamp(thickness * 0.5 + 0.5 - abs(distance) / slope, 0.0, 1.0) : 0.0;

}

void fragment() {

    vec2 p = UV * 2.0 - 1.0;
    float squared = dot(p, p);

    if (squared >= 1.0) {

        COLOR = vec4(0.0);

    } else {

        float z = sqrt(1.0 - squared);
        vec3 sphere = vec3(p.x, -p.y, z);
        float u = dot(sphere, local_up);
        float parallel = 0.0;

        if (abs(u) <= 0.9659) {

            int band = u > 0.70710678 ? 4 : u > 0.25881905 ? 3 : u > -0.25881905 ? 2 : u > -0.70710678 ? 1 : 0;
            float sine = sin(float(band - 2) * PI / 6.0);
            parallel = edge(u - sine, gradient(local_up, p, z), band == 2 ? 1.9 : 1.15) * (band == 2 ? 1.0 : 0.46);

        }

        float nearest = 2.0;
        vec3 chosen = vec3(0.0);
        bool cardinal = false;

        for (int index = 0; index < 4; index++) {

            float angle = PI * float(index) / 4.0;
            vec3 axis = local_east * cos(angle) - local_north * sin(angle);
            float distance = abs(dot(sphere, axis));

            if (distance < nearest) {

                nearest = distance;
                chosen = axis;
                cardinal = index % 2 == 0;

            }

        }

        float meridian = edge(dot(sphere, chosen), gradient(chosen, p, z), cardinal ? 1.3 : 1.0)
            * clamp((0.9986 - abs(u)) * 90.0, 0.0, 1.0) * (cardinal ? 0.46 : 0.20);
        vec3 base = u >= 0.0 ? vec3(0.475, 0.541, 0.612) : vec3(0.145, 0.161, 0.184);
        vec3 ink = mix(base, vec3(0.945, 0.961, 0.976), max(parallel, meridian)) * (0.78 + 0.22 * z);
        COLOR = vec4(ink, clamp((1.0 - sqrt(squared)) * 76.0, 0.0, 1.0));

    }

}
