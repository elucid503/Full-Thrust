// Hull distance/normal fields live in receiver coordinates. Every ray-march sample is bent
// around the hull, not clipped against a camera-facing impact sprite.
uniform int contact_count = 0;
uniform sampler2D contact_field0 : filter_linear, repeat_disable;
uniform sampler2D contact_field1 : filter_linear, repeat_disable;
uniform vec4 contact_domain0;
uniform vec4 contact_domain1;
uniform mat4 contact_transform0;
uniform mat4 contact_transform1;
uniform vec3 contact_bounds_min0;
uniform vec3 contact_bounds_max0;
uniform vec3 contact_bounds_min1;
uniform vec3 contact_bounds_max1;
uniform int contact_engine_count0 = 0;
uniform int contact_engine_count1 = 0;
uniform sampler2DArray contact_hardware0 : filter_linear, repeat_disable;
uniform sampler2DArray contact_hardware1 : filter_linear, repeat_disable;
uniform mat4 contact_engine_transforms0[32];
uniform mat4 contact_engine_transforms1[32];
uniform vec4 contact_engine_domains0[32];
uniform vec4 contact_engine_domains1[32];
uniform vec4 contact_engine_bounds0[32];
uniform vec4 contact_engine_bounds1[32];

vec3 extend_surface(vec3 value, vec2 offset, bool distance_bound) {
    float outside = length(offset);
    if (outside > 0.0) {
        if (distance_bound) {
            // Conservative step length only: this V-shaped bound is not a visual distance.
            value.x = max(outside, value.x - outside);
        } else {
            // Continue from the closest surface point, not from the edge of the texture.
            // Using the marching bound here creates a phantom collision above the receiver.
            vec2 delta = offset + normalize(value.yz + vec2(0.000001)) * max(value.x, 0.0);
            value = vec3(length(delta), normalize(delta));
        }
    }
    return value;
}

vec3 contact_surface(sampler2D field, vec4 domain, vec3 p, bool distance_bound) {
    vec2 at = vec2(length(p.xz), p.y);
    vec2 uv = (at - domain.xy) / domain.zw;
    vec3 value = textureLod(field, clamp(uv, vec2(0.0), vec2(1.0)), 0.0).rgb;
    value = extend_surface(value, (uv - clamp(uv, vec2(0.0), vec2(1.0))) * domain.zw, distance_bound);
    // Conservative reconstruction keeps sub-texel interstage walls solid. The correction is
    // bounded by the field's spatial resolution, not by the size of the plume.
    value.x -= length(domain.zw / vec2(textureSize(field, 0))) * 0.5;
    return value;
}

vec4 receiver_surface(int receiver, vec3 p, bool distance_bound) {
    vec3 value;
    if (receiver == 0) { value = contact_surface(contact_field0, contact_domain0, p, distance_bound); }
    else { value = contact_surface(contact_field1, contact_domain1, p, distance_bound); }
    vec2 radial = p.xz / max(length(p.xz), 0.0001);
    vec4 surface = vec4(value.x, radial.x * value.y, value.z, radial.y * value.y);
    int count = receiver == 0 ? contact_engine_count0 : contact_engine_count1;
    for (int i = 0; i < count; i++) {
        vec4 bound = receiver == 0 ? contact_engine_bounds0[i] : contact_engine_bounds1[i];
        if (length(p - bound.xyz) - bound.w > surface.x) { continue; }
        mat4 mount = receiver == 0 ? contact_engine_transforms0[i] : contact_engine_transforms1[i];
        vec4 domain = receiver == 0 ? contact_engine_domains0[i] : contact_engine_domains1[i];
        vec3 local = (mount * vec4(p, 1.0)).xyz;
        vec2 uv = (vec2(length(local.xz), local.y) - domain.xy) / domain.zw;
        vec3 sample_at = vec3(clamp(uv, vec2(0.0), vec2(1.0)), float(i));
        if (receiver == 0) { value = textureLod(contact_hardware0, sample_at, 0.0).rgb; }
        else { value = textureLod(contact_hardware1, sample_at, 0.0).rgb; }
        value = extend_surface(value, (uv - clamp(uv, vec2(0.0), vec2(1.0))) * domain.zw, distance_bound);
        value.x -= length(domain.zw / vec2(128.0, 256.0)) * 0.5;
        if (value.x < surface.x) {
            radial = local.xz / max(length(local.xz), 0.0001);
            vec3 normal = transpose(mat3(mount)) * vec3(radial.x * value.y, value.z, radial.y * value.y);
            surface = vec4(value.x, normal);
        }
    }
    return surface;
}

