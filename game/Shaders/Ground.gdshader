shader_type spatial;
render_mode specular_schlick_ggx;

// Geographic cover fractions select materials; body-fixed noise supplies the local landscape.

uniform sampler2D biome_map : filter_linear_mipmap, repeat_enable;
uniform sampler2D shoreline_map : filter_linear, repeat_enable;
uniform sampler2D night_map : source_color, filter_linear_mipmap_anisotropic;
uniform float camera_altitude = 0.0;
uniform vec2 terrain_rotation = vec2(1.0, 0.0);


uniform sampler2D rock_colour : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_colour : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D wave_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;

uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0);
uniform vec3 planet_centre = vec3(0.0, 0.0, 0.0);

uniform float planet_radius = 1274200.0;


// Two fixed tile sizes in metres rather than one chosen from the viewing distance. A tile that
// tracks the camera keeps its size on screen, which sounds right and is not: the pattern then slides
// through scale as the camera moves and reads as grain. Fixed tiles let the mip chain do what it is
// for - the fine one averages itself away with distance and the coarse one takes over - which is
// also what ground actually does to an eye moving away from it.
uniform float detail_fine = 4.0;
uniform float detail_coarse = 64.0;

// A third material scale carries surface variation into aerial views.
uniform float detail_macro = 1024.0;

// How much of the relief comes from the coarse tile. The fine one is grain; the coarse one is the
// shape of the ground between one vertex of the mesh and the next.
uniform float detail_balance : hint_range(0.0, 1.0) = 0.62;

uniform float macro_contrast : hint_range(0.0, 1.0) = 0.38;

// Material hue supplements the regional palette without overwhelming it.
uniform float material_hue : hint_range(0.0, 1.0) = 0.12;

// Detail is worth nothing once its tile is under a pixel. Both of these are metres of ground to a
// pixel rather than metres of range: a range threshold is a cone drawn on the ground, plain to see
// from the air and sliding about as the vehicle climbs, where a sampling threshold rides the same
// footprint the mip chain is already fading on and cannot be picked out at all.
uniform float detail_near = 3.0;
uniform float detail_far = 14.0;

// The widest tile is carried much further out than the other two, because from altitude it is the
// only thing standing between the imagery and a flat wash.
uniform float macro_far = 160.0;

uniform float detail_relief : hint_range(0.0, 4.0) = 0.85;
uniform float detail_contrast : hint_range(0.0, 1.0) = 0.32;

// Slope, as one less the cosine to the local vertical, over which soil gives way to bare rock.
uniform float rock_slope_low = 0.05;
uniform float rock_slope_high = 0.32;

uniform float snow_line = 520.0;
uniform float snow_band = 180.0;

uniform vec3 shallow_colour : source_color = vec3(0.045, 0.155, 0.178);
uniform vec3 deep_colour : source_color = vec3(0.003, 0.012, 0.029);

uniform float shallow_depth = 52.0;

uniform float wave_tile = 32.0;
uniform float wave_speed = 0.032;
uniform float wave_relief : hint_range(0.0, 3.0) = 0.28;

// A mirror-smooth sea aliases into sparkle the moment one wave falls under a pixel. The waves are
// faded out over the same range the roughness comes up over, which is the same statement made twice:
// what the normal map stops resolving is exactly what the roughness has to stand in for.
uniform float sea_roughness_near = 0.18;

// Once the swell is under a pixel the roughness is all that stands in for it, and a value this near
// the mirror end turned the whole sea into one sun-coloured sheet on the way up. Open water scatters
// over a mean square slope closer to this, and reads as water from orbit instead of as chrome.
uniform float sea_roughness_far = 0.46;

uniform float swell_near = 1.5;
uniform float swell_far = 26.0;

uniform float cloud_altitude = 9000.0;
uniform float cloud_shadow : hint_range(0.0, 1.0) = 0.32;

uniform float snow_albedo : hint_range(0.4, 1.0) = 0.72;

