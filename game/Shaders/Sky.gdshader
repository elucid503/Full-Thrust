shader_type sky;

// Catalogued night sky plus a solar disc at true angular size with Eddington limb darkening.

// Sampled raw: the map already holds a compressed stop of flux, not an sRGB colour.
uniform sampler2D star_map : filter_linear;

uniform float star_gain : hint_range(0.0, 8.0) = 2.1;

uniform float sun_angular_radius = 0.00465;
uniform float sun_intensity = 46.0;
uniform float limb_darkening : hint_range(0.0, 1.0) = 0.6;

void sky() {

    vec3 direction = normalize(EYEDIR);

    // The map is equirectangular in the simulation frame, whose polar axis is Godot's Y.
    vec2 uv = vec2(atan(-direction.z, direction.x) / TAU + 0.5, acos(clamp(direction.y, -1.0, 1.0)) / PI);

    vec3 colour = texture(star_map, uv).rgb * star_gain;

    if (LIGHT0_ENABLED) {

        float angle = acos(clamp(dot(direction, LIGHT0_DIRECTION), -1.0, 1.0));

        if (angle < sun_angular_radius) {

            float mu = sqrt(max(1.0 - pow(angle / sun_angular_radius, 2.0), 0.0));

            colour += LIGHT0_COLOR * sun_intensity * (1.0 - limb_darkening * (1.0 - mu));

        }

    }

    COLOR = colour;

}
