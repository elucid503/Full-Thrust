shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_back;

// The sea as its own surface over the seabed. The water column is an absorbing, scattering medium
// seen through a refracting surface; the surveyed coast still decides where it is, at every LOD.

uniform sampler2D shoreline_map : filter_linear, repeat_enable;
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;
uniform sampler2D wave_detail : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D foam_detail : filter_linear_mipmap_anisotropic, repeat_enable;

uniform float camera_altitude = 0.0;
uniform vec2 terrain_rotation = vec2(1.0, 0.0);
uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0);
uniform vec3 planet_centre = vec3(0.0, 0.0, 0.0);
uniform float planet_radius = 1274200.0;
uniform bool shoreline_debug = false;
uniform float cloud_shadow : hint_range(0.0, 1.0) = 0.32;

// Inherent optical properties per metre, red to blue. Clear ocean loses red within metres and blue
// over tens; coastal water carries sediment and dissolved organics that turn it green.
uniform vec3 ocean_absorption = vec3(0.40, 0.065, 0.022);
uniform vec3 ocean_backscatter = vec3(0.0012, 0.0016, 0.0022);
uniform vec3 coastal_absorption = vec3(0.55, 0.14, 0.22);
uniform vec3 coastal_backscatter = vec3(0.008, 0.012, 0.011);
uniform float coastal_depth = 30.0;

// Light passing through a crest facing away from the sun.
uniform vec3 crest_colour : source_color = vec3(0.10, 0.42, 0.36);
uniform float crest_strength = 0.12;

uniform float refraction = 0.05;

// The baked spectrum at two tile sizes, each read twice at different drift so the pattern evolves
// rather than slides. Both tiles divide the lattice's 4096 m period, so patches meet without seams.
uniform vec2 detail_tiles = vec2(4.0, 64.0);
uniform vec4 detail_drift_near = vec4(0.0);
uniform vec4 detail_drift_far = vec4(0.0);
uniform float detail_gain = 1.0;

// Mean square slopes: each detail layer's share, and what lies below the finest one.
uniform float layer_variance = 0.02;
uniform float micro_variance = 0.01;

// Fraction of the sea surface breaking at this wind.
uniform float whitecaps = 0.01;
uniform float foam_tile = 8.0;
uniform vec2 foam_drift = vec2(0.0);

// Vessel disturbances in scene space: segment ends with radius and kind, then age, strength,
// height scale and speed or ripple phase.
uniform int wake_count = 0;
uniform vec4 wake_start[32];
uniform vec4 wake_end[32];
uniform vec4 wake_state[32];
uniform vec4 wake_bounds = vec4(0.0, 0.0, 0.0, -1.0);

varying vec3 rest_position;
varying vec3 world_position;
varying float cloud_light;
varying float reflectance;

instance uniform float lod_morph : instance_index(0) = 0.0;
instance uniform float mesh_spacing : instance_index(1) = 1.0;

#include "res://Shaders/Clouds/CloudField.gdshaderinc"
#include "res://Shaders/Ground/OceanField.gdshaderinc"
#include "res://Shaders/Clouds/CloudShadowSampling.gdshaderinc"
#include "res://Shaders/Ground/LandscapeNoise.gdshaderinc"
#include "res://Shaders/Ground/CoastalLandforms.gdshaderinc"
#include "res://Shaders/Ground/ShorelineSampling.gdshaderinc"

vec3 to_fixed(vec3 v) {

    return vec3(terrain_rotation.x * v.x - terrain_rotation.y * v.z, v.y, terrain_rotation.y * v.x + terrain_rotation.x * v.z);

}

vec3 from_fixed(vec3 v) {

    return vec3(terrain_rotation.x * v.x + terrain_rotation.y * v.z, v.y, -terrain_rotation.y * v.x + terrain_rotation.x * v.z);

}

