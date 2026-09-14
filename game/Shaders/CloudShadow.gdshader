shader_type canvas_item;
render_mode unshaded;

#include "res://Shaders/CloudField.gdshaderinc"

uniform vec3 map_centre;
uniform vec3 map_east;
uniform vec3 map_north;
uniform vec3 map_sun;
uniform float map_radius;
uniform float map_span = 16000.0;

void fragment() {

    vec2 offset = (UV - 0.5) * map_span;
    vec3 p = normalize(map_centre + map_east * offset.x + map_north * offset.y) * map_radius;
    float transmission = dot(normalize(p), map_sun) > 0.0 ? cloud_transmittance(p, map_sun, 0.004) : 1.0;
    COLOR = vec4(vec3(transmission), 1.0);

}
