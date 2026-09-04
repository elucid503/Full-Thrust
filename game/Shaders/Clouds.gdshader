shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_draw_never, depth_test_disabled, cull_front;

// A raymarched deck between two shells. Where the cloud is comes from the measured MODIS cover, so
// the weather is the planet's own and the shadows the ground samples land under the cloud casting
// them; what the cloud looks like from a kilometre away is noise, because no map resolves that.

uniform sampler2D cloud_map : filter_linear_mipmap;
uniform sampler3D shape_noise : filter_linear, repeat_enable;
uniform sampler3D detail_noise : filter_linear, repeat_enable;

uniform sampler2D depth_buffer : hint_depth_texture, filter_nearest;

uniform vec3 planet_centre = vec3(0.0, 0.0, 0.0);
uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0);

uniform vec3 sun_colour : source_color = vec3(1.0, 0.972, 0.94);
uniform vec3 sky_colour : source_color = vec3(0.40, 0.52, 0.72);

uniform float planet_radius = 1274200.0;
uniform float base_radius = 1275400.0;
uniform float top_radius = 1281200.0;

uniform float cloud_spin = 0.0;

uniform float coverage_floor : hint_range(0.0, 1.0) = 0.20;
uniform float coverage_ceiling : hint_range(0.0, 1.0) = 0.66;

// Metres per tile of each volume, inverted. The shape tile is wide enough that its repeat is broken
// up by the cover map long before the eye finds it.
// Metres per tile of the shape volume, inverted. A hundred kilometres: cloud masses on a weather
// map are tens of kilometres across, and a tile an order of magnitude under that reads from orbit
// as static rather than as cloud. The same volume is read again a dozen times tighter, because at
// that width alone one cloud spans no variation at all and comes out as a slab of erosion.
uniform float shape_scale = 0.0000092;
uniform float shape_ratio = 13.0;
uniform float shape_mix : hint_range(0.0, 1.0) = 0.42;
uniform float detail_scale = 0.0012;
uniform float detail_erosion : hint_range(0.0, 1.0) = 0.46;

// The same volume read far wider: a weather field a hundred kilometres across that decides how much
// of the map's cover actually builds and how tall it gets. Without it every cloud on the planet is
// the same cloud, which is exactly how the first deck read.
// How far out the frayed edges are still worth resolving.
uniform float erosion_near = 24000.0;
uniform float erosion_far = 120000.0;

uniform float weather_scale = 0.16;
uniform float weather_depth : hint_range(0.0, 1.0) = 0.65;

// Extinction per metre through solid cloud. A three-kilometre tower at this coefficient comes out
// near optical depth twenty, which is what a real cumulus stops.
uniform float density_scale = 0.0078;

uniform float light_reach = 1700.0;

uniform float sun_power = 7.2;

uniform float forward_scatter : hint_range(0.0, 0.99) = 0.62;
uniform float back_scatter : hint_range(-0.99, 0.0) = -0.18;

uniform float powder : hint_range(0.0, 1.0) = 0.55;
// Held low on purpose: the sky fills a cloud's shaded side, but filling it as much as the sun
// lights its top leaves a flat white card with no form in it at all.
uniform float ambient_gain : hint_range(0.0, 2.0) = 0.36;

uniform float drift = 0.0;

const int view_steps = 48;
const int light_steps = 5;

// Returns the near and far ray parameters of a sphere hit, or a reversed pair where the ray misses.
vec2 sphere_span(vec3 origin, vec3 direction, float radius) {

    float half_b = dot(origin, direction);
    float c = dot(origin, origin) - radius * radius;

    float discriminant = half_b * half_b - c;

    if (discriminant < 0.0) {

        return vec2(1.0, -1.0);

    }

    float root = sqrt(discriminant);

    return vec2(-half_b - root, -half_b + root);

}

vec3 unspin(vec3 direction) {

    float sine = sin(-cloud_spin);
    float cosine = cos(-cloud_spin);

    return vec3(direction.x * cosine + direction.z * sine, direction.y, -direction.x * sine + direction.z * cosine);

}

vec2 equirect(vec3 direction) {

    vec3 deck = unspin(direction);

    return vec2(atan(-deck.z, deck.x) / TAU + 0.5, 0.5 - asin(clamp(deck.y, -1.0, 1.0)) / PI);

}

float remap(float value, float low, float high) {

    return clamp((value - low) / max(high - low, 1e-5), 0.0, 1.0);

}

