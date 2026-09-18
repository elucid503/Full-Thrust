shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

// Heat shimmer: the opaque scene behind the nozzle, refracted through scrolling noise. Drawn
// before every additive layer so the flame is added on top of the displaced background.

uniform sampler2D screen_texture : hint_screen_texture, filter_linear;
uniform sampler2D noise_texture : filter_linear_mipmap, repeat_enable;
uniform float strength = 1.0;
uniform float expand_linear = 1.5;
uniform float length_metres = 4.0;
uniform float radius_metres = 0.5;
uniform float effect_time = 0.0;
uniform float seed = 0.0;

varying float along;
varying float around;

void vertex() {

    along = clamp(0.5 - VERTEX.y, 0.0, 1.0);
    around = atan(VERTEX.x, VERTEX.z) / 6.2831853 + 0.5;

    vec2 radial = normalize(VERTEX.xz);
    VERTEX.xz = radial * radius_metres * (1.0 + expand_linear * along);
    VERTEX.y = -along * length_metres;
    NORMAL = normalize(vec3(radial.x, radius_metres * expand_linear / length_metres, radial.y));

}

void fragment() {

    float rim = clamp(abs(dot(normalize(NORMAL), VIEW)), 0.0, 1.0);
    float weight = rim * rim * (1.0 - along) * smoothstep(0.0, 0.08, along);

    vec2 uv = vec2(around * 3.0, along * 2.0 - effect_time * 3.0 + seed);
    vec2 wobble = texture(noise_texture, uv).rg - vec2(0.5);
    vec2 offset = wobble * strength * weight * 0.03;

    ALBEDO = texture(screen_texture, SCREEN_UV + offset).rgb;
    ALPHA = weight;

}
