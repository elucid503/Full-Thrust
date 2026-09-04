shader_type spatial;
render_mode specular_schlick_ggx;

// One face of the cube-sphere. The imagery is the colour and everything finer than a texel of it is
// synthesised: a detail material at a tile size chosen from the viewing distance, so the ground has
// grain at four hundred kilometres and at four metres and never resolves into a flat photograph.

uniform sampler2D surface_map : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D climate_map : filter_linear_mipmap_anisotropic;
uniform sampler2D night_map : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D cloud_map : filter_linear_mipmap;

uniform sampler2D rock_colour : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_colour : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D wave_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;

uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0);
uniform vec3 planet_centre = vec3(0.0, 0.0, 0.0);

uniform float planet_radius = 1274200.0;
uniform float cloud_spin = 0.0;

// Two fixed tile sizes in metres rather than one chosen from the viewing distance. A tile that
// tracks the camera keeps its size on screen, which sounds right and is not: the pattern then slides
// through scale as the camera moves and reads as grain. Fixed tiles let the mip chain do what it is
// for - the fine one averages itself away with distance and the coarse one takes over - which is
// also what ground actually does to an eye moving away from it.
uniform float detail_fine = 2.6;
uniform float detail_coarse = 34.0;

// And a third, wide enough that it is still resolved from a few kilometres up. Without it the
// ground between the imagery's four-hundred-metre texels is one flat colour with grain on it, which
// is exactly what an aerial view of it looked like.
uniform float detail_macro = 380.0;

// How much of the relief comes from the coarse tile. The fine one is grain; the coarse one is the
// shape of the ground between one vertex of the mesh and the next.
uniform float detail_balance : hint_range(0.0, 1.0) = 0.55;

uniform float macro_contrast : hint_range(0.0, 1.0) = 0.27;

// How much of the detail material's own colour is allowed through. The photograph is still the
// ground's colour; this is the difference between rock and soil at a range no photograph resolves.
uniform float material_hue : hint_range(0.0, 1.0) = 0.30;

// Detail is worth nothing once a tile is under a pixel; past here the imagery carries it alone.
uniform float detail_near = 18000.0;
uniform float detail_far = 46000.0;

uniform float detail_relief : hint_range(0.0, 4.0) = 1.15;
uniform float detail_contrast : hint_range(0.0, 1.0) = 0.30;

// Slope, as one less the cosine to the local vertical, over which soil gives way to bare rock.
uniform float rock_slope_low = 0.05;
uniform float rock_slope_high = 0.32;

uniform float snow_line = 2600.0;
uniform float snow_band = 900.0;

uniform vec3 shallow_colour : source_color = vec3(0.055, 0.208, 0.235);
uniform vec3 deep_colour : source_color = vec3(0.004, 0.017, 0.045);

uniform float shallow_depth = 260.0;
uniform float foam_depth = 2.5;
uniform float wave_tile = 11.0;
uniform float wave_speed = 0.045;
uniform float wave_relief : hint_range(0.0, 3.0) = 0.85;

// A mirror-smooth sea aliases into sparkle the moment one wave falls under a pixel. The waves are
// faded out over the same range the roughness comes up over, which is the same statement made twice:
// what the normal map stops resolving is exactly what the roughness has to stand in for.
uniform float sea_roughness_near = 0.075;
uniform float sea_roughness_far = 0.30;

uniform float swell_near = 500.0;
uniform float swell_far = 6000.0;

uniform float cloud_altitude = 9000.0;
uniform float cloud_shadow : hint_range(0.0, 1.0) = 0.55;

// The imagery is a satellite average over four hundred metres, taken through the whole atmosphere:
// it is darker and flatter than the ground looks from a hundred metres up. Lifted as the detail
// engages, which is exactly where the difference starts to show.
uniform float close_lift : hint_range(0.5, 1.0) = 0.83;

// A lit surface under air is filled by the whole sky as well as by the sun, and without it every
// slope facing away from the sun goes to black.
uniform float sky_fill : hint_range(0.0, 0.5) = 0.11;

// The lights are compressed into a JPEG, and a JPEG has no true black: without a floor taken off
// it the whole night side carries a flat warm haze rather than only its cities.
uniform float night_floor = 0.035;

uniform float night_gain = 1.1;
uniform float night_dusk = -0.02;
uniform float night_dark = -0.20;

varying vec3 world_position;

void vertex() {

    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

}

