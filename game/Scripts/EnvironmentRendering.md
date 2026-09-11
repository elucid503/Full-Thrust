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
Plume strikes now force a persistent linear gravity-wave grid (`SurfaceResponse`) instead of an
animated ring. Height and staggered horizontal velocity advance at 60 Hz; foam is transported with
the water. Pressure displaces water without removing volume, and the surface rebounds on shutdown.
An absorbing border damps outgoing waves. The effective layer depth is 2 m, with a smoothly limited
pressure head to keep this local approximation stable under rocket-scale forcing.

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
increase density to suggest displaced condensate. Sixteen curved-path samples and seven refinement
steps locate terrain contact. The hot wall jet uses the same hit and a terrain-derived normal. The contact trace and falling
droplets also sample the evolving water height, so the jet follows its own depression.
Cooling gas can disturb surfaces beyond the luminous plume, with increasing mixing and falling pressure.

Up to three body-fixed 128 m regions retain surface and airborne state as an engine moves. Each
region accepts eight strikes and carries 256 moving gas/spray/dust parcels. Ejection follows impact
pressure and incidence; drag slows motion relative to the ground, droplets fall and deposit foam,
vapour rises and dilutes, and dust spreads and settles. Depositing droplets also return a small
momentum impulse to the water. Region positions survive floating-origin rebasing.

The renderer deposits parcel density into a filtered 64 x 32 x 64 volume at 20 Hz and ray-marches
emission/extinction with self-shadowing. Water textures update at 30 Hz. Density kernels are wider
than a voxel to avoid blocks. Terrain surveys run asynchronously; simulation buffers and texture
storage are reused, and empty volumes stop uploading. Regions retire after their spray and waves
have decayed; a fourth distant region replaces the oldest to bound memory and work.

This is a reduced local response, not a multiphase CFD or heat-transfer solution. Water uses an
effective depth rather than resolving deep-water dispersion; pressure-head saturation and optical
parcel mass are model approximations. Surface waves do not change vessel collision/buoyancy. The
ambient air still co-rotates with the planet; there is no weather solver. Launch hardware is not a
surface-flow collision mesh. Existing hull-obstacle displacement and cloud clearing remain enabled.

## Validation

Build the game and run `tests/FullThrust.Sim.Tests.csproj`. Simulation tests cover rest, outward
momentum, symmetry, rebound, propagation, dissipation, dry-cell barriers, simultaneous sources,
and interior water-volume conservation. Shader compilation and visual quality still need a running
game instance; inspect the Godot log for worker failures. The debug bridge exposes `surfaceParcels`,
`surfaceWaveHeight`, `surfaceMs`, and `surfaceFailures`. Visual state advances while flight is paused.

Background references: [Clawpack depth-averaged flow solvers](https://www.clawpack.org/sphinx-versioning/riemann/Shallow_water_Riemann_solvers.html)
and [NASA plume/water interaction](https://www.nas.nasa.gov/SC21/research/project25.html).
The implementation is a reduced game model, not a reproduction of those high-fidelity solvers.
