shader_type spatial;
render_mode specular_schlick_ggx;

// One face of the cube-sphere. The imagery is the colour and everything finer than a texel of it is
// synthesised: a detail material at a tile size chosen from the viewing distance, so the ground has
// grain at four hundred kilometres and at four metres and never resolves into a flat photograph.

uniform sampler2D surface_map : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D climate_map : filter_linear_mipmap_anisotropic;
uniform sampler2D night_map : source_color, filter_linear_mipmap_anisotropic;


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

// And a third, wide enough that it is still resolved from a few kilometres up. Without it the
// ground between the imagery's four-hundred-metre texels is one flat colour with grain on it, which
// is exactly what an aerial view of it looked like.
uniform float detail_macro = 1024.0;

// How much of the relief comes from the coarse tile. The fine one is grain; the coarse one is the
// shape of the ground between one vertex of the mesh and the next.
uniform float detail_balance : hint_range(0.0, 1.0) = 0.62;

uniform float macro_contrast : hint_range(0.0, 1.0) = 0.38;

// How much of the detail material's own colour is allowed through. The photograph is still the
// ground's colour; this is the difference between rock and soil at a range no photograph resolves.
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
uniform float foam_depth = 0.8;

// How far the surf runs out from the waterline. Metres of ground, and the band comes out twice
// this wide, because the sounding is carried as its own square root and the depth under a linear
// interpolation of that rises as the square of the distance. The depth to cut it at is read off the
// mesh rather than assumed - the mesh is the only thing that knows how fast its sea floor drops
// away here - but the width is this and nothing else, at every level of the tree.
uniform float foam_reach = 7.0;
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
uniform float cloud_shadow : hint_range(0.0, 1.0) = 0.48;

uniform float snow_albedo : hint_range(0.4, 1.0) = 0.72;

// The imagery is a satellite average over four hundred metres, taken through the whole atmosphere:
// it is darker and flatter than the ground looks from a hundred metres up. Lifted as the detail
// engages, which is exactly where the difference starts to show.
uniform float close_lift : hint_range(0.5, 1.0) = 1.0;

// A lit surface under air is filled by the whole sky as well as by the sun, and without it every
// slope facing away from the sun goes to black.
uniform float sky_fill : hint_range(0.0, 0.5) = 0.055;

// The lights are compressed into a JPEG, and a JPEG has no true black: without a floor taken off
// it the whole night side carries a flat warm haze rather than only its cities.
uniform float night_floor = 0.035;

uniform float night_gain = 1.1;
uniform float night_dusk = -0.02;
uniform float night_dark = -0.20;

varying vec3 world_position;
instance uniform float lod_morph = 0.0;

void vertex() {

    VERTEX += CUSTOM0.xyz * lod_morph;
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

}

// Offset the shadow sample sunward by deck height over the cosine of the solar zenith angle, so a
// cloud shades the ground it is actually over rather than the ground directly beneath it.
#include "res://Shaders/CloudField.gdshaderinc"

float overcast(vec3 radial, vec3 up) {

    float cosine = max(dot(sun_direction, up), 0.18);
    vec3 first = radial + sun_direction * max(base_radius + 600.0 - length(radial), 0.0) / cosine;
    vec3 second = radial + sun_direction * max(base_radius + 1500.0 - length(radial), 0.0) / cosine;
    float density = density_at(first, 1200.0) + density_at(second, 1200.0);
    return 1.0 - exp(-density * 2.8 / cosine);

}

