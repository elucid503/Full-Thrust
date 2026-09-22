// Inherent optical properties per metre, red to blue, shared by the sea and by the ground where it
// stands in for the sea. Clear ocean loses red within metres and blue over tens; coastal water
// carries sediment and dissolved organics that turn it green.
uniform vec3 ocean_absorption = vec3(0.40, 0.065, 0.022);
uniform vec3 ocean_backscatter = vec3(0.0012, 0.0016, 0.0022);
uniform vec3 coastal_absorption = vec3(0.55, 0.14, 0.22);
uniform vec3 coastal_backscatter = vec3(0.008, 0.012, 0.011);
uniform float coastal_depth = 30.0;

void water_column(float depth, out vec3 absorption, out vec3 backscatter) {

    float coastal = exp(-depth / coastal_depth);
    absorption = mix(ocean_absorption, coastal_absorption, coastal);
    backscatter = mix(ocean_backscatter, coastal_backscatter, coastal);

}

// Along the view path, and for the light that had to come down through the column to reach a bed
// that was lit as if in air.
vec3 water_transmittance(vec3 absorption, vec3 backscatter, float path, float vertical) {

    return exp(-(absorption + backscatter * 20.0) * path - (absorption + backscatter) * 1.2 * vertical);

}

vec3 water_reflectance(vec3 absorption, vec3 backscatter) {

    return 0.33 * backscatter / absorption;

}
