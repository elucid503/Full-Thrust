#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
layout(rgba16f, set = 0, binding = 0) uniform image2D scene_color;
layout(set = 0, binding = 1) uniform sampler2D scene_depth;
layout(push_constant, std430) uniform Parameters {
    vec2 size;
    float near_depth;
    float history_reactivity;
} parameters;

void main() {

    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = ivec2(parameters.size);
    if (any(greaterThanEqual(pixel, size))) { return; }
    vec4 color = imageLoad(scene_color, pixel);
    if (color.a <= parameters.history_reactivity) { return; }

    // FSR dilates transparency over neighbouring opaque silhouettes; retain their history.
    float foreground = 0.0;
    float background = 1.0;
    for (int y = -2; y <= 2; y++) {

        for (int x = -2; x <= 2; x++) {

            float depth = texelFetch(scene_depth, clamp(pixel + ivec2(x, y), ivec2(0), size - 1), 0).r;
            foreground = max(foreground, depth);
            background = min(background, depth);

        }

    }
    if (foreground > parameters.near_depth && background < foreground * 0.25) {

        imageStore(scene_color, pixel, vec4(color.rgb, parameters.history_reactivity));

    }

}
