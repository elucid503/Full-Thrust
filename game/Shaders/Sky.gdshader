shader_type sky;

// Catalogued night sky plus a solar disc at true angular size with Eddington limb darkening.

// Sampled raw: the map already holds a compressed stop of flux, not an sRGB colour.
uniform sampler2D star_map : filter_linear;

uniform float star_gain : hint_range(0.0, 8.0) = 0.32;
uniform float star_visibility : hint_range(0.0, 1.0) = 1.0;

// The atmosphere shell draws the camera view with the full optical integral. Reflections cannot
// see that post-composite, so the radiance cubemap receives a low-frequency version of the same
// daylight. It gives metal and water a sky to reflect without drawing a second sky on screen.
uniform vec3 planet_up = vec3(0.0, 1.0, 0.0);
uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0);
uniform float atmosphere_amount : hint_range(0.0, 1.0) = 0.0;
uniform float daylight : hint_range(0.0, 1.0) = 0.0;

uniform float sun_angular_radius = 0.00465;
uniform float sun_intensity = 46.0;
uniform float limb_darkening : hint_range(0.0, 1.0) = 0.6;

void sky() {

    vec3 direction = normalize(EYEDIR);

    // The map is equirectangular in the simulation frame, whose polar axis is Godot's Y.
    vec2 uv = vec2(atan(-direction.z, direction.x) / TAU + 0.5, acos(clamp(direction.y, -1.0, 1.0)) / PI);

    vec3 colour = texture(star_map, uv).rgb * star_gain * star_visibility;

    if (AT_CUBEMAP_PASS && atmosphere_amount > 0.001 && daylight > 0.001) {

        float elevation = dot(direction, planet_up);
        float above = smoothstep(-0.14, 0.38, elevation);
        float horizon = exp(-abs(elevation) * 5.5);

        float sun_height = dot(planet_up, sun_direction);
        float sunset = 1.0 - smoothstep(0.02, 0.34, sun_height);
        float aureole = pow(max(dot(direction, sun_direction), 0.0), 10.0);

        vec3 zenith = vec3(0.16, 0.34, 0.62);
        vec3 horizon_colour = mix(vec3(0.48, 0.62, 0.76), vec3(0.94, 0.42, 0.17), sunset);

        vec3 air = mix(horizon_colour, zenith, above) * (0.42 + horizon * 0.58);

        air += mix(vec3(0.20, 0.28, 0.42), vec3(1.0, 0.42, 0.12), sunset) * aureole * 0.28;

        colour += air * atmosphere_amount * daylight;

    }

    if (LIGHT0_ENABLED) {

        float angle = acos(clamp(dot(direction, LIGHT0_DIRECTION), -1.0, 1.0));

        if (angle < sun_angular_radius) {

            float mu = sqrt(max(1.0 - pow(angle / sun_angular_radius, 2.0), 0.0));

            colour += LIGHT0_COLOR * sun_intensity * (1.0 - limb_darkening * (1.0 - mu));

        }

    }

    COLOR = colour;

}
