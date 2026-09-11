uniform float environment_time = 0.0;
uniform int wake_count = 0;
uniform vec4 wake_centres[8];
uniform vec4 wake_axes[8];
uniform vec4 water_impact = vec4(0.0);
uniform float impact_strength = 0.0;

float plume_clearance(vec3 p) {

    float clearing = 0.0;
    float compression = 0.0;
    for (int i = 0; i < wake_count; i++) {

        vec3 offset = p - wake_centres[i].xyz;
        float along = clamp(dot(offset, wake_axes[i].xyz), 0.0, wake_axes[i].w);
        float radius = wake_centres[i].w + along * 0.12;
        float distance = length(offset - wake_axes[i].xyz * along);
        clearing = max(clearing, 1.0 - smoothstep(radius * 0.35, radius, distance));
        compression = max(compression, smoothstep(radius * 0.7, radius, distance)
            * (1.0 - smoothstep(radius, radius * 1.5, distance)));

    }
    return clearing - compression * 0.3 * (1.0 - clearing);

}
