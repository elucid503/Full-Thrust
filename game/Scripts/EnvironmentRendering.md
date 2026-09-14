See [WaterBehavior.md](WaterBehavior.md) for the current shared wind, ocean shader, stable shorelines, F1 controls, and water/ground physics. It supersedes the fixed-wave and sea-level collision descriptions below.

# Environmental rendering

See [CloudPerformance.md](CloudPerformance.md) for the current cloud marcher, fog compositing,
minimal graphics controls and performance validation. It supersedes the older cloud quality
presets and fog ordering described below.

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

Cloud rays use deterministic, bounded integration with conservative coarse density checks and
fine samples in occupied intervals. Standard/High/Ultra use nearby strides of 24/16/12 metres;
plume intersections receive twice as many samples. Pixel footprint controls distant detail independently
of stride. Weather is interpolated over each coarse interval, not camera-relative 2 km bins.
Shape and erosion textures are 256³ and 128³ and are shared across flight resets. Additional billow
and edge erosion octaves add structure at roughly 140 m and 45 m scales. Their contribution fades
when pixels cannot resolve them. Density is stronger inside the cloud volume.

Camera-relative, compensated radial heights avoid subtracting two planet-sized floats in cloud and
fog samples. Factored sphere intersections use the precise eye altitude. Grazing rays retain both
cloud-deck intervals across the clear inner cavity. There is no camera-altitude cloud mode switch
or camera-distance opacity fade. Air is integrated to the cloud's optical centroid before compositing.

Cloud lighting uses a double-buffered 512², 64 km cache with three optical-depth height levels,
twelve solar samples, and a clear top boundary. Local sample density retains short-range self-shading;
outside the cache a direct broad solar sample supplies a smoothly blended fallback. Cache texture and
world coordinates are published together after rendering, preventing a recenter from pairing a new
coordinate frame with an old image. Ground shadows use a separate 16 km, 512² cache. Fog and air
shafts select prefiltered shadow channels according to their integration footprint; terrain keeps
the sharper channel. Both caches refresh at most twice a second unless the focus moves 2 km.

Main-engine nozzle position, direction, expanded plume size, and throttle feed the effects.
Eight persistent clearing capsules retain four seconds of local cloud disturbance. Birth and expiry
fade smoothly, with expansion and advection along the exhaust direction; compressed rims suggest
displaced condensate. Emission cadence allows each capsule to finish its lifetime before reuse. Ocean hits require a forward ray intersection
within the visible plume length and an underwater terrain sample. Nozzles striking the same patch of ground share one
steam or dust volume, and terrain contact follows the survey and its local normal. Positions are retained in double-precision body coordinates and
converted through the floating origin for rendering.

These are bounded visual approximations, not a fluid or heat-transfer solver. Cloud disturbances
do not conserve condensate mass, the cloud lighting cache approximates the vertical optical-depth profile, and wave
displacement does not modify the simulation's sea-level collision surface. RCS does not generate
environmental wakes. Separated ocean strikes have independent steam emitters; the
strongest strike controls ocean surface deformation, and clustered power is clamped to a single
nozzle's worth before it drives the water. Billow dilution follows an entraining column rather than
the free-puff cube law, so the risen mass stays visible. Scattering octaves and the powder term are
art-directed approximations of multiple scattering, not a transport solution.

Coastal fog and clouds share a premultiplied composite, ordered by whether the eye is above or
below the 900 m fog ceiling. Fog density tapers to zero between 400 and 900 m; it no longer fades
based on camera altitude. Thirty-two deterministic quadratic distance bins integrate extinction and
filtered cloud-cast shafts. Depth clips the final bin instead of redistributing every fog sample when
the camera moves. Rayleigh/Mie air remains active above it. F8 offers Clear, Coastal haze, and
Haze & sun shafts. Clear removes the additional fog and shafts, not the base atmosphere. Structure
and tree occlusion of fog is not simulated; layer ordering is an approximation for orbital grazing rays.

F8 includes a 50% FSR 2 Performance option alongside existing scales. Output and the interface remain
at display resolution. This option gives the RTX 4060 Ti additional room for the denser cloud pass;
cloud-detail controls remain independent of render scale.

Distant forest uses a second, visual-only instance of the forest streamer: 1 km cells, asynchronous
generation, and two-triangle canopy impostors out to 8 km. The same biome, woodland, coastline,
slope and clearing rules govern placement. Canopies blend in over 700â€“1600 m, then fade out over
6â€“8 km; detailed trees retain their existing close rendering and collision cache. The distant
layer approximates canopy coverage rather than reproducing every close tree or destroyed trunk.
It casts no shadow maps, has cell-level culling, and suspends visual streaming above its reach.

