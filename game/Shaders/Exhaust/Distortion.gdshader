shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_front, depth_test_disabled, shadows_disabled, fog_disabled;

// A closed volume keeps background refraction visible from below and inside the exhaust.
uniform sampler2D screen_texture : hint_screen_texture, filter_linear;
uniform sampler2D noise_texture : filter_linear_mipmap, repeat_enable;
uniform sampler2D cloud_buffer : filter_nearest, repeat_disable;
uniform sampler2D cloud_depth : filter_nearest, repeat_disable;
uniform bool cloud_buffer_ready = false;
uniform float strength = 1.0;
uniform float length_metres = 12.0;
uniform float radius_metres = 0.5;
uniform vec3 crossflow = vec3(0.0);

#include "res://Shaders/Exhaust/Volume.gdshaderinc"
#include "res://Shaders/Exhaust/Contact.gdshaderinc"

// Match the cloud pass's depth-aware upsampling to keep its colour off foreground silhouettes.
vec4 refracted_cloud(vec2 uv, mat4 inverse_projection) {

    if (!cloud_buffer_ready) { return vec4(0.0); }
    float scene_distance = 4000000.0;
    float depth = textureLod(depth_buffer, uv, 0.0).r;
    if (depth > 0.0) {

        vec4 view = inverse_projection * vec4(uv * 2.0 - 1.0, depth, 1.0);
        scene_distance = length(view.xyz / view.w);

    }
    ivec2 size = textureSize(cloud_buffer, 0);
    vec2 at = uv * vec2(size) - 0.5;
    ivec2 base = ivec2(floor(at));
    vec2 fraction = fract(at);
    vec4 cloud = vec4(0.0);
    float total = 0.0;
    for (int y = 0; y < 2; y++) {

        for (int x = 0; x < 2; x++) {

            ivec2 pixel = clamp(base + ivec2(x, y), ivec2(0), size - 1);
            float distance = texelFetch(cloud_depth, pixel, 0).r;
            float weight = (x == 0 ? 1.0 - fraction.x : fraction.x)
                * (y == 0 ? 1.0 - fraction.y : fraction.y);
            weight *= exp(-abs(distance - scene_distance) / max(scene_distance * 0.03, 1.0));
            cloud += texelFetch(cloud_buffer, pixel, 0) * weight;
            total += weight;

        }

    }
    return total > 0.01 ? cloud / total : vec4(0.0);

}