// Optional close-range palette adjustment, neutral by default.
uniform float close_lift : hint_range(0.5, 1.0) = 1.0;

// A lit surface under air is filled by the whole sky as well as by the sun, and without it every
// slope facing away from the sun goes to black.
uniform float sky_fill : hint_range(0.0, 0.5) = 0.055;

// The lights are compressed into a JPEG, and a JPEG has no true black: without a floor taken off
// it the whole night side carries a flat warm haze rather than only its cities.
uniform float night_floor = 0.035;

uniform float night_gain = 0.32;
uniform float night_dusk = -0.02;
uniform float night_dark = -0.20;

varying float cloud_light;
varying vec3 world_position;
varying float surface_elevation;
instance uniform float lod_morph : instance_index(0) = 0.0;

#include "res://Shaders/CloudField.gdshaderinc"
#include "res://Shaders/OceanField.gdshaderinc"
#include "res://Shaders/CloudShadowSampling.gdshaderinc"

instance uniform float mesh_spacing : instance_index(1) = 1.0;
varying float ocean_room;
varying float coastal_gradient;
varying float smooth_coastal_height;

#include "res://Shaders/LandscapeNoise.gdshaderinc"
#include "res://Shaders/CoastalLandforms.gdshaderinc"
#include "res://Shaders/ShorelineSampling.gdshaderinc"

void vertex() {

    VERTEX += CUSTOM0.xyz * lod_morph;
    surface_elevation = CUSTOM0.w;
    smooth_coastal_height = CUSTOM1.r;
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    ocean_room = smoothstep(0.0, 4.0, -CUSTOM0.w);
    float local = 1.0 - smoothstep(20000.0, 25000.0, camera_altitude);
    vec3 radial = world_position - planet_centre;
    vec3 fixed_vertex = vec3(terrain_rotation.x * radial.x - terrain_rotation.y * radial.z,
        radial.y, terrain_rotation.y * radial.x + terrain_rotation.x * radial.z);
    coastal_gradient = shoreline_slope(normalize(fixed_vertex), shoreline_height(fixed_vertex));
    vec4 wave = ocean_wave(fixed_vertex, mesh_spacing);
    vec3 up = normalize(world_position - planet_centre);
    float displacement = wave.x * ocean_room * local;
    float distance = length(world_position - planet_centre - water_impact.xyz);
    float influence = exp(-distance / max(water_impact.w * 4.0, 1.0)) * impact_strength;
    displacement += (sin(distance * 0.7 - environment_time * 5.0) * 0.25 - 0.4) * influence * ocean_room * local;
    VERTEX += inverse(mat3(MODEL_MATRIX)) * up * displacement;
    world_position += up * displacement;

}


float surface_grain(vec2 metres, float scale) {

    // The 64-cell period matches every power-of-two UV origin used by the terrain patches.
    vec2 p = metres / scale;
    vec2 cell = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    vec2 low = mod(cell, vec2(64.0));
    vec2 high = mod(cell + 1.0, vec2(64.0));
    return mix(mix(landscape_hash(vec3(low, scale)), landscape_hash(vec3(high.x, low.y, scale)), f.x),
        mix(landscape_hash(vec3(low.x, high.y, scale)), landscape_hash(vec3(high, scale)), f.x), f.y);

}

// Offset the shadow sample sunward by deck height over the cosine of the solar zenith angle, so a
// cloud shades the ground it is actually over rather than the ground directly beneath it.


float overcast(vec3 radial, vec3 up) {

    return 1.0 - cloud_transmittance(radial, sun_direction, 0.004);

}

#include "res://Shaders/TerrainDetail.gdshaderinc"


