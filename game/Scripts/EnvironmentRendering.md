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
Steam emission builds gradually with varied timing, direction and billow size. Rendering follows the
Decima/Nubis cloud model: overlapping billows form a smooth envelope, which is then remapped against
an inverted-Worley erosion field so the boundary is pushed inward while the interior keeps full
density. That remap, rather than a density multiply, is what yields cauliflower instead of smaller
spheres. The erosion field is shared by the whole emitter, so structure costs three texture lookups
per ray sample rather than any per billow.

Lighting uses a dual-lobe Henyey-Greenstein phase function summed over three of Wrenninge's
scattering octaves, each dimmer, less attenuated and more isotropic than the last, which stands in
for the multiple scattering that makes thick smoke bright rather than black. The powdered-sugar term
darkens edges facing the sun. Ambient is a sky-to-ground-bounce gradient over column height,
occluded by how deeply buried the sample is. Scattering is integrated analytically across each step
so transmittance stays energy-conserving at large strides. Sun shadowing exploits the billow
representation directly: the metaball falloff has a closed-form chord integral, so optical depth
toward the sun needs no second march, and it is refreshed every few samples and dropped once the
sample can no longer reach the camera. Rays march a constant world stride, so grazing rays cost
proportionally less. Individual billows grow, rise buoyantly and dilute with age; dust spreads closer
to the ground. Their birth positions remain fixed to the planet when the nozzle moves, and existing
billows disperse over twelve seconds after emission stops, by which time the column has risen well
above the vehicle.

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
within the visible plume length and an underwater terrain sample. Nozzles striking the same patch of ground share one
steam or dust volume, and terrain contact follows the survey and its local normal. Positions are retained in double-precision body coordinates and
converted through the floating origin for rendering.

These are bounded visual approximations, not a fluid or heat-transfer solver. Cloud disturbances
do not conserve condensate mass, cloud shadows currently affect the planet surface, and wave
displacement does not modify the simulation's sea-level collision surface. RCS does not generate
environmental wakes. Separated ocean strikes have independent steam emitters; the
strongest strike controls ocean surface deformation, and clustered power is clamped to a single
nozzle's worth before it drives the water. Billow dilution follows an entraining column rather than
the free-puff cube law, so the risen mass stays visible. Scattering octaves and the powder term are
art-directed approximations of multiple scattering, not a transport solution.

## Validation

Build the game and run `tests/FullThrust.Sim.Tests.csproj`. Shader compilation and visual quality
still need a running game instance; inspect the Godot log for worker failures.
