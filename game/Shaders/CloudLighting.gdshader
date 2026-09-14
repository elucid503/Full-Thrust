shader_type canvas_item;
render_mode unshaded, blend_disabled;

#include "res://Shaders/CloudField.gdshaderinc"

uniform vec3 map_centre;
uniform vec3 map_east;
uniform vec3 map_north;
uniform vec3 map_sun;
uniform float map_radius;
uniform float map_span = 64000.0;

void fragment() {

    vec2 offset = (UV - 0.5) * map_span;
    vec3 ground = normalize(map_centre + map_east * offset.x + map_north * offset.y) * map_radius;
    float solar = max(dot(normalize(ground), map_sun), 0.08);
    float stride = (top_radius - base_radius) / (12.0 * solar);
    vec3 start = ground + map_sun * (base_radius - map_radius) / solar;
    vec3 optical = vec3(0.0);
    for (int i = 0; i < 12; i++) {

        vec3 p = start + map_sun * (float(i) + 0.5) * stride;
        float height = (length(p) - base_radius) / (top_radius - base_radius);
        float amount = density_at(p, 550.0) * stride * 0.009;
        optical += amount * clamp((vec3(height) - vec3(0.0, 0.333333, 0.666667)) * 12.0 + 0.5, 0.0, 1.0);

    }
    COLOR = vec4(clamp(optical / 16.0, 0.0, 1.0), 1.0);

}
