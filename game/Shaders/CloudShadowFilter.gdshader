shader_type canvas_item;
render_mode unshaded;

uniform sampler2D source : hint_screen_texture, filter_linear, repeat_disable;

float blurred(vec2 uv, float spacing) {

    float value = 0.0;
    for (int y = -1; y <= 1; y++) {

        for (int x = -1; x <= 1; x++) {

            float weight = (x == 0 ? 0.5 : 0.25) * (y == 0 ? 0.5 : 0.25);
            value += texture(source, uv + vec2(float(x), float(y)) * spacing).r * weight;

        }

    }
    return value;

}

void fragment() {

    COLOR = vec4(texture(source, SCREEN_UV).r, blurred(SCREEN_UV, 256.0 / 16000.0),
        blurred(SCREEN_UV, 1024.0 / 16000.0), 1.0);

}