// Height, horizontal gradient and foam cover from every vessel disturbance near p.
float wake_field(vec3 p, vec3 up, out vec3 gradient, out float foam) {

    gradient = vec3(0.0);
    foam = 0.0;
    float height = 0.0;
    if (wake_count == 0 || distance(p, wake_bounds.xyz) > wake_bounds.w) { return 0.0; }
    for (int i = 0; i < wake_count; i++) {

        vec3 start = wake_start[i].xyz;
        vec3 span = wake_end[i].xyz - start;
        float along = clamp(dot(p - start, span) / max(dot(span, span), 0.0001), 0.0, 1.0);
        vec3 offset = p - start - span * along;
        offset -= up * dot(offset, up);
        float d = length(offset);
        vec3 outward = offset / max(d, 0.001);
        float radius = max(wake_start[i].w, 0.1);
        float kind = wake_end[i].w;
        vec4 state = wake_state[i];

        if (kind < 0.5) {

            // Hull collar: water churned where the hull pierces the surface.
            foam += state.y * (1.0 - smoothstep(radius, radius * 1.35 + 0.8, d));

        } else if (kind < 1.5) {

            // Wake: a foam trail that spreads (radius grows on the CPU) and thins as it ages.
            foam += state.y * exp(-state.x / 45.0) * (1.0 - smoothstep(radius * 0.4, radius, d));

        } else if (kind < 2.5) {

            // Splash: a ring wave running outward from the entry point.
            float front = radius + state.w * state.x;
            float band = max(radius * 0.5, 1.0) + state.x * 0.35;
            float wavelength = max(band * 1.6, 0.5);
            float k = TAU / wavelength;
            float away = d - front;
            float envelope = state.z * exp(-state.x / 9.0) * exp(-away * away / (band * band)) / (1.0 + front * 0.1);
            float ripple = sin(k * away);
            height += envelope * ripple;
            gradient += outward * envelope * (k * cos(k * away) - 2.0 * away / (band * band) * ripple);
            float crown = exp(-state.x / 5.0) * (1.0 - smoothstep(front * 0.6, front, d));
            float ring = exp(-state.x / 14.0) * exp(-away * away / (band * band * 0.5));
            foam += state.y * max(crown, ring * 0.8);

        } else {

            // Jet: exhaust pressure pushes a crater and drives ripples out across the rim.
            float crater = exp(-d * d / (radius * radius));
            height -= state.z * crater;
            gradient += outward * (state.z * 2.0 * d / (radius * radius) * crater);
            float k = TAU / max(radius * 0.8, 0.3);
            float rim = 0.25 * state.z * exp(-d / (radius * 3.0)) * (1.0 - crater);
            height += rim * sin(k * d - state.w);
            gradient += outward * (rim * k * cos(k * d - state.w));
            foam += state.y * (1.0 - smoothstep(radius * 0.4, radius * 1.8, d));

        }

    }
    return height;

}

void vertex() {

    VERTEX += CUSTOM0.xyz * lod_morph;
    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    vec3 radial = world - planet_centre;
    vec3 up = normalize(radial);
    rest_position = to_fixed(radial);
    vec3 shift;
    mat3 jacobian;
    vec4 wave = ocean_wave(rest_position, mesh_spacing * (1.0 + lod_morph), CUSTOM0.w, shift, jacobian);
    float local = 1.0 - smoothstep(20000.0, 25000.0, camera_altitude);
    vec3 wake_gradient;
    float wake_foam;
    float wake = wake_field(world, up, wake_gradient, wake_foam);
    vec3 displacement = (from_fixed(shift) + up * wave.x) * local + up * wake;
    VERTEX += inverse(mat3(MODEL_MATRIX)) * displacement;
    world_position = world + displacement;

}

float scene_distance(vec2 uv, float raw, mat4 inverse_projection) {

    vec4 view = inverse_projection * vec4(uv * 2.0 - 1.0, raw, 1.0);
    return length(view.xyz / view.w);

}

