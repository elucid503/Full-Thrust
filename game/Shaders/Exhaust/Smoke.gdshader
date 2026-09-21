shader_type spatial;
render_mode unshaded, blend_premul_alpha, cull_front, depth_draw_never, depth_test_disabled, shadows_disabled;

uniform sampler3D flow_noise : filter_linear, repeat_enable;
uniform sampler2D depth_buffer : hint_depth_texture, filter_nearest;
uniform vec3 tint : source_color = vec3(0.58, 0.53, 0.47);
uniform vec3 flame_light : source_color = vec3(0.0);
uniform vec3 sun_direction = vec3(0.3, 0.8, 0.2);
uniform float daylight = 1.0;
uniform float effect_time = 0.0;
varying vec3 local_position;
varying flat vec3 local_camera;
varying flat vec3 local_sun;
varying flat mat4 view_to_local;
varying flat float variation;

void vertex() {

    local_position = VERTEX;
    view_to_local = inverse(MODELVIEW_MATRIX);
    local_camera = (view_to_local * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
    local_sun = normalize((inverse(MODEL_MATRIX) * vec4(sun_direction, 0.0)).xyz);
    variation = INSTANCE_CUSTOM.z * 17.0;

}

float density_at(vec3 p) {

    vec3 uv = p * 0.9 + vec3(variation, effect_time * 0.025, variation * 0.37);
    float n = textureLod(flow_noise, uv, 0.0).r;
    float detail = textureLod(flow_noise, uv * 2.3 + vec3(0.21), 0.0).r;
    float radius = mix(0.65, 1.0, smoothstep(0.25, 0.7, n));
    float envelope = max(1.0 - dot(p, p) * 4.0 / (radius * radius), 0.0);
    return pow(envelope, 1.3) * smoothstep(0.3, 0.66, n * 0.75 + detail * 0.25) * 24.0;

}

void fragment() {

    vec3 ray = normalize(local_position - local_camera);
    float b = dot(local_camera, ray);
    float discriminant = b * b - dot(local_camera, local_camera) + 0.25;
    if (discriminant <= 0.0) { discard; }
    float near_hit = max(0.0, -b - sqrt(discriminant));
    float far_hit = -b + sqrt(discriminant);
    float depth = textureLod(depth_buffer, SCREEN_UV, 0.0).r;
    if (depth > 0.0) {

        vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
        vec3 scene = (view_to_local * vec4(view.xyz / view.w, 1.0)).xyz;
        far_hit = min(far_hit, dot(scene - local_camera, ray));

    }
    if (far_hit <= near_hit) { discard; }
    float ds = (far_hit - near_hit) / 12.0;
    float transmission = 1.0;
    vec3 total = vec3(0.0);
    for (int i = 0; i < 12; i++) {

        vec3 p = local_camera + ray * (near_hit + (float(i) + 0.5) * ds);
        float density = density_at(p) * COLOR.a;
        float opacity = 1.0 - exp(-density * ds);
        float shadow = exp(-density_at(p + local_sun * 0.18) * 0.18);
        vec3 lighting = tint * (0.16 + daylight * (0.14 + 0.7 * shadow)) + flame_light;
        total += transmission * opacity * lighting;
        transmission *= 1.0 - opacity;
        if (transmission < 0.02) { break; }

    }
    ALBEDO = total;
    ALPHA = 1.0 - transmission;

}
