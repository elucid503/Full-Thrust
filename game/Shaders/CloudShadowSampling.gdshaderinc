uniform sampler2D local_cloud_shadow : filter_linear, repeat_disable;
uniform vec3 shadow_centre;
uniform vec3 shadow_east;
uniform vec3 shadow_north;
uniform float shadow_ready = 0.0;
uniform float shadow_span = 16000.0;

float filtered_cloud_transmission(vec2 uv, float footprint) {

    vec3 levels = textureLod(local_cloud_shadow, uv, 0.0).rgb;
    float transmission = mix(levels.r, levels.g, smoothstep(64.0, 512.0, footprint));
    return mix(transmission, levels.b, smoothstep(512.0, 2048.0, footprint));

}

vec2 local_cloud_light(vec3 world) {

    vec3 offset = world - shadow_centre;
    vec2 uv = vec2(dot(offset, shadow_east), dot(offset, shadow_north)) / shadow_span + 0.5;
    float coverage = (1.0 - smoothstep(0.38, 0.49, max(abs(uv.x - 0.5), abs(uv.y - 0.5)))) * shadow_ready;
    vec2 texel = 1.25 / vec2(textureSize(local_cloud_shadow, 0));
    float transmission = texture(local_cloud_shadow, uv).r * 0.4;
    transmission += texture(local_cloud_shadow, uv + vec2(texel.x, texel.y)).r * 0.15;
    transmission += texture(local_cloud_shadow, uv + vec2(-texel.x, texel.y)).r * 0.15;
    transmission += texture(local_cloud_shadow, uv + vec2(texel.x, -texel.y)).r * 0.15;
    transmission += texture(local_cloud_shadow, uv - texel).r * 0.15;
    float shade = 1.0 - transmission;
    return vec2(shade, coverage);

}
