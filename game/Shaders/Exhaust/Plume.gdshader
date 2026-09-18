shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec4 core_colour : source_color = vec4(1.0, 0.84, 0.66, 1.0);
uniform vec4 tail_colour : source_color = vec4(1.0, 0.4, 0.14, 1.0);
uniform float luminosity = 1.0;
uniform float length_radii : hint_range(8.0, 120.0) = 70.0;
uniform float air_spread : hint_range(0.01, 0.3) = 0.045;
uniform float vacuum_spread : hint_range(0.01, 0.5) = 0.16;
uniform float brightness : hint_range(0.1, 8.0) = 2.0;
uniform float exit_radius = 0.4;
uniform float plume_length = 20.0;
uniform float spread = 0.045;
uniform float throttle = 0.0;
uniform float purge = 0.0;
uniform float burnoff = 0.0;
uniform float effect_time = 0.0;

varying float flame_distance;

void vertex() {

    float along = 0.5 - VERTEX.y;
    flame_distance = along;
    VERTEX.xz *= exit_radius + along * plume_length * spread;
    VERTEX.y = -along * plume_length;

}

void fragment() {

    float along = flame_distance;
    float edge = pow(abs(dot(normalize(NORMAL), normalize(VIEW))), 1.5);
    float tail = 1.0 - smoothstep(0.15, 1.0, along);
    float flow = 0.9 + 0.1 * sin(along * 65.0 - effect_time * 45.0);
    float cells = 0.8 + 0.2 * cos(along * 100.0) * (1.0 - smoothstep(0.05, 0.35, along));
    vec3 colour = mix(core_colour.rgb, tail_colour.rgb, smoothstep(0.0, 0.5, along));
    ALBEDO = colour * luminosity * brightness;
    ALPHA = clamp((throttle + burnoff * 0.25 + purge * 0.06) * edge * tail * flow * cells, 0.0, 1.0);

}