void contact_hull(int receiver, vec4 domain, mat4 transform, vec3 original,
        float nozzle_radius, float sample_width, inout vec3 flow, inout float mask, inout float wake) {
    vec3 low = receiver == 0 ? contact_bounds_min0 : contact_bounds_min1;
    vec3 high = receiver == 0 ? contact_bounds_max0 : contact_bounds_max1;
    float thickness = max(nozzle_radius * 0.95, 0.12);
    float margin = thickness * 6.0;
    if (any(lessThan(original.xz, low.xz - vec2(margin))) || any(greaterThan(original.xz, high.xz + vec2(margin))) || original.y > high.y + margin) {
        return;
    }
    vec3 local = (transform * vec4(original, 1.0)).xyz;
    vec4 surface = receiver_surface(receiver, local, false);
    // Integrate the skin transition over a sample footprint; a sub-step hard edge produces
    // stationary stipple and vertical teeth when viewed just below the capsule's rim.
    mask *= smoothstep(0.0, max(thickness * 0.2, sample_width), surface.x);
    if (surface.x <= 0.0) { return; }
    vec3 normal = normalize(transpose(mat3(transform)) * surface.yzw + vec3(0.000001));
    float hull_radius = domain.z * 0.5;
    float wrap = exp(-surface.x / thickness);
    // A continuous expansion of the incident footprint creates a sheet. Subtracting a fixed
    // radial displacement folds coordinates through the axis and creates a second bright ring.
    float spread = hull_radius * wrap;
    float jet_width = max(nozzle_radius + max(-original.y, 0.0) * 0.035, 0.05);
    float widening = 1.0 + spread / jet_width;
    flow.xz /= widening;
    flow.y -= max(normal.y, 0.0) * thickness * wrap * 0.25;
    // Gas expands and cools along the surface instead of retaining the core's brightness.
    mask *= (1.0 + wrap * max(normal.y, 0.0)) / sqrt(widening);

    // Trace upstream in hull space to distinguish a real lee wake from gas merely near a hull.
    // Enter at the receiver bounds, then advance only with conservative distance samples.
    // The visual extrapolation above must never supply a ray step or skip a thin wall.
    float travel = max(thickness * 0.08, low.y - original.y);
    float limit = max(min(-original.y, high.y - original.y), 0.0);
    vec3 upstream = mat3(transform) * vec3(0.0, 1.0, 0.0);
    float visibility = 1.0;
    if (limit > travel) {
        if (all(greaterThanEqual(original.xz, low.xz)) && all(lessThanEqual(original.xz, high.xz))) {
            for (int j = 0; j < 16; j++) {
                if (travel >= limit) { break; }
                float d = receiver_surface(receiver, local + upstream * travel, true).x;
                visibility = min(visibility, smoothstep(thickness * 0.08, max(thickness * 0.5, sample_width), d));
                if (d < thickness * 0.08) {
                    // A blocked streamline cannot regenerate its original hot jet behind a
                    // solid receiver. Turbulent gas must arrive around the exposed edges.
                    mask = 0.0;
                    return;
                }
                travel += max(d * 0.8, thickness * 0.08);
            }
        }
    }
    // Resolve the finite jet's silhouette continuously instead of binary bands at the rim.
    mask *= visibility;
    wake = max(wake, wrap * 0.9);
}

vec3 contact_flow(vec3 p, float radius, float sample_width, out float mask, out float wake) {
    vec3 flow = p;
    mask = 1.0;
    wake = 0.0;
    if (contact_count == 0) { return p; }
    if (contact_count > 0) { contact_hull(0, contact_domain0, contact_transform0, p, radius, sample_width, flow, mask, wake); }
    if (mask <= 0.001) { return flow; }
    if (contact_count > 1) { contact_hull(1, contact_domain1, contact_transform1, p, radius, sample_width, flow, mask, wake); }
    float phase = effect_time * 4.0 + p.y / max(radius * 2.0, 0.1);
    flow.xz += wake * radius * 1.05 * vec2(sin(phase + p.z / max(radius, 0.1)), cos(phase * 0.83 + p.x / max(radius, 0.1)));
    return flow;
}
