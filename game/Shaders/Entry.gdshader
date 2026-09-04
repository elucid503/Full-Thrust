shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// UV.x runs from the nose of the shock back down the wake, UV.y from the axis out to the rim.
// The mesh is a fixed shape in units of the body radius; everything that changes with the entry
// changes here, so the shock does not have to be rebuilt as the vehicle slows down.

uniform float intensity : hint_range(0.0, 1.0) = 0.0;
uniform float heat : hint_range(0.0, 1.0) = 0.0;

uniform vec3 cool_colour : source_color = vec3(1.00, 0.36, 0.10);
uniform vec3 hot_colour : source_color = vec3(1.00, 0.86, 0.78);

// Where the bow ends and the wake begins, matched to the mesh so the two are shaded as what they are.
uniform float bow = 0.22;

uniform float rim_power = 2.6;
uniform float wake_reach = 3.2;
// Ten additive shells stack, so this is per shell and the sum is what must stay off the clip.
uniform float brightness = 0.17;

uniform float flicker_rate = 17.0;

void fragment() {

    float axial = clamp(UV.x, 0.0, 1.0);
    float radial = clamp(UV.y, 0.0, 1.0);

    float across = pow(max(1.0 - radial, 0.0), rim_power);

    // The bow is the bright part and holds its brightness to the shoulder; the wake falls away from
    // there, and falls away faster the less there is driving it.
    float along = axial <= bow
        ? 0.55 + 0.45 * (axial / max(bow, 1e-3))
        : pow(max(1.0 - (axial - bow) / (1.0 - bow), 0.0), wake_reach * (1.6 - intensity));

    float shimmer = 1.0 + 0.09 * sin(TIME * flicker_rate + axial * 27.0) + 0.05 * sin(TIME * flicker_rate * 0.63 - axial * 41.0 + radial * 9.0);

    float density = along * across * shimmer;

    // The shoulder runs hottest and the wake cools as it trails, the same way the gas actually does.
    float local = clamp(heat * (1.0 - 0.75 * axial) * (1.0 - 0.4 * radial), 0.0, 1.0);

    vec3 colour = mix(cool_colour, hot_colour, local);

    // Edge-on slabs of a translucent shell read brighter, which is what the grazing term buys.
    float grazing = 1.0 - abs(dot(normalize(NORMAL), normalize(VIEW)));

    ALBEDO = colour * intensity * density * (0.40 + 0.90 * grazing) * brightness;

    ALPHA = 1.0;

}
