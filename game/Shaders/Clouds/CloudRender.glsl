#[compute]
#version 450
#define CLOUD_COMPUTE
#define PI 3.141592653589793
#define TAU 6.283185307179586

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
layout(rgba16f, set = 0, binding = 0) uniform writeonly image2D cloud_output;
layout(r32f, set = 0, binding = 1) uniform writeonly image2D distance_output;
layout(set = 0, binding = 2) uniform sampler2D depth_buffer;
layout(set = 0, binding = 3) uniform sampler2D cloud_map;
layout(set = 0, binding = 4) uniform sampler3D shape_noise;
layout(set = 0, binding = 5) uniform sampler3D detail_noise;
layout(set = 0, binding = 6) uniform sampler2D local_cloud_shadow;
layout(set = 0, binding = 7) uniform sampler2D local_cloud_lighting;
layout(set = 0, binding = 8) uniform sampler2D sun_optical_depth;
layout(std140, set = 0, binding = 9) uniform Parameters {
    vec4 data[24];
} parameters;

#define planet_centre parameters.data[0].xyz
#define planet_radius parameters.data[0].w
#define sun_direction parameters.data[1].xyz
#define extinction parameters.data[1].w
#define eye_up parameters.data[2].xyz
#define eye_height parameters.data[2].w
#define shadow_centre parameters.data[3].xyz
#define shadow_ready parameters.data[3].w
#define shadow_east parameters.data[4].xyz
#define shadow_span parameters.data[4].w
#define shadow_north parameters.data[5].xyz
#define sun_power parameters.data[5].w
#define coastal_weather_direction parameters.data[6].xyz
#define weather_coverage parameters.data[6].w
#define base_radius parameters.data[7].x
#define top_radius parameters.data[7].y
#define ambient_gain parameters.data[7].z
#define sun_shafts parameters.data[7].w
#define cloud_frame mat3(parameters.data[8].xyz, parameters.data[9].xyz, parameters.data[10].xyz)
#define fog_enabled parameters.data[8].w
#define atmosphere_radius parameters.data[20].x
#define rayleigh_height parameters.data[20].y
#define mie_height parameters.data[20].z
#define ozone_half_width parameters.data[20].w
#define rayleigh_coefficients parameters.data[21].xyz
#define mie_coefficient parameters.data[21].w
#define ozone_coefficients parameters.data[22].xyz
#define sun_radiance parameters.data[23].xyz

#include "res://Shaders/Atmosphere/Sunlight.gdshaderinc"
#include "res://Shaders/Clouds/CloudShadowSampling.gdshaderinc"
#include "res://Shaders/Clouds/CloudRayGeometry.gdshaderinc"
#include "res://Shaders/Atmosphere/AtmosphereComposite.gdshaderinc"
#include "res://Shaders/Clouds/CloudField.gdshaderinc"
#include "res://Shaders/Clouds/CloudMarch.gdshaderinc"
#include "res://Shaders/Atmosphere/CoastalFog.gdshaderinc"

#define inverse_projection mat4(parameters.data[16], parameters.data[17], parameters.data[18], parameters.data[19])

vec3 camera_ray(vec2 uv) {

    return (inverse_projection * vec4(uv * 2.0 - 1.0, 1.0, 1.0)).xyz;

}

void main() {

    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(cloud_output);
    if (any(greaterThanEqual(pixel, size))) { return; }
    vec2 uv = (vec2(pixel) + 0.5) / vec2(size);
    vec3 view_ray = camera_ray(uv);
    mat3 camera_basis = mat3(parameters.data[12].xyz, parameters.data[13].xyz, parameters.data[14].xyz);
    vec3 ray = normalize(camera_basis * view_ray);
    vec3 origin = parameters.data[15].xyz - planet_centre;
    float depth = textureLod(depth_buffer, uv, 0.0).r;
    vec4 view_position = inverse_projection * vec4(uv * 2.0 - 1.0, depth, 1.0);
    float scene_distance = depth > 0.0 ? length(view_position.xyz / view_position.w) : 4000000.0;
    float footprint = max(length(normalize(camera_ray(uv + vec2(1.0 / float(size.x), 0.0))) - normalize(view_ray)),
        length(normalize(camera_ray(uv + vec2(0.0, 1.0 / float(size.y)))) - normalize(view_ray)));
    float jitter = fract(52.9829189 * fract(dot(vec2(pixel), vec2(0.06711056, 0.00583715))));
    float cloud_distance;
    vec4 cloud = cloud_radiance(origin, ray, scene_distance, footprint, jitter, cloud_distance);
    vec4 foreground_fog;
    vec4 full_fog = coastal_fog(origin, ray, cloud.a >= 1.0 ? cloud_distance : scene_distance, cloud_distance, foreground_fog);
    imageStore(cloud_output, pixel, cloud_fog_composite(cloud, foreground_fog, full_fog));
    imageStore(distance_output, pixel, vec4(scene_distance));

}
// Shared integration fades distant jitter and local coastal fog.