void fragment() {

    vec3 ray = normalize(volume_position - volume_camera);
    vec2 interval = volume_interval(volume_camera, ray, SCREEN_UV, INV_PROJECTION_MATRIX);
    if (interval.y <= interval.x) { discard; }

    float ds = (interval.y - interval.x) / 16.0;
    float scale = max(radius_metres, 0.001);
    vec3 flow_position = vec3(0.0);
    float optical_depth = 0.0;

    for (int i = 0; i < 16; i++) {

        vec3 p = volume_camera + ray * (interval.x + (float(i) + 0.5) * ds);
        float contact_mask;
        float contact_wake;
        p = contact_flow(p, scale, ds, contact_mask, contact_wake);
        float along = clamp(-p.y / length_metres, 0.0, 1.0);
        vec3 bent = p - crossflow * along * along;
        float radius = scale + max(-p.y, 0.0) * 0.28;
        float r = length(bent.xz) / radius;
        float envelope = (1.0 - smoothstep(0.45, 1.0, r))
            * smoothstep(0.0, 0.04, along) * (1.0 - smoothstep(0.3, 1.0, along));
        if (envelope <= 0.0) { continue; }

        float weight = envelope * contact_mask * (1.0 + contact_wake * 0.5) * ds / radius;
        flow_position += bent * weight;
        optical_depth += weight;

    }

    // Evaluate coherent flow once; averaging signed noise along the ray erased the axial shimmer.
    vec3 p = flow_position / max(optical_depth, 0.0001);
    float radius = scale + max(-p.y, 0.0) * 0.28;
    // Independent advection and band-limited noise avoid a repeating, slowly bending lens.
    vec2 uv = vec2(p.x / radius * 1.8, p.y / scale * 0.14);
    vec2 drift_a = effect_time * vec2(0.071, 2.37);
    vec2 drift_b = effect_time * vec2(-1.91, 0.053);
    vec2 warp = vec2(sin(effect_time * 0.73 + seed), cos(effect_time * 0.59 + seed)) * 0.09;
    vec2 at_a = uv + drift_a + warp + vec2(seed);
    vec2 at_b = uv.yx * vec2(0.83, 1.0) + drift_b - warp
        + vec2(p.z / radius * 1.8, seed * 0.37);
    vec2 noise_a = texture(noise_texture, at_a).rg
        - textureLod(noise_texture, at_a, 4.0).rg;
    vec2 noise_b = texture(noise_texture, at_b).rg
        - textureLod(noise_texture, at_b, 4.0).rg;
    vec3 shimmer = vec3(noise_a.x + noise_b.y * 0.4, noise_b.x * 0.25,
        noise_a.y - noise_b.x * 0.4);
    // Pressure fronts occupy the surrounding air, independently of the hot core's coverage.
    vec3 source = vec3(0.0, -length_metres * 0.45, 0.0) + crossflow * 0.2025;
    float closest = dot(source - volume_camera, ray);
    vec3 to_ray = volume_camera + ray * clamp(closest, interval.x, interval.y) - source;
    float distance_to_ray = length(to_ray);
    float wave_range = length_metres * 0.42;
    float pixel_width = max(fwidth(distance_to_ray), scale * 0.015);
    vec3 waves = vec3(0.0);
    for (int front = 0; front < 3; front++) {

        float age = fract(effect_time * 0.85 + float(front) / 3.0 + seed * 0.13);
        float front_radius = mix(scale * 1.5, wave_range, age);
        float thickness = max(scale * 0.16 + front_radius * 0.018, pixel_width);
        float band = (distance_to_ray - front_radius) / thickness;
        float pulse = -band * exp(-band * band * 1.5);
        float life = smoothstep(0.0, 0.12, age) * (1.0 - smoothstep(0.65, 1.0, age));
        float visible_front = smoothstep(interval.x - thickness, interval.x + thickness, closest)
            * (1.0 - smoothstep(interval.y - thickness, interval.y + thickness, closest));
        waves += to_ray / max(distance_to_ray, 0.001) * pulse * life * visible_front;

    }
    vec3 deflection = shimmer * 0.9;
    vec3 view_deflection = (VIEW_MATRIX * MODEL_MATRIX * vec4(deflection, 0.0)).xyz;
    // Bound displacement and fade coverage to avoid a visible refractive shell.
    float coverage = 1.0 - exp(-optical_depth * 0.85);
    vec2 pixel_offset = view_deflection.xy * vec2(1.0, -1.0) * strength * 12.0;
    pixel_offset /= sqrt(1.0 + dot(pixel_offset, pixel_offset) / 6.25);
    pixel_offset *= coverage;
    // Fade shimmer at depth breaks instead of tearing foreground silhouettes into the sky.
    vec2 shimmer_uv = clamp(SCREEN_UV + pixel_offset / VIEWPORT_SIZE, vec2(0.001), vec2(0.999));
    float depth_here = textureLod(depth_buffer, SCREEN_UV, 0.0).r;
    float depth_there = textureLod(depth_buffer, shimmer_uv, 0.0).r;
    float depth_break = abs(depth_here - depth_there) / max(max(depth_here, depth_there), 0.000001);
    pixel_offset *= 1.0 - smoothstep(0.01, 0.06, depth_break);
    vec3 view_waves = (VIEW_MATRIX * MODEL_MATRIX * vec4(waves, 0.0)).xyz;
    vec2 wave_offset = view_waves.xy * vec2(1.0, -1.0) * strength * 12.0;
    wave_offset /= max(1.0, length(wave_offset) / 3.0);
    pixel_offset += wave_offset;
    if (dot(pixel_offset, pixel_offset) < 0.000001) { discard; }
    vec2 offset = pixel_offset / VIEWPORT_SIZE;
    vec2 displaced_uv = clamp(SCREEN_UV + offset, vec2(0.001), vec2(0.999));

    // Do not drag an opaque foreground object into a clear ray through the exhaust.
    vec2 displaced_interval = volume_interval(volume_camera, ray, displaced_uv, INV_PROJECTION_MATRIX);
    if (displaced_interval.y <= interval.x) { displaced_uv = SCREEN_UV; }

    // Include the separately rendered weather instead of refracting only opaque objects beneath it.
    vec4 cloud_here = refracted_cloud(SCREEN_UV, INV_PROJECTION_MATRIX);
    vec4 cloud_there = refracted_cloud(displaced_uv, INV_PROJECTION_MATRIX);
    vec3 background_here = textureLod(screen_texture, SCREEN_UV, 0.0).rgb;
    vec3 background_there = textureLod(screen_texture, displaced_uv, 0.0).rgb;
    ALBEDO = (background_there - background_here) * (1.0 - cloud_here.a)
        + cloud_there.rgb - cloud_here.rgb - background_here * (cloud_there.a - cloud_here.a);
    ALPHA = 1.0;

}