void fragment() {

    vec3 radial = world_position - planet_centre;

    float height = length(radial);

    vec3 up = radial / height;

    float elevation = height - planet_radius;

    vec4 climate = texture(climate_map, UV);

    vec3 ground = texture(surface_map, UV).rgb;

    // Depth arrives with the mesh rather than off the world position, which at a million metres
    // from the planet's centre has lost the last decimetre and would fray the shoreline.
    float depth = COLOR.r * COLOR.r * 1600.0;

    // Widened to whatever a pixel of the mesh actually spans, so the coastline is one antialiased
    // line at every altitude rather than a stair at some of them.
    float sea = smoothstep(0.0, max(0.004, fwidth(COLOR.r)), COLOR.r);

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
    float vegetation = climate.b * (1.0 - bare) * (1.0 - snow);
    vec3 rock_tint = vec3(0.17, 0.15, 0.125);
    ground = mix(ground, mix(ground * 0.65, rock_tint, 0.42), bare * 0.7);
    ground *= mix(vec3(1.0), vec3(0.72, 0.86, 0.65), vegetation * 0.5);

    vec3 relief = vec3(0.0, 0.0, 1.0);

    if (macro > 0.001) {

        // Broad blotching first: the same material read at a few hundred metres a tile, which is
        // the scale the imagery has already smoothed away.
        vec3 wide = mix(texture(soil_colour, wide_uv).rgb, texture(rock_colour, wide_uv).rgb, bare);

        float blotch = dot(wide, vec3(0.2126, 0.7152, 0.0722));

        ground *= 1.0 + macro_contrast * macro * (1.0 - snow * 0.6) * (blotch - 0.5) * 2.0;

    }

    if (detail > 0.001) {

        vec3 near_grain = mix(texture(soil_colour, near_uv).rgb, texture(rock_colour, near_uv).rgb, bare);
        vec3 far_grain = mix(texture(soil_colour, far_uv).rgb, texture(rock_colour, far_uv).rgb, bare);

        vec3 near_bump = mix(texture(soil_normal, near_uv).rgb, texture(rock_normal, near_uv).rgb, bare);
        vec3 far_bump = mix(texture(soil_normal, far_uv).rgb, texture(rock_normal, far_uv).rgb, bare);

        relief = mix(near_bump, far_bump, detail_balance) * 2.0 - 1.0;

        vec3 material = mix(near_grain, far_grain, detail_balance);

        // The imagery is still the colour: the luminance is taken as a modulation about the
        // material's own mean, and only a fraction of its hue is let through on top of that.
        float grain = dot(material, vec3(0.2126, 0.7152, 0.0722));

        vec3 tint = material / max(grain, 0.04);

        vec3 lifted = pow(ground, vec3(close_lift)) * (1.0 + detail_contrast * (1.0 - snow * 0.6) * (grain - 0.5) * 2.0);

        lifted *= mix(vec3(1.0), tint, material_hue * (1.0 - snow) * (1.0 - sea));

        ground = mix(ground, lifted, detail);

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

    // Depth arrives as a vertex attribute, so a coarse patch spreads a metre of water over
    // kilometres of ground: a band cut at a fixed depth is as wide as whatever patch happens to be
    // under it, which is why the waterline swelled and shrank as the vehicle climbed. Cut it at the
    // depth this mesh's own sea floor reaches a fixed distance out instead, and the surf is the same
    // stretch of ground at every level of the tree.
    float sounding_step = max(fwidth(depth), 0.0001);

    float sea_floor = sounding_step / max(footprint, 0.001);

    float surf = max(min(foam_depth, foam_reach * sea_floor), 0.0001);

    // And gone once that stretch is a couple of pixels, rather than left to alias into a flicker.
    // Surf is a thing seen from the beach: carrying it to the horizon is what made every coastline
    // on the planet a painted ribbon.
    float shore = 1.0 - smoothstep(foam_reach * 0.5, foam_reach * 3.0, footprint);

    float shelf = 1.0 - smoothstep(shallow_depth * 0.5, shallow_depth * 3.0, sounding_step);

    float shallow = exp(-depth / shallow_depth) * shelf;

    // The phase runs across the band rather than over a fixed depth, so the surf sweeps up the beach
    // the same way whatever the band is cut at, and carries a slow term off the face coordinate so
    // that every coastline on the planet does not brighten and fade in step.
    float shore_pulse = 0.5 + 0.5 * sin(depth / surf * 5.0 - TIME * 1.5 + fract(UV.x * 37.0 + UV.y * 29.0) * TAU);

    float foam = sea * (1.0 - smoothstep(0.0, surf, depth)) * shore * shore_pulse;

    vec3 water = mix(deep_colour, shallow_colour, shallow * shallow);

    float land_relief = detail_relief * detail * (1.0 - snow * 0.45);
    float sea_relief = wave_relief * swell_fade;

    vec3 perturbed = mix(relief * vec3(land_relief, land_relief, 1.0), swell * vec3(sea_relief, sea_relief, 1.0), sea);

    NORMAL_MAP = normalize(perturbed) * 0.5 + 0.5;
    NORMAL_MAP_DEPTH = 1.0;

    float lit = 1.0 - cloud_shadow * smoothstep(0.04, 0.92, overcast(radial, up));

    ALBEDO = (mix(ground, water, sea) + vec3(foam * 0.07)) * lit;

    METALLIC = 0.0;

    float swell_roughness = sqrt(pow(mix(sea_roughness_far, sea_roughness_near, swell_fade), 2.0) + wave_variance);
    vec3 dn_dx = dFdx(perturbed);
    vec3 dn_dy = dFdy(perturbed);
    swell_roughness = sqrt(min(swell_roughness * swell_roughness + 0.35 * (dot(dn_dx, dn_dx) + dot(dn_dy, dn_dy)), 0.36));

    float land_roughness = mix(0.86, 0.68, bare);

    land_roughness = mix(land_roughness, 0.90, climate.b);
    land_roughness = mix(land_roughness, 0.76, snow);

    ROUGHNESS = mix(land_roughness, swell_roughness, sea);
    SPECULAR = mix(0.46, 0.25, sea) * lit;

    float night = (1.0 - smoothstep(night_dark, night_dusk, dot(up, sun_direction)));

    float daylight = max(dot(up, sun_direction), 0.0);

    // Half the sky for a flat face, less for one tipped over, and none of it at night.
    float dome = 0.5 + 0.5 * clamp(dot(world_normal, up), 0.0, 1.0);

    vec3 lights = max(texture(night_map, UV).rgb - night_floor, vec3(0.0)) / (1.0 - night_floor);

    vec3 sky_irradiance = vec3(0.52, 0.68, 1.0) * sky_fill * daylight * dome;

    // The environment cubemap supplies the reflected sky to the sea. This small Fresnel fill keeps
    // the horizon physically readable while that low-resolution probe is filtering its brightest
    // values, without painting a second sun glitter over the PBR highlight.
    float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 5.0);
    vec3 sea_horizon = vec3(0.16, 0.29, 0.43) * fresnel * daylight * 0.18;

    EMISSION = lights * night_gain * night * lit * (1.0 - sea * 0.85)
        + ALBEDO * sky_irradiance * (1.0 - sea)
        + sea_horizon * sea;

}
