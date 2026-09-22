shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled;

uniform sampler2D depth_buffer : hint_depth_texture, filter_nearest;
#include "res://Shaders/Clouds/CloudMarch.gdshaderinc"

uniform sampler2D cloud_buffer : filter_nearest, repeat_disable;
uniform sampler2D cloud_depth : filter_nearest, repeat_disable;
uniform bool cloud_buffer_ready = false;

void vertex() {

    POSITION = vec4(VERTEX.xy, 1.0, 1.0);

}

void fragment() {

    vec3 camera = INV_VIEW_MATRIX[3].xyz;
    vec4 view_ray = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, 1.0, 1.0);
    vec3 ray = normalize(mat3(INV_VIEW_MATRIX) * view_ray.xyz);
    vec3 origin = camera - planet_centre;
    float scene_distance = 4000000.0;
    float depth = texture(depth_buffer, SCREEN_UV).r;
    if (depth > 0.0) {

        vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
        scene_distance = length(view.xyz / view.w);

    }
    vec4 filtered = vec4(0.0);
    float total = 0.0;
    if (cloud_buffer_ready) {

        ivec2 size = textureSize(cloud_buffer, 0);
        vec2 at = SCREEN_UV * vec2(size) - 0.5;
        ivec2 base = ivec2(floor(at));
        vec2 fraction = fract(at);
        for (int y = 0; y < 2; y++) {

            for (int x = 0; x < 2; x++) {

                ivec2 pixel = clamp(base + ivec2(x, y), ivec2(0), size - 1);
                float distance = texelFetch(cloud_depth, pixel, 0).r;
                float weight = (x == 0 ? 1.0 - fraction.x : fraction.x) * (y == 0 ? 1.0 - fraction.y : fraction.y);
                // Tight near the vessel, where colour must not bleed onto it; loose over distant
                // scenery, where a strict match sends grazing terrain to the unaccumulated fallback.
                float tolerance = mix(max(scene_distance * 0.03, 1.0), scene_distance * 0.5,
                    smoothstep(2000.0, 10000.0, min(distance, scene_distance)));
                weight *= exp(-abs(distance - scene_distance) / tolerance);
                filtered += texelFetch(cloud_buffer, pixel, 0) * weight;
                total += weight;

            }

        }

    }
    // Evaluate only unmatched silhouettes at full resolution, so cloud colour cannot bleed onto a vessel.
    float footprint = max(length(dFdx(ray)), length(dFdy(ray)));
    float jitter = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
    vec4 combined;
    if (total > 0.01) {

        combined = filtered / total;

    } else {

        float cloud_distance;
        vec4 cloud = cloud_radiance(origin, ray, scene_distance, footprint, jitter, cloud_distance);
        vec4 fog;
        float fog_distance = cloud.a >= 1.0 ? cloud_distance : scene_distance;
        vec4 full_fog = coastal_fog(origin, ray, fog_distance, cloud_distance, fog);
        combined = cloud_fog_composite(cloud, fog, full_fog);

    }
    if (combined.a < 0.0001) { discard; }
    ALBEDO = combined.rgb;
    ALPHA = combined.a;

}
