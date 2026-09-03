shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, specular_disabled;

// Own shell above the surface so the deck casts an offset shadow and overhangs the limb.

uniform sampler2D cloud_map : filter_linear_mipmap_anisotropic;

uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0);

uniform float coverage_floor : hint_range(0.0, 1.0) = 0.08;
uniform float coverage_ceiling : hint_range(0.0, 1.0) = 0.62;

uniform float relief : hint_range(0.0, 8.0) = 2.4;
uniform vec3 tint : source_color = vec3(1.0, 0.995, 0.985);

// Matches the surface datum, so a cloud sits above the ground it shadows.
uniform float longitude_offset = 0.25;

// Bump relief has no self-shadowing, so at grazing light it flips the lambert term texel by texel
// and the deck reads as crinkled foil. Fade the perturbation out as the sun approaches the horizon.
uniform float relief_horizon = 0.02;
uniform float relief_daylight = 0.34;

varying vec3 world_normal;

void vertex() {

    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);

}

void fragment() {

    vec2 uv = vec2(UV.x + longitude_offset, UV.y);

    float density = texture(cloud_map, uv).r;

    vec2 texel = 1.0 / vec2(textureSize(cloud_map, 0));

    float west = texture(cloud_map, uv - vec2(texel.x, 0.0)).r;
    float east = texture(cloud_map, uv + vec2(texel.x, 0.0)).r;
    float north = texture(cloud_map, uv - vec2(0.0, texel.y)).r;
    float south = texture(cloud_map, uv + vec2(0.0, texel.y)).r;

    float standing = relief * smoothstep(relief_horizon, relief_daylight, dot(world_normal, sun_direction));

    NORMAL_MAP = normalize(vec3((west - east) * standing, (north - south) * standing, 1.0)) * 0.5 + 0.5;
    NORMAL_MAP_DEPTH = 1.0;

    ALBEDO = tint;
    ROUGHNESS = 1.0;

    ALPHA = smoothstep(coverage_floor, coverage_ceiling, density);

}
