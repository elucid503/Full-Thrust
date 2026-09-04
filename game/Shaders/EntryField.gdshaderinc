#include "res://Shaders/Volume.gdshaderinc"

uniform sampler2D hull_field : filter_linear, repeat_disable;
uniform vec4 field_domain = vec4(0.0, -1.0, 3.0, 12.0);
uniform mat4 flow_to_body;
uniform sampler2D footprint : filter_linear, repeat_disable;
uniform vec2 footprint_extent = vec2(2.0);
uniform float body_radius = 1.0;
uniform float intensity = 0.0;
uniform float heat = 0.0;
uniform float standoff = 0.15;
uniform float wake_length = 10.0;
uniform float ablation = 0.0;
uniform float brightness = 1.0;
uniform vec3 air_hot_colour : source_color = vec3(1.0, 0.7, 0.4);
uniform vec3 air_cool_colour : source_color = vec3(1.0, 0.35, 0.12);
uniform vec3 ablation_colour : source_color = vec3(1.0, 0.55, 0.14);
uniform float cooling_rate = 3.0;

vec4 hull_at(vec3 p) {

    vec3 body = (flow_to_body * vec4(p, 1.0)).xyz;
    float radial = length(body.xz);
    vec2 q = vec2(radial, body.y);
    vec2 uv = (q - field_domain.xy) / field_domain.zw;
    vec2 clamped = clamp(uv, vec2(0.0), vec2(1.0));
    vec3 field = textureLod(hull_field, clamped, 0.0).rgb;
    vec2 outside = (uv - clamped) * field_domain.zw;
    float distance = field.r + length(outside);
    vec3 normal = vec3(body.x / max(radial, 0.00001) * field.g, field.b, body.z / max(radial, 0.00001) * field.g);
    normal = transpose(mat3(flow_to_body)) * normal;

    return vec4(normal, distance);

}

vec3 footprint_at(vec2 lateral) {

    vec2 uv = lateral / (2.0 * footprint_extent) + 0.5;
    vec2 clamped = clamp(uv, vec2(0.0), vec2(1.0));
    vec3 field = textureLod(footprint, clamped, 0.0).rgb;
    field.b += length((uv - clamped) * 2.0 * footprint_extent);

    return field;

}