// Screen-space reflection of whatever stands over the water: a marched ray refined by bisection.
vec4 reflection(vec3 origin, vec3 normal, mat4 projection, mat4 inverse_projection) {

    vec3 ray = reflect(normalize(origin), normal);
    if (ray.z > 0.3) { return vec4(0.0); }
    float stride = 0.4 + length(origin) * 0.01;
    vec3 position = origin + normal * 0.05;
    for (int march = 0; march < 28; march++) {

        vec3 previous = position;
        position += ray * stride;
        vec4 clip = projection * vec4(position, 1.0);
        if (clip.w <= 0.0) { break; }
        vec2 uv = clip.xy / clip.w * 0.5 + 0.5;
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) { break; }
        float raw = texture(depth_texture, uv).r;
        vec4 scene = inverse_projection * vec4(uv * 2.0 - 1.0, raw, 1.0);
        float behind = -position.z + scene.z / scene.w;
        if (raw > 0.0 && behind > 0.0 && behind < stride * 2.0 + 0.5) {

            vec3 low = previous;
            vec3 high = position;
            for (int refine = 0; refine < 5; refine++) {

                vec3 middle = (low + high) * 0.5;
                vec4 middle_clip = projection * vec4(middle, 1.0);
                vec2 middle_uv = middle_clip.xy / middle_clip.w * 0.5 + 0.5;
                vec4 middle_scene = inverse_projection * vec4(middle_uv * 2.0 - 1.0, texture(depth_texture, middle_uv).r, 1.0);
                if (-middle.z > -middle_scene.z / middle_scene.w) { high = middle; } else { low = middle; }

            }
            vec4 hit = projection * vec4(high, 1.0);
            uv = hit.xy / hit.w * 0.5 + 0.5;
            vec2 edge = smoothstep(vec2(0.0), vec2(0.08), uv) * (1.0 - smoothstep(vec2(0.92), vec2(1.0), uv));
            float fade = edge.x * edge.y * (1.0 - float(march) / 28.0) * (1.0 - smoothstep(0.0, 0.3, ray.z));
            return vec4(textureLod(screen_texture, uv, 0.0).rgb, fade);

        }
        stride *= 1.18;

    }
    return vec4(0.0);

}

