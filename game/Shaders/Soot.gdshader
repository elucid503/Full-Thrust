shader_type spatial;
render_mode blend_mix, depth_draw_never;

uniform sampler2D soot_map : filter_linear, repeat_enable;
uniform mat4 stage_from_model;
uniform float stage_base;
uniform float stage_length;
varying vec3 stage_position;

void vertex() {

    VERTEX += NORMAL * 0.001;
    stage_position = (stage_from_model * vec4(VERTEX, 1.0)).xyz;

}

void fragment() {

    float angle = atan(stage_position.z, stage_position.x) / 6.283185307 + 0.5;
    float height = clamp((stage_position.y - stage_base) / max(stage_length, 0.01), 0.011, 0.989);
    float coat = texture(soot_map, vec2(angle, height)).r;
    ALBEDO = vec3(0.024, 0.022, 0.019);
    ROUGHNESS = 0.96;
    METALLIC = 0.0;
    ALPHA = coat;

}