// The maps are laid out on the ground and the ground turns, so a direction has to be read back into
// the frame they were drawn in before it can index one.
vec3 unspin(vec3 direction) {

    float sine = sin(-cloud_spin);
    float cosine = cos(-cloud_spin);

    return vec3(direction.x * cosine + direction.z * sine, direction.y, -direction.x * sine + direction.z * cosine);

}

vec2 equirect(vec3 direction) {

    vec3 fixed_frame = unspin(direction);

    float longitude = atan(-fixed_frame.z, fixed_frame.x);
    float latitude = asin(clamp(fixed_frame.y, -1.0, 1.0));

    return vec2(longitude / TAU + 0.5, 0.5 - latitude / PI);

}

// Offset the shadow sample sunward by deck height over the cosine of the solar zenith angle, so a
// cloud shades the ground it is actually over rather than the ground directly beneath it.
float overcast(vec3 radial, vec3 up) {

    float overhead = max(dot(sun_direction, up), 0.20);

    vec3 slide = (sun_direction - up * dot(sun_direction, up)) * (cloud_altitude / overhead);

    return texture(cloud_map, equirect(normalize(radial + slide))).r;

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
    float depth = COLOR.r * COLOR.r * 8000.0;

    float sea = smoothstep(0.0, 0.02, COLOR.r);

    float range = length(VERTEX);

    vec2 near_uv = UV2 / detail_fine;
    vec2 far_uv = UV2 / detail_coarse;
    vec2 wide_uv = UV2 / detail_macro;

    float detail = 1.0 - smoothstep(detail_near, detail_far, range);

    // The widest tile is worth carrying much further out than the other two, because from altitude
    // it is the only thing standing between the imagery and a flat wash.
    float macro = 1.0 - smoothstep(detail_far, detail_far * 3.4, range);

    vec3 world_normal = normalize(mat3(INV_VIEW_MATRIX) * NORMAL);

    float slope = 1.0 - clamp(dot(world_normal, up), 0.0, 1.0);

    float bare = smoothstep(rock_slope_low, rock_slope_high, slope);

    float snow = max(climate.g, smoothstep(snow_line, snow_line + snow_band, elevation) * (1.0 - bare)) * (1.0 - sea);

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

    vec3 swell = vec3(0.0, 0.0, 1.0);

    float swell_fade = 1.0 - smoothstep(swell_near, swell_far, range);

    if (sea > 0.001 && swell_fade > 0.001) {

        vec2 drift = vec2(TIME * wave_speed, TIME * wave_speed * 0.63);

        vec3 first = texture(wave_normal, UV2 / wave_tile + drift).rgb * 2.0 - 1.0;
        vec3 second = texture(wave_normal, UV2 / (wave_tile * 0.43) - drift * 1.7).rgb * 2.0 - 1.0;

        swell = normalize(first + second * 0.6);

    }

    float shallow = 1.0 - clamp(depth / shallow_depth, 0.0, 1.0);
    float foam = sea * (1.0 - smoothstep(0.0, foam_depth, depth));

    vec3 water = mix(deep_colour, shallow_colour, shallow * shallow);

    float land_relief = detail_relief * detail * (1.0 - snow * 0.45);
    float sea_relief = wave_relief * swell_fade;

    vec3 perturbed = mix(relief * vec3(land_relief, land_relief, 1.0), swell * vec3(sea_relief, sea_relief, 1.0), sea);

    NORMAL_MAP = normalize(perturbed) * 0.5 + 0.5;
    NORMAL_MAP_DEPTH = 1.0;

    float lit = 1.0 - cloud_shadow * smoothstep(0.10, 0.70, overcast(radial, up));

    ALBEDO = (mix(ground, water, sea) + vec3(foam * 0.4)) * lit;

    METALLIC = 0.0;

    float swell_roughness = mix(sea_roughness_far, sea_roughness_near, swell_fade);

    ROUGHNESS = mix(mix(mix(0.93, 0.80, climate.b), 0.45, snow), swell_roughness, sea);
    SPECULAR = mix(0.5, 0.32, sea);

    float night = smoothstep(night_dusk, night_dark, dot(up, sun_direction));

    float daylight = max(dot(up, sun_direction), 0.0);

    // Half the sky for a flat face, less for one tipped over, and none of it at night.
    float dome = 0.5 + 0.5 * clamp(dot(world_normal, up), 0.0, 1.0);

    vec3 lights = max(texture(night_map, UV).rgb - night_floor, vec3(0.0)) / (1.0 - night_floor);

    EMISSION = lights * night_gain * night * lit * (1.0 - sea * 0.85)
        + ALBEDO * sky_fill * daylight * dome;

}
