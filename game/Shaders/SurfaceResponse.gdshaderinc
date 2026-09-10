uniform int response_count = 0;
uniform sampler2D response_map0 : filter_linear, repeat_disable;
uniform sampler2D response_map1 : filter_linear, repeat_disable;
uniform sampler2D response_map2 : filter_linear, repeat_disable;
uniform vec3 response_anchor0;
uniform vec3 response_anchor1;
uniform vec3 response_anchor2;
uniform vec3 response_side0;
uniform vec3 response_side1;
uniform vec3 response_side2;
uniform vec3 response_ahead0;
uniform vec3 response_ahead1;
uniform vec3 response_ahead2;

vec4 response_sample(sampler2D field, vec3 p, vec3 anchor, vec3 side, vec3 ahead) {

    vec3 offset = p - anchor;
    vec2 local = vec2(dot(offset, side), dot(offset, ahead));
    if (max(abs(local.x), abs(local.y)) > 62.0) { return vec4(0.0); }
    return textureLod(field, (local / 2.0 + vec2(32.5)) / 65.0, 0.0);

}

vec4 surface_response(vec3 p) {

    vec4 result = vec4(0.0);
    if (response_count > 0) { result += response_sample(response_map0, p, response_anchor0, response_side0, response_ahead0); }
    if (response_count > 1) { result += response_sample(response_map1, p, response_anchor1, response_side1, response_ahead1); }
    if (response_count > 2) { result += response_sample(response_map2, p, response_anchor2, response_side2, response_ahead2); }
    return result;

}
