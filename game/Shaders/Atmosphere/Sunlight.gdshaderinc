// Direct sunlight at a point; mirrors Sunlight.cs. The includer declares planet_radius and sun_direction.

#ifndef SUNLIGHT_INCLUDED
#define SUNLIGHT_INCLUDED

#ifndef CLOUD_COMPUTE
uniform sampler2D sun_optical_depth : filter_linear, repeat_disable;

uniform float atmosphere_radius = 1334200.0;

uniform float rayleigh_height = 5600.0;
uniform float mie_height = 1200.0;
uniform float ozone_half_width = 15000.0;

uniform vec3 rayleigh_coefficients = vec3(5.8e-6, 1.35e-5, 3.31e-5);
uniform float mie_coefficient = 7.0e-6;
uniform vec3 ozone_coefficients = vec3(0.650e-6, 1.881e-6, 0.085e-6);

// Linear radiance above the air, in the units Godot hands a directional light's LIGHT_COLOR.
uniform vec3 sun_radiance = vec3(3.3, 3.3, 3.3);
#endif

const float solar_radius = 0.00465;

vec3 sun_optical_depth_at(float radial_distance, float solar_cosine) {

    vec2 lookup_uv = vec2(0.5 + 0.5 * sign(solar_cosine) * sqrt(abs(solar_cosine)),
        sqrt(clamp((radial_distance - planet_radius) / (atmosphere_radius - planet_radius), 0.0, 1.0)));
    lookup_uv = (lookup_uv * vec2(255.0, 127.0) + 0.5) / vec2(256.0, 128.0);

    return textureLod(sun_optical_depth, lookup_uv, 0.0).rgb * vec3(rayleigh_height, mie_height, ozone_half_width);

}

vec3 extinction_through(vec3 depth) {

    return exp(-(rayleigh_coefficients * depth.x + vec3(mie_coefficient * 1.1 * depth.y) + ozone_coefficients * depth.z));

}

// Fraction of the solar disc above the geometric horizon, in cosine space where the two agree.
float solar_disc_visible(float radial_distance, float solar_cosine) {

    float ratio = min(planet_radius / radial_distance, 1.0);
    float dip = -sqrt(max(1.0 - ratio * ratio, 0.0));

    return smoothstep(dip - solar_radius, dip + solar_radius, solar_cosine);

}

// Relative to the planet's centre.
vec3 sun_transmittance(vec3 position) {

    float radial_distance = length(position);
    float solar_cosine = dot(position, sun_direction) / radial_distance;

    return extinction_through(sun_optical_depth_at(radial_distance, solar_cosine)) * solar_disc_visible(radial_distance, solar_cosine);

}

// Roughly one under a noon sun at sea level, the light every hand-set colour here was tuned under.
vec3 sunlight_colour(vec3 position) {

    return sun_transmittance(position) * sun_radiance / PI;

}

// Diffuse sky irradiance against solar height: a sixth of noon at sunset, gone by the end of twilight.
float skylight(float solar_cosine) {

    return smoothstep(-0.12, 0.45, solar_cosine);

}

#endif
