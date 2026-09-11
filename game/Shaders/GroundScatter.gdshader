shader_type spatial;
render_mode cull_disabled;

uniform vec3 eye_position;
uniform float wind_time;
uniform vec2 grass_range = vec2(160.0, 290.0);
uniform vec2 stone_range = vec2(230.0, 330.0);
uniform sampler2D rock_colour : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
instance uniform float cell_fade = 1.0;
varying vec3 local_position;
varying vec3 local_normal;
varying mat3 stone_to_view;
varying float blade;
varying float variation;

void vertex() {

    local_position = VERTEX;
    local_normal = NORMAL;
    stone_to_view = MODELVIEW_NORMAL_MATRIX;
    blade = UV2.x;
    variation = COLOR.a;
    vec3 root = MODEL_MATRIX[3].xyz;
    float distance_to_eye = length(root - eye_position);
    float fade = 1.0 - smoothstep(mix(stone_range.x, grass_range.x, blade), mix(stone_range.y, grass_range.y, blade), distance_to_eye);
    float phase = variation * TAU;
    float gust = sin(wind_time * (TAU * 1108.0 / 4096.0) + phase) * 0.65 + sin(wind_time * (TAU * 2021.0 / 4096.0) + phase * 2.0) * 0.25;
    VERTEX.xz += vec2(0.11, 0.045) * gust * UV.y * UV.y * blade;
    VERTEX *= fade * cell_fade;

}

void fragment() {

    float blade_ridge = 0.82 + 0.18 * sin(UV.x * PI);
    ALBEDO = COLOR.rgb * mix(0.48, 1.20, UV.y) * blade_ridge;
    if (blade < 0.5) {

        vec3 weights = pow(abs(local_normal), vec3(4.0));
        weights /= max(weights.x + weights.y + weights.z, 0.001);
        vec3 stone = texture(rock_colour, local_position.yz * 0.7).rgb * weights.x
            + texture(rock_colour, local_position.xz * 0.7).rgb * weights.y
            + texture(rock_colour, local_position.xy * 0.7).rgb * weights.z;
        float grain = dot(stone, vec3(0.2126, 0.7152, 0.0722));
        ALBEDO = COLOR.rgb * (0.65 + grain * 1.3);
        vec2 nx = texture(rock_normal, local_position.yz * 0.7).rg * 2.0 - 1.0;
        vec2 ny = texture(rock_normal, local_position.xz * 0.7).rg * 2.0 - 1.0;
        vec2 nz = texture(rock_normal, local_position.xy * 0.7).rg * 2.0 - 1.0;
        vec3 bump = vec3(0.0, nx.x, nx.y) * weights.x
            + vec3(ny.x, 0.0, ny.y) * weights.y + vec3(nz.x, nz.y, 0.0) * weights.z;
        NORMAL = normalize(stone_to_view * normalize(local_normal + bump * 0.65));

    }
    NORMAL = FRONT_FACING ? NORMAL : -NORMAL;
    ROUGHNESS = mix(0.86, 0.93, blade);
    SPECULAR = 0.22;
    AO = mix(1.0, mix(0.5, 1.0, UV.y), blade);
    BACKLIGHT = COLOR.rgb * blade * 0.35;

}
