shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, specular_disabled;

// Own shell above the surface so the deck casts an offset shadow and overhangs the limb.

uniform sampler2D cloud_map : filter_linear_mipmap_anisotropic;

uniform float coverage_floor : hint_range(0.0, 1.0) = 0.08;
uniform float coverage_ceiling : hint_range(0.0, 1.0) = 0.62;

uniform float relief : hint_range(0.0, 8.0) = 2.4;
uniform vec3 tint : source_color = vec3(1.0, 0.995, 0.985);

// Matches the surface datum, so a cloud sits above the ground it shadows.
uniform float longitude_offset = 0.25;

void fragment() {

    vec2 uv = vec2(UV.x + longitude_offset, UV.y);

    float density = texture(cloud_map, uv).r;

    vec2 texel = 1.0 / vec2(textureSize(cloud_map, 0));

    float west = texture(cloud_map, uv - vec2(texel.x, 0.0)).r;
    float east = texture(cloud_map, uv + vec2(texel.x, 0.0)).r;
    float north = texture(cloud_map, uv - vec2(0.0, texel.y)).r;
    float south = texture(cloud_map, uv + vec2(0.0, texel.y)).r;

    NORMAL_MAP = normalize(vec3((west - east) * relief, (north - south) * relief, 1.0)) * 0.5 + 0.5;
    NORMAL_MAP_DEPTH = 1.0;

    ALBEDO = tint;
    ROUGHNESS = 1.0;

    ALPHA = smoothstep(coverage_floor, coverage_ceiling, density);

}
