shader_type spatial;
render_mode cull_disabled;

uniform vec3 eye_position;
varying vec3 local_position;
varying float distance_to_eye;

void vertex() {

    local_position = VERTEX;
    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    distance_to_eye = length(world - eye_position);
    float fade = 1.0 - smoothstep(800.0, 1600.0, distance_to_eye);
    VERTEX *= fade;

}

void fragment() {

    float leaves = step(0.5, UV2.x);
    float grain = fract(sin(dot(floor(local_position * 5.0), vec3(12.9898, 78.233, 37.719))) * 43758.5453);
    float resolved = 1.0 - smoothstep(80.0, 220.0, distance_to_eye);
    vec2 leaf = UV * 2.0 - 1.0;
    float lobes = 0.82 + 0.12 * sin(leaf.x * 23.0) * sin(leaf.y * 19.0);
    if (leaves > 0.5 && dot(leaf, leaf) > lobes) {

        discard;

    }
    ALBEDO = COLOR.rgb * mix(1.0, 0.70 + grain * 0.50, leaves * resolved);
    ROUGHNESS = 0.92;
    SPECULAR = 0.15;
    BACKLIGHT = COLOR.rgb * leaves * 0.28;

}