void fragment() {

    vec3 radial = world_position - planet_centre;
    vec3 up = normalize(radial);
    float coast = shoreline_height(rest_position);
    float coast_width = max(fwidth(coast), 0.02);
    float sea = 1.0 - smoothstep(-coast_width, coast_width, coast);
    float depth = max(-coast, 0.0);
    float footprint = max(max(length(dFdx(UV2)), length(dFdy(UV2))), 0.0001);

    float lit = 1.0;
    if (dot(up, sun_direction) > 0.0) {

        vec2 local_shadow = local_cloud_light(world_position);
        float opacity = local_shadow.x;
        if (local_shadow.y < 0.999) { opacity = mix(1.0 - cloud_transmittance(radial, sun_direction, 0.004), opacity, local_shadow.y); }
        lit -= cloud_shadow * opacity;

    }
    cloud_light = lit;

    // Swell: the Jacobian turns rest-frame slopes into slopes of the surface as drawn.
    vec3 shift;
    mat3 jacobian;
    vec4 wave = ocean_wave(rest_position, footprint, coast, shift, jacobian);
    vec3 swell_slope = from_fixed(inverse(transpose(jacobian)) * wave.yzw);
    float fold = determinant(jacobian);
    float reach = ocean_reach(coast);
    float crest = clamp(wave.x / max(reach, 0.02), -1.0, 1.0);
    float variance = micro_variance + ocean_lost_variance(footprint, coast);

    // Detail: slopes on the lattice, mapped through the patch's own s and t axes.
    vec3 tangent = normalize(mat3(INV_VIEW_MATRIX) * TANGENT);
    vec3 binormal = normalize(mat3(INV_VIEW_MATRIX) * BINORMAL);
    vec2 detail_slope = vec2(0.0);
    float breaking = 0.0;
    float breaking_weight = 0.0;
    for (int tier = 0; tier < 2; tier++) {

        float tile = tier == 0 ? detail_tiles.x : detail_tiles.y;
        vec4 drift = tier == 0 ? detail_drift_near : detail_drift_far;
        float lost = clamp(log2(footprint * 64.0 / tile) / 4.0, 0.0, 1.0);
        variance += layer_variance * lost;
        if (lost >= 1.0) { continue; }
        vec2 uv = UV2 / tile;
        vec4 first = texture(wave_detail, uv + drift.xy);
        vec4 second = texture(wave_detail, uv + drift.zw);
        detail_slope += (first.rg + second.rg - 1.0) * 1.4142 * detail_gain * (1.0 - lost);
        float weight = (1.0 - lost) * (tier == 0 ? 1.0 : 0.6);
        breaking += max(first.b, second.b) * weight;
        breaking_weight += weight;

    }
    vec3 detail_gradient = tangent * detail_slope.x - binormal * detail_slope.y;

    vec3 wake_gradient;
    float wake_foam;
    float wake_height = wake_field(world_position, up, wake_gradient, wake_foam);

    vec3 normal = normalize(up - swell_slope - detail_gradient - wake_gradient);
    vec3 view_normal = normalize(mat3(VIEW_MATRIX) * normal);
    vec3 dn_dx = dFdx(view_normal);
    vec3 dn_dy = dFdy(view_normal);
    variance += 0.5 * (dot(dn_dx, dn_dx) + dot(dn_dy, dn_dy));

    // Foam: whitecaps where the spectrum breaks first (swell crests break sooner), surf in the
    // shallows, a line at the waterline, and whatever the vessels have churned up.
    float resolved_breaking = breaking_weight > 0.0 ? breaking / breaking_weight : 0.0;
    float boost = 1.0 + max(1.0 - fold, 0.0) * 20.0 + max(crest, 0.0) * 0.5;
    float caps = smoothstep(1.0 - whitecaps * boost * 1.4, 1.0 - whitecaps * boost * 0.6 + 0.0001, resolved_breaking);
    caps = mix(caps, whitecaps * 0.8, 1.0 - clamp(breaking_weight, 0.0, 1.0));

    float surface_distance = length(VERTEX);
    float raw = texture(depth_texture, SCREEN_UV).r;
    float column = raw > 0.0 ? max(scene_distance(SCREEN_UV, raw, INV_PROJECTION_MATRIX) - surface_distance, 0.0) : 1e6;
    vec3 view_world = normalize(mat3(INV_VIEW_MATRIX) * VIEW);
    float cosine_up = max(dot(view_world, up), 0.02);
    float contact = (1.0 - smoothstep(0.0, 0.45, column * cosine_up)) * (1.0 - smoothstep(60.0, 300.0, surface_distance));
    float surf = (1.0 - smoothstep(0.4, 5.0, depth)) * smoothstep(0.1, 0.8, crest) * step(0.001, reach);
    float cover = clamp(caps + contact * 0.85 + surf * 0.75 + wake_foam, 0.0, 1.0);
    vec4 foam_sample = texture(foam_detail, UV2 / foam_tile + foam_drift);
    float pattern = foam_sample.r * 0.65 + foam_sample.g * 0.35;
    float foam = smoothstep(1.0 - cover, 1.3 - cover, pattern);
    foam = mix(foam, cover * 0.7, smoothstep(foam_tile / 128.0, foam_tile / 8.0, footprint)) * sea;

    // Refraction: bend the view of the bed, but never towards anything standing in front of the water.
    vec2 bend = (mat3(VIEW_MATRIX) * (normal - up)).xy * refraction * clamp(column * 0.3, 0.0, 1.0)
        / (1.0 + surface_distance * 0.02);
    vec2 refracted_uv = clamp(SCREEN_UV + bend, vec2(0.0), vec2(1.0));
    float refracted_raw = texture(depth_texture, refracted_uv).r;
    float refracted = refracted_raw > 0.0 ? scene_distance(refracted_uv, refracted_raw, INV_PROJECTION_MATRIX) : 1e7;
    if (refracted < surface_distance) {

        refracted_uv = SCREEN_UV;
        refracted_raw = raw;
        refracted = surface_distance + column;

    }
    float path = refracted_raw > 0.0 ? max(refracted - surface_distance, 0.0) : 1e6;
    // Far off the bed is a coarse patch; the surveyed depth holds the colour steady at every LOD.
    path = mix(path, (depth + max(wave.x, 0.0)) / cosine_up, smoothstep(250.0, 2000.0, surface_distance));

    float coastal = exp(-depth / coastal_depth);
    vec3 absorption = mix(ocean_absorption, coastal_absorption, coastal);
    vec3 backscatter = mix(ocean_backscatter, coastal_backscatter, coastal);
    // The bed was lit as if in air: light has to reach it through the column and come back out.
    vec3 transmittance = exp(-(absorption + backscatter * 20.0) * path - (absorption + backscatter) * 1.2 * path * cosine_up);
    vec3 bed = textureLod(screen_texture, refracted_uv, clamp(log2(1.0 + path * 0.35), 0.0, 4.0)).rgb;
    vec3 deep_reflectance = 0.33 * backscatter / absorption;

    float nv = clamp(dot(view_normal, VIEW), 0.0, 1.0);
    float fresnel = 0.02 + 0.98 * pow(1.0 - nv, 5.0);
    float daylight = max(dot(up, sun_direction), 0.0);

    vec4 mirrored = surface_distance < 600.0 && foam < 0.9
        ? reflection(VERTEX, view_normal, PROJECTION_MATRIX, INV_PROJECTION_MATRIX) : vec4(0.0);
    mirrored.a *= 1.0 - smoothstep(300.0, 600.0, surface_distance);

    float behind_crest = pow(clamp(dot(-view_world, sun_direction), 0.0, 1.0), 4.0);
    vec3 crest_light = crest_colour * crest_strength * behind_crest * max(crest, 0.0)
        * smoothstep(-0.05, 0.25, dot(up, sun_direction)) * lit;

    // The environment cubemap filters its brightest sky away; a small Fresnel fill keeps the horizon readable.
    vec3 horizon = vec3(0.16, 0.29, 0.43) * fresnel * daylight * 0.18;

    NORMAL = view_normal;
    ALBEDO = mix(deep_reflectance * (1.0 - transmittance) * (1.0 - fresnel), vec3(0.8, 0.82, 0.82), foam);
    EMISSION = (bed * transmittance * (1.0 - fresnel) + crest_light) * (1.0 - foam)
        + mirrored.rgb * fresnel * mirrored.a + horizon * (1.0 - foam);
    float roughness = sqrt(sqrt(variance));
    ROUGHNESS = mix(clamp(roughness, 0.05, 0.8), 0.9, foam);
    METALLIC = 0.0;
    // Specular 0.354 is water's 2% reflectance at normal incidence; a screen-space hit replaces the sky.
    SPECULAR = 0.354 * sqrt(1.0 - mirrored.a) * (1.0 - foam);
    reflectance = 0.02 * (1.0 - mirrored.a) * (1.0 - foam);
    ALPHA = sea;

    // Masked-off water would still write depth and clip the plume over dry land. Last, so every
    // derivative above is taken with the whole quad alive.
    if (sea < 0.004 && !shoreline_debug) { discard; }

    if (shoreline_debug) {

        ALBEDO = vec3(0.0);
        EMISSION = vec3(sea);
        ROUGHNESS = 1.0;
        SPECULAR = 0.0;
        reflectance = 0.0;
        ALPHA = 1.0;

    }

}

void light() {

    float nl = max(dot(NORMAL, LIGHT), 0.0);
    vec3 radiance = LIGHT_COLOR * ATTENUATION * cloud_light;
    DIFFUSE_LIGHT += radiance * nl / PI;
    float nv = max(dot(NORMAL, VIEW), 0.001);
    vec3 half_vector = normalize(LIGHT + VIEW);
    float nh = max(dot(NORMAL, half_vector), 0.0);
    float lh = max(dot(LIGHT, half_vector), 0.0);
    float alpha = max(ROUGHNESS * ROUGHNESS, 0.0025);
    float alpha2 = alpha * alpha;
    float denominator = nh * nh * (alpha2 - 1.0) + 1.0;
    float distribution = alpha2 / (PI * denominator * denominator);
    float visibility = 0.5 / max(nl * sqrt(nv * nv * (1.0 - alpha2) + alpha2)
        + nv * sqrt(nl * nl * (1.0 - alpha2) + alpha2), 0.001);
    float fresnel = reflectance + (1.0 - reflectance) * pow(1.0 - lh, 5.0);
    SPECULAR_LIGHT += radiance * nl * distribution * visibility * fresnel * step(0.0001, reflectance);

}