## Validation

Build the game and run `tests/FullThrust.Sim.Tests.csproj`. Shader compilation and visual quality
still need a running game instance; inspect the Godot log for worker failures.

Run `res://Tests/CloudVolumeChecks.tscn` with Vulkan for eleven GPU checks covering cloud density,
wake clearing/recovery/compression, deck bounds, and centimetre-level radial-height agreement with
double precision. These numerical checks do not replace visual zoom and frame-time validation.

## Shoreline and material refinement

CoastalLandforms.Elevation and Shaders/CoastalLandforms.gdshaderinc implement the same bounded
world-space coastal field. CPU geometry, contact queries, groundcover placement and the GPU sea
mask therefore use the refined contour. The height survey remains the source of large-scale
geography. CoastalLandformChecks.tscn compares 48 CPU/GPU probes (maximum observed error 0.039 m).
Terrain detail adds resolved dunes and gullies while preserving the refined sea boundary.
Ground materials add slope-dependent beach width, wet sand, geology variation, filtered strata
and sand ripples. Shoreline surf/foam has been removed; water waves remain.

MapView.Sync must run before Planet.Sync: the active camera and compensated cloud eye coordinates
must describe the same frame. Cloud macro distortion is evaluated at each density sample; caching
it across large orbital ray intervals caused view-dependent changes and is not used.
Cloud density sample spacing is unchanged. Low-altitude fog can bound the residual cloud
contribution before the cloud integration starts. The F8 contact-shading option controls SSAO;
its light affect is zero so occlusion applies to indirect illumination.


## Temporal reconstruction and close shorelines

AtmosphereComposite preserves coloured transmission under premultiplied blending while exposing
only real extinction as alpha. The atmosphere and clouds use fullscreen carrier quads; their analytic
ray geometry determines the physical shell. Camera far-plane clipping must not be used to hide the
atmosphere when diagnosing temporal artifacts.

GeometryHistory is a PostTransparent compositor effect. Godot FSR 2 uses resolved colour alpha as
reactivity, so this effect caps it only near close foreground depth discontinuities. RGB is unchanged.
Use GetColorLayer(view, false) and GetDepthLayer(view, false): the one-argument compatibility methods
may return MSAA buffers. The imported GeometryHistory.glsl must stay a compute shader resource.
Native TAA bypasses the effect. RenderingStabilityChecks.tscn includes exact GPU colour-preservation
and silhouette-mask assertions in addition to cloud mip and atmospheric-composite checks.

FilteredVolume creates 3D mip levels for the shared base cloud noise once after generation. Each
mip is a stack of independent L8 slices, with both slice dimensions and stack depth halved. Planet
rebinds the finished texture to clouds, ground receivers and cached shadow/light generators.

ShorelineSampling measures slope at fixed world offsets per vertex. Within four-metre mesh spacing,
shore masks use the separate smooth CPU coastal-height channel (CUSTOM1), before terrain relief clamps; the global per-pixel contour takes over by sixteen metres.
This avoids sub-metre floating-point stairs in planet-sized fragment coordinates without increasing
terrain subdivisions. GraphicsOptions saves the optional 2x MSAA coverage setting; the benchmark tool
records and accepts GeometryAntialiasing explicitly.


## Coherent cloud wind

CloudWind supplies a steady spherical flow, 10 m/s northeast at the launch coast. Every cloud
noise octave and the broad weather map use the same material-coordinate matrix, so formations
translate coherently rather than changing shape through unrelated texture scroll rates. The
CPU composes inverse wind and planet rotation in double precision before uploading the matrix.
The field preserves altitude, works across poles and map/flight cameras, follows simulation time,
and freezes during pause. It is a visual weather preset; vessel aerodynamic air velocity is unchanged.

Published shadow/lighting cache frames move with the flow between refreshes. Pausing skips their
periodic refresh; fast simulation-time changes can request an earlier refresh. The eight existing
plume disturbance capsules are carried and rotated by the same wind until they recover.
Ultra uses six density samples per coarse interval, Standard and High use eight, and plume
intersections use sixteen. The count never switches with camera distance. Cloud volume textures,
density and coverage remain unchanged. CloudWindChecks exercises both GPU coordinate frames,
normal/reversed/accelerated time, poles and long saves; CloudVolumeChecks retains the wake checks.
