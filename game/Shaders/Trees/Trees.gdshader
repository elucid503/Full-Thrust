shader_type spatial;
render_mode cull_disabled;

uniform vec3 eye_position;
uniform vec3 planet_centre = vec3(0.0);
uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0);
uniform float planet_radius = 1274200.0;
uniform float wind_time;
uniform sampler2D broadleaf_map : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D pine_map : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D birch_map : source_color, filter_linear_mipmap_anisotropic;
varying vec3 local_position;
varying float distance_to_eye;
varying vec3 world_position;
varying vec3 sun_light;
varying float leaf_amount;
#include "res://Shaders/Atmosphere/Sunlight.gdshaderinc"
#include "res://Shaders/Clouds/CloudShadowSampling.gdshaderinc"

void vertex() {

    local_position = VERTEX;
    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_position = world;
    distance_to_eye = length(world - eye_position);
    float fade = 1.0 - smoothstep(800.0, 1600.0, distance_to_eye);
    float phase = float(INSTANCE_ID) * 2.399963;
    float bend = pow(clamp(VERTEX.y / 11.0, 0.0, 1.0), 2.0);
    float wind = sin(wind_time * (TAU * 587.0 / 4096.0) + phase) * 0.12
        + sin(wind_time * (TAU * 1825.0 / 4096.0) + phase + VERTEX.y) * UV2.x * 0.035;
    VERTEX.xz += vec2(1.0, 0.45) * wind * bend * (1.0 - smoothstep(250.0, 650.0, distance_to_eye));
    VERTEX *= fade;

}

void fragment() {
    float leaves = step(0.5, UV2.x);
    leaf_amount = leaves;
    sun_light = sun_radiance * sun_transmittance(world_position - planet_centre) * ground_cloud_sun(world_position);
    vec4 foliage = vec4(1.0);
    if (leaves > 0.5) {
        if (UV2.y < 0.5) { foliage = texture(broadleaf_map, UV); }
        else if (UV2.y < 1.5) { foliage = texture(pine_map, UV); }
        else { foliage = texture(birch_map, UV); }
        if (foliage.a < 0.45) { discard; }
    }
    float resolved = 1.0 - smoothstep(80.0, 220.0, distance_to_eye);
    float grain = sin(UV.x * 120.0 + sin(UV.y * 9.0) * 2.0);
    ALBEDO = COLOR.rgb * mix(vec3(1.0 + grain * 0.16 * resolved), foliage.rgb * 0.65, leaves);
    NORMAL = FRONT_FACING ? NORMAL : -NORMAL;
    ROUGHNESS = mix(0.95, 0.83, leaves);
    SPECULAR = 0.15;
    AO = mix(1.0, mix(0.72, 1.0, clamp(local_position.y / 10.5, 0.0, 1.0)), leaves);
    AO_LIGHT_AFFECT = 0.0;
    BACKLIGHT = ALBEDO * leaves * 0.25;
}

void light() {

    vec3 radiance = (LIGHT_IS_DIRECTIONAL ? sun_light : LIGHT_COLOR) * ATTENUATION;
    float diffuse = max(dot(NORMAL, LIGHT), 0.0) + max(dot(-NORMAL, LIGHT), 0.0) * leaf_amount * 0.2;
    DIFFUSE_LIGHT += radiance * diffuse / PI;

}
