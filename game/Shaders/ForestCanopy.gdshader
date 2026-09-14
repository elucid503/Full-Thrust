shader_type spatial;
render_mode cull_disabled;

uniform vec3 eye_position;
varying vec3 world_position;
varying float cloud_light;
varying float variation;

#include "res://Shaders/CloudShadowSampling.gdshaderinc"

void vertex() {

    vec3 root = MODEL_MATRIX[3].xyz;
    float distance = length(root - eye_position);
    float fade = smoothstep(700.0, 1600.0, distance) * (1.0 - smoothstep(6000.0, 8000.0, distance));
    vec3 up = normalize(MODEL_MATRIX[1].xyz);
    float width = length(MODEL_MATRIX[0].xyz);
    float height = length(MODEL_MATRIX[1].xyz);
    vec3 centre = root + up * height * 6.5;
    vec3 right = INV_VIEW_MATRIX[0].xyz;
    vec3 above = INV_VIEW_MATRIX[1].xyz;
    float projected_height = mix(width * 0.72, height, abs(dot(above, up)));
    world_position = centre + (right * VERTEX.x * width + above * VERTEX.y * projected_height) * fade;
    POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world_position, 1.0);
    variation = fract(float(INSTANCE_ID) * 0.618034);

}

void fragment() {

    vec2 p = UV * 2.0 - 1.0;
    float angle = atan(p.y, p.x);
    float lobes = 0.86 + 0.065 * sin(angle * 5.0 + variation * TAU) + 0.035 * sin(angle * 9.0 + variation * 12.0);
    float radius = length(p) / lobes;
    if (radius > 1.0) { discard; }
    NORMAL = normalize(vec3(p.x * 0.7, -p.y * 0.7, sqrt(max(1.0 - radius * radius, 0.01))));
    vec2 shadow = local_cloud_light(world_position);
    cloud_light = 1.0 - 0.32 * shadow.x * shadow.y;
    float crown = 0.72 + 0.28 * sqrt(max(1.0 - radius * radius, 0.0));
    ALBEDO = mix(vec3(0.055, 0.095, 0.022), vec3(0.085, 0.13, 0.037), variation) * crown * COLOR.rgb;
    ROUGHNESS = 0.92;
    SPECULAR = 0.08;

}

void light() {

    float shade = LIGHT_IS_DIRECTIONAL ? cloud_light : 1.0;
    DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * shade * (0.3 + 0.7 * max(dot(NORMAL, LIGHT), 0.0)) / PI;

}
