shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// UV.x runs from the throat to the tail of the plume, UV.y from the axis to the rim of the volume.
uniform float throttle : hint_range(0.0, 1.0) = 0.0;

uniform vec3 core_colour : source_color = vec3(0.78, 0.88, 1.00);
uniform vec3 flame_colour : source_color = vec3(1.00, 0.55, 0.24);

uniform float rim_power = 2.2;
uniform float reach = 2.4;
uniform float brightness = 0.85;
uniform float flicker_rate = 24.0;

void fragment() {

    float axial = clamp(UV.x, 0.0, 1.0);
    float radial = clamp(UV.y, 0.0, 1.0);

    // Both falloffs are smooth to the last shell, so the plume has no visible end cap or rim.
    float along = pow(max(1.0 - axial, 0.0), reach);
    float across = pow(max(1.0 - radial, 0.0), rim_power);

    float shimmer = 1.0 + 0.05 * sin(TIME * flicker_rate + axial * 31.0) + 0.03 * sin(TIME * flicker_rate * 0.57 - axial * 17.0);

    float density = along * across * shimmer;

    // The hot core is white-blue and short; the skirt of the plume cools to orange as it expands.
    float heat = clamp(pow(max(1.0 - radial, 0.0), 2.0) * (1.0 - 0.6 * axial), 0.0, 1.0);

    vec3 colour = mix(flame_colour, core_colour, heat);

    // Edge-on slabs of a translucent plume read brighter, which is what the grazing term buys.
    float grazing = 1.0 - abs(dot(normalize(NORMAL), normalize(VIEW)));

    // Unshaded output comes straight from ALBEDO; additive blending turns it into light.
    ALBEDO = colour * throttle * density * (0.45 + 0.85 * grazing) * brightness;

    ALPHA = 1.0;

}
