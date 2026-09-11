# Environmental rendering

The existing terrain geometry and collision survey remain shared. Below 20 km above sea level,
the ground shader combines filtered rock/soil colour and normals with procedural material variation.
It blends back to the previous material from 20 to 25 km; at and above 25 km the additional
material contribution is zero. Biome geography, snow, and the orbital palette are retained.
Procedural sampling is body-fixed so rebasing, patch changes, and cube-face boundaries do not
move its pattern. Subpixel octaves fade using the screen-space footprint; fully unresolved octaves stop sampling.

Water uses four gravity-wave components with analytical slopes and vertex displacement. Wavelengths
range from 16 to 128 metres. Mesh spacing and pixel footprint suppress unresolved components;
shallow water damps displacement. The same body-fixed field runs across all six terrain faces.
Plume strikes add a depression, outgoing oscillations, foam, and a depth-clipped steam volume.
The steam rises through advected noise and fades after the nozzle leaves or shuts down.

Cloud ray budgets vary from 64 near samples to 32 distant samples. Adjacent samples reuse lighting
only when density and separation permit; primary density and extinction are still integrated at
every sample. Empty samples skip erosion, and distant/light samples omit fine noise. Factored
sphere intersections reduce cancellation near the cloud layer. Grazing rays march both deck intervals
across the clear inner cavity, avoiding a missing far-side underside. Weather and billows vary the
base, and aerosol extinction reduces distant contrast. Ground and ocean cloud shadows
integrate optical depth along the solar ray through the spherical deck, using the same density
field and plume clearings as the visible clouds.

Main-engine nozzle position, direction, expanded plume size, and throttle feed the effects.
Eight persistent clearing capsules retain four seconds of local cloud disturbance; their rims
increase density to suggest displaced condensate. Ocean hits require a forward ray intersection
within twice the visible plume length and an underwater terrain sample. One steam volume represents
the most recent water strike. Positions are retained in double-precision body coordinates and
converted through the floating origin for rendering.

These are bounded visual approximations, not a fluid or heat-transfer solver. Cloud disturbances
do not conserve condensate mass, cloud shadows currently affect the planet surface, and wave
displacement does not modify the simulation's sea-level collision surface. RCS does not generate
environmental wakes. Multiple simultaneous ocean strikes share the single steam emitter.

## Validation

Build the game and run `tests/FullThrust.Sim.Tests.csproj`. Shader compilation and visual quality
still need a running game instance; inspect the Godot log for worker failures.