void fragment() {

    vec3 radial = world_position - planet_centre;
    vec3 fixed_position = vec3(terrain_rotation.x * radial.x - terrain_rotation.y * radial.z,
        radial.y, terrain_rotation.y * radial.x + terrain_rotation.x * radial.z);

    float height = length(radial);

    vec3 up = radial / height;

    // Interpolated survey height avoids spherical chord sag and large-coordinate rounding.
    float elevation = surface_elevation;

    vec3 fixed_up = normalize(fixed_position);
    vec2 geography = vec2(atan(-fixed_up.z, fixed_up.x) / TAU + 0.5,
        clamp(0.5 - asin(clamp(fixed_up.y, -1.0, 1.0)) / PI, 0.00025, 0.99975));
    vec4 biome = texture(biome_map, geography);
    vec4 climate = vec4(0.0, biome.a, biome.r, biome.b);
    vec3 ground;

    vec2 coast_uv = vec2(geography.x, clamp(geography.y,
        0.5 / float(textureSize(shoreline_map, 0).y), 1.0 - 0.5 / float(textureSize(shoreline_map, 0).y)));
    float coast_elevation = coastal_elevation(fixed_up * planet_radius, texture(shoreline_map, coast_uv).r, fixed_up.y);
    // Close meshes carry double-sampled heights without planet-coordinate quantization.
    coast_elevation = mix(smooth_coastal_height, coast_elevation, smoothstep(4.0, 16.0, mesh_spacing));
    float depth = max(-coast_elevation, 0.0);
    // Resolve the surveyed zero contour per pixel, including on distant coarse patches.
    float coast_width = max(fwidth(coast_elevation), 0.02);
    float sea = 1.0 - smoothstep(-coast_width, coast_width, coast_elevation);
    // Fixed metre offsets avoid quantized screen derivatives moving the beach and surf with the eye.
    float coast_slope = max(coastal_gradient, 0.002);
    float coast_distance = coast_elevation / coast_slope;

    // Metres of ground under one pixel. Every fade below is driven off this rather than off the
    // range, so nothing steps at a fixed distance from the eye.
    float footprint = max(length(dFdx(UV2)), length(dFdy(UV2)));

    vec2 near_uv = UV2 / detail_fine;
    vec2 far_uv = UV2 / detail_coarse;
    vec2 wide_uv = UV2 / detail_macro;

    float detail = 1.0 - smoothstep(detail_near, detail_far, footprint);

    float macro = 1.0 - smoothstep(detail_far, macro_far, footprint);

    vec3 world_normal = normalize(mat3(INV_VIEW_MATRIX) * NORMAL);

    float slope = 1.0 - clamp(dot(world_normal, up), 0.0, 1.0);

    float bare = smoothstep(rock_slope_low, rock_slope_high, slope);

    float latitude = abs(up.y);
    float regional_snow_line = mix(950.0, 80.0, smoothstep(0.3, 0.96, latitude));
    float snow = max(climate.g, smoothstep(regional_snow_line, regional_snow_line + snow_band, elevation))
        * (1.0 - smoothstep(0.12, 0.42, slope)) * (1.0 - sea);
    vec3 habitat_position = fixed_up * planet_radius;
    float region = landscape_noise(habitat_position / 1800.0);
    float geology = landscape_noise(habitat_position / 8500.0 + vec3(7.0, 23.0, 11.0));
    vec3 habitat = habitat_position / vec3(650.0, 1100.0, 420.0) + region * vec3(1.7, 0.8, -1.3);
    float mosaic = landscape_noise(habitat) * 0.55
        + landscape_noise(habitat * 2.13 + vec3(17.0, 3.0, 9.0)) * 0.30
        + landscape_noise(habitat * 5.71 + vec3(2.0, 31.0, 7.0)) * 0.15;
    float coastal = (1.0 - smoothstep(0.55, 0.75, abs(fixed_up.y)))
        * (1.0 - smoothstep(15.0, 45.0, elevation)) * (1.0 - biome.b);
    float groves = smoothstep(0.42, 0.68, mosaic);
    float damp = coastal * (1.0 - smoothstep(2.0, 8.0, elevation)) * (1.0 - groves);
    float vegetation = smoothstep(0.18, 0.80, biome.r + (mosaic - 0.5) * 0.5)
        * (1.0 - bare) * (1.0 - snow);
    float close_detail = close_detail_strength * (1.0 - smoothstep(300.0, 1200.0, length(VERTEX)))
        * (1.0 - smoothstep(0.12, 1.2, footprint)) * (1.0 - sea) * (1.0 - snow);
    vec2 detail_dx = dFdx(near_uv);
    vec2 detail_dy = dFdy(near_uv);
    vec2 metres_dx = dFdx(UV2);
    vec2 metres_dy = dFdy(UV2);
    float cavity = 1.0;
    float close_roughness = 0.85;
    if (close_detail > 0.001) {

        vec3 tangent_eye = vec3(dot(VIEW, normalize(TANGENT)), dot(VIEW, normalize(BINORMAL)), dot(VIEW, normalize(NORMAL)));
        float grazing = smoothstep(0.06, 0.24, tangent_eye.z);
        float depth_resolved = 1.0 - smoothstep(0.08, 0.5, footprint);
        if (depth_resolved > 0.001) {

            near_uv = terrain_parallax(near_uv, detail_dx, detail_dy, tangent_eye, bare,
                close_detail * grazing * depth_resolved, detail_fine);

        }
        vec3 rock_data = textureGrad(rock_detail, near_uv, detail_dx, detail_dy).rgb;
        vec3 soil_data = textureGrad(soil_detail, near_uv, detail_dx, detail_dy).rgb;
        float rock_height_value = rock_data.r;
        float soil_height_value = soil_data.r;
        bare = mix(bare, smoothstep(-0.16, 0.16, bare * 2.0 - 1.0 + rock_height_value - soil_height_value), close_detail);
        cavity = mix(soil_data.g, rock_data.g, bare);
        close_roughness = mix(soil_data.b, rock_data.b, bare);

    }
    float forest = biome.g * vegetation * mix(0.25, 1.5, groves);
    float beach_width = mix(22.0, 65.0, region) * (1.0 - smoothstep(0.12, 0.45, coast_slope));
    float beach = (1.0 - smoothstep(beach_width * 0.4, max(beach_width, 1.0), coast_distance))
        * (1.0 - smoothstep(3.0, 7.0, elevation)) * (1.0 - damp * 0.5);
    float sand = coastal * smoothstep(0.56, 0.78, 1.0 - mosaic)
        * smoothstep(3.0, 9.0, elevation) * (1.0 - damp) * 0.35;
    beach = max(beach, sand) * (1.0 - bare) * (1.0 - sea);
    vec3 earth = mix(vec3(0.115, 0.082, 0.046), vec3(0.38, 0.265, 0.135), biome.b);
    vec3 grass = mix(vec3(0.095, 0.135, 0.038), vec3(0.22, 0.20, 0.075), biome.b);
    vec3 woodland = mix(vec3(0.070, 0.105, 0.043), vec3(0.100, 0.135, 0.052), region);
    vec3 rock_tint = mix(vec3(0.20, 0.19, 0.17), vec3(0.32, 0.18, 0.105), biome.b * region);
    rock_tint = mix(rock_tint, vec3(0.31, 0.30, 0.25), smoothstep(0.50, 0.76, geology) * 0.7);
    earth *= mix(vec3(0.83, 0.91, 0.92), vec3(1.19, 1.05, 0.88), geology);
    grass = mix(grass, mix(vec3(0.12, 0.17, 0.060), vec3(0.23, 0.235, 0.11), region), coastal * 0.8);
    vegetation = max(vegetation, coastal * 0.85);
    ground = mix(earth, grass, vegetation);
    ground = mix(ground, vec3(0.095, 0.125, 0.060), damp * 0.35);
    ground = mix(ground, woodland, forest * 0.5);
    ground = mix(ground, rock_tint, bare);
    vec3 beach_colour = mix(vec3(0.32, 0.27, 0.18), vec3(0.57, 0.51, 0.38), geology);
    ground = mix(ground, beach_colour, beach * (1.0 - biome.a));
    float swash = sin(environment_time * 1.05 + landscape_noise(habitat_position / 90.0) * 3.0) * 1.4;
    float wet_sand = (1.0 - smoothstep(2.0 + swash, 12.0 + swash, coast_distance)) * beach;
    ground *= 1.0 - wet_sand * 0.38;
    ground *= mix(0.90, 1.10, mosaic);
    if (close_detail > 0.001) {

        float patches = landscape_noise(fixed_position / 28.0 + region * 3.0);
        float flecks = landscape_noise(fixed_position / 7.0 + mosaic * 2.0);
        vec3 dry_tint = mix(vec3(0.78, 0.89, 0.69), vec3(1.22, 1.04, 0.78), patches);
        ground *= mix(vec3(1.0), dry_tint * mix(0.85, 1.18, flecks), close_detail * 0.7);

    }
    if (forest > 0.01 && footprint < 22.0) {

        float canopy = landscape_noise(fixed_position / 12.0);
        float resolved = 1.0 - smoothstep(6.0, 22.0, footprint);
        ground *= 1.0 - forest * resolved * (0.12 + 0.35 * smoothstep(0.42, 0.70, canopy));

    }

    float procedural = 1.0 - smoothstep(20000.0, 25000.0, camera_altitude);
    vec3 local_ground = ground;
    vec3 relief = vec3(0.0, 0.0, 1.0);

    if (macro > 0.001 && sea < 0.999) {

        // Broad blotching first: the same material read at a few hundred metres a tile, which is
        // the scale the imagery has already smoothed away.
        vec3 wide = mix(textureGrad(soil_colour, wide_uv, metres_dx / detail_macro, metres_dy / detail_macro).rgb,
            textureGrad(rock_colour, wide_uv, metres_dx / detail_macro, metres_dy / detail_macro).rgb, bare);

        float blotch = dot(wide, vec3(0.2126, 0.7152, 0.0722));

        ground *= 1.0 + macro_contrast * macro * (1.0 - snow * 0.6) * (blotch - 0.5) * 2.0;

    }

    if (detail > 0.001 && sea < 0.999) {

        vec3 near_grain = mix(textureGrad(soil_colour, near_uv, detail_dx, detail_dy).rgb, textureGrad(rock_colour, near_uv, detail_dx, detail_dy).rgb, bare);
        vec3 far_grain = mix(textureGrad(soil_colour, far_uv, metres_dx / detail_coarse, metres_dy / detail_coarse).rgb,
            textureGrad(rock_colour, far_uv, metres_dx / detail_coarse, metres_dy / detail_coarse).rgb, bare);

        vec3 near_bump = mix(textureGrad(soil_normal, near_uv, detail_dx, detail_dy).rgb, textureGrad(rock_normal, near_uv, detail_dx, detail_dy).rgb, bare);
        vec3 far_bump = mix(textureGrad(soil_normal, far_uv, metres_dx / detail_coarse, metres_dy / detail_coarse).rgb,
            textureGrad(rock_normal, far_uv, metres_dx / detail_coarse, metres_dy / detail_coarse).rgb, bare);

        relief = mix(near_bump, far_bump, mix(detail_balance, 0.18, close_detail)) * 2.0 - 1.0;

        vec3 material = mix(near_grain, far_grain, mix(detail_balance, 0.18, close_detail));

        // Keep the material's contrast while preserving the regional palette.
        float grain = dot(material, vec3(0.2126, 0.7152, 0.0722));

        vec3 tint = material / max(grain, 0.04);

        vec3 lifted = pow(ground, vec3(close_lift)) * (1.0 + detail_contrast * (1.0 - snow * 0.6) * (grain - 0.5) * 2.0);

        lifted *= mix(vec3(1.0), tint, material_hue * (1.0 - snow) * (1.0 - sea));

        ground = mix(ground, lifted, detail);

    }

    if (procedural > 0.0 && sea < 0.999) {

        local_ground = ground;
        float grain = 0.0;
        float amplitude = 0.55;
        float scale = 64.0;
        for (int octave = 0; octave < 8; octave++) {

            float resolved = 1.0 - smoothstep(scale * 0.2, scale * 0.7, footprint);
            if (resolved < 0.001) { break; }
            grain += (surface_grain(UV2, scale) - 0.5) * amplitude * resolved;
            scale *= 0.5;
            amplitude *= 0.6;

        }
        float strata_resolved = 1.0 - smoothstep(1.0, 5.0, footprint);
        float strata = sin(elevation * 1.8 + mosaic * 9.0 + geology * 13.0)
            + sin(elevation * 0.39 + region * 7.0) * 0.7;
        grain += strata * bare * strata_resolved * 0.08;
        float ripple_resolved = 1.0 - smoothstep(0.08, 0.4, footprint);
        grain += sin(UV2.x * 19.0 + sin(UV2.y * 0.5) * 1.7) * beach * ripple_resolved * 0.035;
        ground = mix(ground, local_ground * (1.0 + grain * mix(0.65, 1.1, bare)), procedural);
        vec3 dpdx = dFdx(world_position);
        vec3 dpdy = dFdy(world_position);
        vec3 r1 = cross(dpdy, world_normal);
        vec3 r2 = cross(world_normal, dpdx);
        float determinant = dot(dpdx, r1);
        vec3 gradient = (dFdx(grain) * r1 + dFdy(grain) * r2) / max(abs(determinant), 0.000001) * sign(determinant);
        vec3 normal = normalize(world_normal - gradient * 0.16);
        vec3 tangent = normalize(mat3(INV_VIEW_MATRIX) * TANGENT);
        vec3 bitangent = normalize(mat3(INV_VIEW_MATRIX) * BINORMAL);
        vec3 macro_normal = vec3(dot(normal, tangent), dot(normal, bitangent), dot(normal, world_normal));
        relief = normalize(vec3(relief.xy + macro_normal.xy * procedural, relief.z));

    }

    // Source imagery carries sunlit snow close to display white. Treating that value as diffuse
    // reflectance and lighting it a second time erased every Alpine ridge, so restore the headroom
    // real snow leaves for direct illumination and blue-sky shadow.
    ground = mix(ground, vec3(snow_albedo * 0.82, snow_albedo * 0.88, snow_albedo * 0.94), snow);

    vec3 swell = vec3(0.0, 0.0, 1.0);
    float wave_variance = 0.0;

    float swell_fade = 1.0 - smoothstep(swell_near, swell_far, footprint);

    if (sea > 0.001 && swell_fade > 0.001) {

        vec2 wave_drift = vec2(TIME * wave_speed, TIME * wave_speed * 0.63);

        vec3 first = texture(wave_normal, UV2 / wave_tile + wave_drift).rgb * 2.0 - 1.0;
        vec3 second = texture(wave_normal, vec2(-UV2.y, UV2.x) / (wave_tile * 0.25) - wave_drift * 1.7).rgb * 2.0 - 1.0;

        vec3 long_wave = texture(wave_normal, UV2 / (wave_tile * 4.0) + wave_drift * 0.125).rgb * 2.0 - 1.0;
        float fine_resolved = 1.0 - smoothstep(0.6, 4.0, footprint);
        float chop_resolved = 1.0 - smoothstep(3.0, 16.0, footprint);
        swell = normalize(vec3(first.xy * chop_resolved + second.xy * 0.6 * fine_resolved + long_wave.xy * 0.7, 1.0));
        wave_variance = 0.022 * (1.0 - fine_resolved) + 0.016 * (1.0 - chop_resolved);

    }

    vec4 geometric_wave = ocean_wave(fixed_position, max(footprint, mesh_spacing));
    vec3 wave_gradient = vec3(terrain_rotation.x * geometric_wave.y + terrain_rotation.y * geometric_wave.w,
        geometric_wave.z, -terrain_rotation.y * geometric_wave.y + terrain_rotation.x * geometric_wave.w);
    vec2 wave_slope = vec2(dot(wave_gradient, normalize(mat3(INV_VIEW_MATRIX) * TANGENT)),
        dot(wave_gradient, normalize(mat3(INV_VIEW_MATRIX) * BINORMAL)));
    swell.xy -= wave_slope * ocean_room * procedural / max(wave_relief, 0.01);
    vec3 impact_offset = radial - water_impact.xyz;
    float impact_distance = length(impact_offset);
    float impact = exp(-impact_distance / max(water_impact.w * 4.0, 1.0)) * impact_strength;
    swell.xy += vec2(dot(impact_offset, normalize(mat3(INV_VIEW_MATRIX) * TANGENT)),
        dot(impact_offset, normalize(mat3(INV_VIEW_MATRIX) * BINORMAL))) / max(impact_distance, 1.0)
        * cos(impact_distance * 0.7 - environment_time * 5.0) * impact * 0.4;

    float sounding_step = max(fwidth(depth), 0.0001);

    float shelf = 1.0 - smoothstep(shallow_depth * 0.5, shallow_depth * 3.0, sounding_step);

    float shallow = exp(-depth / shallow_depth) * shelf;

    float shoals = landscape_noise(habitat_position / 420.0);
    vec3 shelf_colour = mix(shallow_colour, vec3(0.035, 0.105, 0.085), coastal * 0.6);
    vec3 water = mix(deep_colour, shelf_colour * mix(0.75, 1.18, shoals), shallow * shallow);

    float land_relief = detail_relief * detail * (1.0 - snow * 0.45);
    float sea_relief = wave_relief * swell_fade;

    vec3 perturbed = mix(relief * vec3(land_relief, land_relief, 1.0), swell * vec3(sea_relief, sea_relief, 1.0), sea);

    NORMAL_MAP = normalize(perturbed) * 0.5 + 0.5;
    NORMAL_MAP_DEPTH = 1.0;

    float lit = 1.0;
    if (dot(up, sun_direction) > 0.0) {

        vec2 local_shadow = local_cloud_light(world_position);
        float opacity = local_shadow.x;
        if (local_shadow.y < 0.999) { opacity = mix(overcast(radial, up), opacity, local_shadow.y); }
        lit -= cloud_shadow * opacity;

    }

    // Cloud attenuation belongs to direct illumination, not the material reflectance.
    cloud_light = lit;
    ALBEDO = mix(ground, water, sea);

    METALLIC = 0.0;

    float swell_roughness = sqrt(pow(mix(sea_roughness_far, sea_roughness_near, swell_fade), 2.0) + wave_variance);
    vec3 dn_dx = dFdx(perturbed);
    vec3 dn_dy = dFdy(perturbed);
    swell_roughness = sqrt(min(swell_roughness * swell_roughness + 0.35 * (dot(dn_dx, dn_dx) + dot(dn_dy, dn_dy)), 0.36));

    float land_roughness = mix(0.86, 0.68, bare);

    land_roughness = mix(land_roughness, 0.90, climate.b);
    land_roughness = mix(land_roughness, 0.76, snow);

    land_roughness = mix(land_roughness, clamp(close_roughness, 0.55, 1.0), close_detail);
    land_roughness = mix(land_roughness, 0.58, damp * (1.0 - beach));
    land_roughness = mix(land_roughness, 0.30, wet_sand);
    AO = mix(1.0, cavity, close_detail * 0.75);
    AO_LIGHT_AFFECT = 0.0;
    ROUGHNESS = mix(land_roughness, swell_roughness, sea);
    SPECULAR = mix(0.38, 0.25, sea);

    float night = (1.0 - smoothstep(night_dark, night_dusk, dot(up, sun_direction)));

    float daylight = max(dot(up, sun_direction), 0.0);

    // Half the sky for a flat face, less for one tipped over, and none of it at night.
    float dome = 0.5 + 0.5 * clamp(dot(world_normal, up), 0.0, 1.0);

    vec3 lights = max(texture(night_map, UV).rgb - night_floor, vec3(0.0)) / (1.0 - night_floor);
    float ascent = smoothstep(350.0, 1800.0, camera_altitude);
    if (night * ascent > 0.001) {

        vec2 district = atan(UV * 2.0 - 1.0) * planet_radius / 128.0;
        vec2 block = floor(district);
        vec2 lamp = vec2(landscape_hash(vec3(block, 7.0)), landscape_hash(vec3(block, 19.0)));
        float density = smoothstep(0.015, 0.25, dot(lights, vec3(0.2126, 0.7152, 0.0722)));
        float ward = landscape_noise(fixed_position / 650.0);
        float occupied = step(lamp.y, density * smoothstep(0.20, 0.65, ward + density * 0.30));
        lamp = lamp.x < 0.5 ? vec2(0.10, lamp.y) : vec2(lamp.y, 0.10);
        float radius = max(0.020, footprint / 128.0 * 0.65);
        float point = exp(-dot(fract(district) - lamp, fract(district) - lamp) / (radius * radius));
        point *= 0.008 / (radius * radius) * occupied;
        float pattern = mix(point, 0.025 * density, smoothstep(24.0, 100.0, footprint));
        vec3 warmth = mix(vec3(1.0, 0.48, 0.16), vec3(1.0, 0.85, 0.60), lamp.x);
        lights = mix(lights * warmth * pattern * 32.0, lights, smoothstep(160.0, 500.0, footprint));

    }
    lights *= ascent;

    float sky_day = smoothstep(-0.08, 0.22, dot(up, sun_direction));
    vec3 sky_irradiance = mix(vec3(0.52, 0.68, 1.0), vec3(0.78, 0.82, 0.88), 1.0 - lit)
        * sky_fill * sky_day * dome * AO;

    // The environment cubemap supplies the reflected sky to the sea. This small Fresnel fill keeps
    // the horizon physically readable while that low-resolution probe is filtering its brightest
    // values, without painting a second sun glitter over the PBR highlight.
    float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 5.0);
    vec3 sea_horizon = vec3(0.16, 0.29, 0.43) * fresnel * daylight * 0.18;

    EMISSION = lights * night_gain * night * lit * (1.0 - sea)
        + ALBEDO * sky_irradiance * (1.0 - sea)
        + sea_horizon * sea;

}


