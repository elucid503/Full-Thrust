uniform sampler2D soil_detail : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_detail : filter_linear_mipmap_anisotropic, repeat_enable;
uniform float parallax_depth : hint_range(0.0, 0.25) = 0.12;
uniform float close_detail_strength : hint_range(0.0, 1.0) = 1.0;

float terrain_depth(vec2 uv, vec2 dx, vec2 dy, float rock) {

    if (rock < 0.01) { return 1.0 - textureGrad(soil_detail, uv, dx, dy).r; }
    if (rock > 0.99) { return 1.0 - textureGrad(rock_detail, uv, dx, dy).r; }
    return 1.0 - mix(textureGrad(soil_detail, uv, dx, dy).r,
        textureGrad(rock_detail, uv, dx, dy).r, rock);

}

vec2 terrain_parallax(vec2 uv, vec2 dx, vec2 dy, vec3 eye, float rock, float strength, float tile) {

    float layers = mix(16.0, 8.0, clamp(eye.z, 0.0, 1.0));
    vec2 step_uv = eye.xy / max(eye.z, 0.22) * (parallax_depth * mix(0.35, 1.0, rock) * strength / tile / layers);
    vec2 at = uv;
    float depth = 0.0;
    float sampled = terrain_depth(at, dx, dy, rock);
    float previous = sampled;
    for (int layer = 0; layer < 16; layer++) {

        if (depth >= sampled) { break; }
        previous = sampled - depth;
        at -= step_uv;
        depth += 1.0 / layers;
        sampled = terrain_depth(at, dx, dy, rock);

    }
    float after = sampled - depth;
    return mix(at, at + step_uv, clamp(-after / max(previous - after, 0.00001), 0.0, 1.0));

}
