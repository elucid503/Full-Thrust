shader_type spatial;
render_mode specular_schlick_ggx;

// Blue Marble albedo with no baked hillshade; relief is lit by the scene sun.

uniform sampler2D albedo_map : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D terrain_map : filter_linear_mipmap_anisotropic;
uniform sampler2D night_map : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D cloud_map : filter_linear_mipmap_anisotropic;

uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0);

// Godot's sphere starts its seam a quarter turn from where the mosaic starts its prime meridian.
uniform float longitude_offset = 0.25;

uniform float planet_radius = 1274200.0;
uniform float relief_metres = 8800.0;
uniform float relief_strength = 2.6;

// Bump relief has no self-shadowing, so at grazing light it flips the lambert term texel by texel
// and the ground reads as crinkled foil. Fade the perturbation out as the sun approaches the horizon.
uniform float relief_horizon = 0.02;
uniform float relief_daylight = 0.34;

uniform float cloud_altitude = 10000.0;
uniform float cloud_drift = 0.0;
uniform float cloud_shadow = 0.62;

uniform vec3 ocean_colour : source_color = vec3(0.008, 0.024, 0.055);
uniform float night_gain = 0.8;

// Cities belong below the geometric terminator; lighting them while the ground is still lit paints a gold band along it.
uniform float night_dusk = -0.02;
uniform float night_dark = -0.20;

varying vec3 world_normal;

void vertex() {

    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);

}

// Offset the cloud-shadow sample sunward by deck height times tan of the solar zenith angle.
vec2 cloud_shadow_uv(vec2 uv, float latitude) {

    vec3 up = world_normal;
    vec3 east = normalize(cross(vec3(0.0, 1.0, 0.0), up));
    vec3 north = cross(up, east);

    float overhead = max(dot(sun_direction, up), 0.20);

    float east_offset = cloud_altitude * dot(sun_direction, east) / overhead;
    float north_offset = cloud_altitude * dot(sun_direction, north) / overhead;

    float circumference = TAU * planet_radius * max(cos(latitude), 0.05);

    return uv + vec2(east_offset / circumference + cloud_drift, -north_offset / (PI * planet_radius));

}

void fragment() {

    vec2 uv = vec2(UV.x + longitude_offset, UV.y);

    vec3 ground = texture(albedo_map, uv).rgb;
    vec3 terrain = texture(terrain_map, uv).rgb;

    float water = terrain.g;
    float ice = terrain.b;

    float latitude = (0.5 - uv.y) * PI;

    vec2 texel = 1.0 / vec2(textureSize(terrain_map, 0));

    float metres_east = TAU * planet_radius * max(cos(latitude), 0.02) * texel.x;
    float metres_north = PI * planet_radius * texel.y;

    float west_height = texture(terrain_map, uv - vec2(texel.x, 0.0)).r;
    float east_height = texture(terrain_map, uv + vec2(texel.x, 0.0)).r;
    float north_height = texture(terrain_map, uv - vec2(0.0, texel.y)).r;
    float south_height = texture(terrain_map, uv + vec2(0.0, texel.y)).r;

    float slope_east = (east_height - west_height) * relief_metres / (2.0 * metres_east);
    float slope_north = (south_height - north_height) * relief_metres / (2.0 * metres_north);

    float sun_elevation = dot(world_normal, sun_direction);

    float standing = relief_strength * smoothstep(relief_horizon, relief_daylight, sun_elevation);

    vec3 relief = normalize(vec3(-slope_east * standing, -slope_north * standing, 1.0));

    NORMAL_MAP = mix(relief, vec3(0.0, 0.0, 1.0), water) * 0.5 + 0.5;
    NORMAL_MAP_DEPTH = 1.0;

    float shade = texture(cloud_map, cloud_shadow_uv(uv, latitude)).r;
    float lit = 1.0 - cloud_shadow * smoothstep(0.10, 0.70, shade);

    ALBEDO = mix(ground, max(ground, ocean_colour), water) * lit;

    METALLIC = 0.0;
    ROUGHNESS = mix(mix(0.94, 0.55, ice), 0.055, water);
    SPECULAR = mix(0.5, 0.25, water);

    float night = smoothstep(night_dusk, night_dark, sun_elevation);

    EMISSION = texture(night_map, uv).rgb * night_gain * night * lit;

}