float density_at(vec3 position, float erosion) {

    float radius = length(position);

    float level = (radius - base_radius) / (top_radius - base_radius);

    if (level <= 0.0 || level >= 1.0) {

        return 0.0;

    }

    float cover = smoothstep(coverage_floor, coverage_ceiling, texture(cloud_map, equirect(position / radius)).r);

    if (cover <= 0.0) {

        return 0.0;

    }

    vec3 wind = vec3(drift, 0.0, drift * 0.4);

    float weather = texture(shape_noise, position * (shape_scale * weather_scale) + wind * 0.25).r;

    // Under a weak weather field the map's cover thins to a sheet; under a strong one it builds.
    cover *= mix(1.0 - weather_depth, 1.0 + weather_depth * 0.45, weather);

    if (cover <= 0.0) {

        return 0.0;

    }

    // Flat bottomed and feathered on top. How far up it is allowed to build is the weather's to
    // decide, which is the difference between a stratus sheet and a stack of cumulus standing on it.
    float ceiling = 0.16 + weather * 0.62 + cover * 0.24;

    float profile = smoothstep(0.0, 0.10 + weather * 0.10, level) * (1.0 - smoothstep(ceiling, ceiling + 0.30, level));

    float wide = texture(shape_noise, position * shape_scale + wind).r;
    float close = texture(shape_noise, position * (shape_scale * shape_ratio) + wind * 2.3).r;

    float shape = mix(wide, close, shape_mix);

    float density = remap(shape, 1.0 - cover * profile, 1.0);

    if (density <= 0.0) {

        return 0.0;

    }

    if (erosion <= 0.001) {

        return density * density_scale;

    }

    // Erosion is strongest at the top of the deck, where a real cloud frays and the base does not.
    float wisp = texture(detail_noise, position * detail_scale + wind * 3.0).r;

    return remap(density, wisp * erosion * (0.25 + 0.75 * level), 1.0) * density_scale;

}

float towards_sun(vec3 position, float erosion) {

    float step_length = light_reach / float(light_steps);

    float optical = 0.0;

    for (int index = 0; index < light_steps; index++) {

        vec3 at = position + sun_direction * (step_length * (float(index) + 0.5));

        optical += density_at(at, erosion) * step_length;

    }

    return optical;

}

float henyey(float cosine, float g) {

    float gg = g * g;

    return (1.0 - gg) / (4.0 * PI * pow(1.0 + gg - 2.0 * g * cosine, 1.5));

}

void fragment() {

    vec3 camera = INV_VIEW_MATRIX[3].xyz;
    vec3 ray = normalize((INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz - camera);

    vec3 origin = camera - planet_centre;

    float height = length(origin);

    vec2 top = sphere_span(origin, ray, top_radius);
    vec2 base = sphere_span(origin, ray, base_radius);

    if (top.y <= max(top.x, 0.0)) {

        discard;

    }

    float near;
    float far;

    if (height > top_radius) {

        near = top.x;
        far = base.y > base.x ? base.x : top.y;

    }
    else if (height > base_radius) {

        near = 0.0;
        far = base.y > base.x && base.x > 0.0 ? base.x : top.y;

    }
    else {

        near = base.y;
        far = top.y;

    }

    near = max(near, 0.0);

    // Anything already drawn - the ground, the vehicle - ends the march where it stands.
    float depth = texture(depth_buffer, SCREEN_UV).x;

    if (depth > 0.0) {

        vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);

        far = min(far, length(view.xyz / view.w));

    }

    if (far <= near) {

        discard;

    }

    float step_length = (far - near) / float(view_steps);

    // A step of jitter off the pixel's own coordinates. Without it the march bands the deck into
    // shells wherever the step lands on the same surface across a run of pixels.
    float offset = fract(sin(dot(SCREEN_UV, vec2(12.9898, 78.233))) * 43758.5453);

    float cosine = dot(ray, sun_direction);

    // Two lobes: a strong forward one that lights the rim of a cloud between the eye and the sun,
    // and a weak backward one that keeps the far side from going flat.
    float phase = mix(henyey(cosine, forward_scatter), henyey(cosine, back_scatter), 0.32);

    float transmittance = 1.0;

    vec3 scattered = vec3(0.0);

    for (int index = 0; index < view_steps; index++) {

        float travelled = near + step_length * (float(index) + offset);

        vec3 position = origin + ray * travelled;

        // A cloud two hundred kilometres off is a shape, not a texture: eroding it there only
        // shatters it into single pixels, which is what a marched deck reads as from orbit.
        float erosion = detail_erosion * (1.0 - smoothstep(erosion_near, erosion_far, travelled));

        float density = density_at(position, erosion);

        if (density <= 0.0) {

            continue;

        }

        float optical = towards_sun(position, erosion);

        // The powder term is what gives a cloud a dark core against a bright edge; without it a
        // thick cloud lit from behind reads as a flat grey card.
        float energy = exp(-optical) * mix(1.0, (1.0 - exp(-optical * 2.0)) * 2.0, powder);

        float elevation = dot(position / length(position), sun_direction);

        float lit = smoothstep(-0.12, 0.03, elevation);

        // The sky a cloud sits under is only bright while there is a sun over the horizon to light
        // it. Without this the ambient term carries straight through the terminator and the night
        // side of the deck glows blue.
        float dusk = smoothstep(-0.30, 0.08, elevation);

        float level = clamp((length(position) - base_radius) / (top_radius - base_radius), 0.0, 1.0);

        vec3 light = sun_colour * (sun_power * energy * phase * lit)
            + sky_colour * ambient_gain * (0.35 + 0.65 * level) * dusk;

        float extinction = density * step_length;
        float remaining = exp(-extinction);

        scattered += transmittance * light * (1.0 - remaining);

        transmittance *= remaining;

        if (transmittance < 0.015) {

            break;

        }

    }

    ALBEDO = scattered;
    ALPHA = 1.0 - transmittance;

}