// Burley diffuse softens rough soil at grazing angles; a normalized GGX lobe retains water glints.
// No additional texture reads or shadow rays: reuse the cloud sample from fragment().
void light() {
    float nl = max(dot(NORMAL, LIGHT), 0.0);
    float nv = max(dot(NORMAL, VIEW), 0.001);
    vec3 half_vector = normalize(LIGHT + VIEW);
    float nh = max(dot(NORMAL, half_vector), 0.0);
    float lh = max(dot(LIGHT, half_vector), 0.0);
    float fd90 = 0.5 + 2.0 * ROUGHNESS * lh * lh;
    float diffuse = (1.0 + (fd90 - 1.0) * pow(1.0 - nl, 5.0))
        * (1.0 + (fd90 - 1.0) * pow(1.0 - nv, 5.0));
    vec3 radiance = LIGHT_COLOR * ATTENUATION * cloud_light;
    DIFFUSE_LIGHT += radiance * nl * diffuse / PI;
    float alpha = max(ROUGHNESS * ROUGHNESS, 0.025);
    float alpha2 = alpha * alpha;
    float denominator = nh * nh * (alpha2 - 1.0) + 1.0;
    float distribution = alpha2 / (PI * denominator * denominator);
    float visibility = 0.5 / max(nl * sqrt(nv * nv * (1.0 - alpha2) + alpha2)
        + nv * sqrt(nl * nl * (1.0 - alpha2) + alpha2), 0.001);
    float fresnel = mix(0.025, 1.0, pow(1.0 - lh, 5.0));
    SPECULAR_LIGHT += radiance * nl * distribution * visibility * fresnel;
}
