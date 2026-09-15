# Water and weather

F1 includes Calm / clear (0 m/s), Fair (10 m/s), Breezy (18 m/s), and Gale / overcast
(32 m/s). Presets set cloud coverage as well as wind. The wind is southwesterly at the
launch coast, tangent to the globe, and fades out in the upper atmosphere. Settings last
until the flight restarts. Pausing freezes environmental motion and transitions.

Wind approaches its target with a five-second time constant, clouds with eight seconds,
and wave amplitude with twenty-five seconds. Cloud position uses integrated wind distance,
including earlier settings, so neither editing wind nor refreshing a shadow cache jumps
cloud phase. Aerodynamics samples the same wind at each integration step. Surface contact
and water remain relative to the rotating planet, independently of atmospheric wind.

`sim/Ocean.cs` and `Shaders/OceanField.gdshaderinc` evaluate the same four spherical
gravity waves (roughly 19–137 m). Irregular wavelength spacing, curved crests, and spatially
varying packet strength prevent the aerial reflection from forming a repeating grid.
Integer angular cycles close each wave around the planet;
analytical gradients and orbital velocities drive shading and hull interaction. CPU-reduced
time phases retain precision during long missions. Wave directions spread around the wind;
amplitudes and small-wave roughness respond to sea state. Each component is limited to 8%
of local depth, so the combined trough cannot penetrate the seabed. Geometry fades unresolved
waves and follows terrain LOD morphing; shading resolves waves by pixel footprint.

Close detail uses 12 curved wave packets and three independently advected irregular bands
at 80, 26, and 8 cm scales. The bands use analytic noise derivatives rather than a tiled normal
texture, breaking up the long regular ridges in reflections. Their time and origin phases are
reduced in doubles before the shader uses small local coordinates. Camera movement and
floating-origin rebases therefore preserve the pattern. Unresolved wave slopes contribute to
roughness; the specular lobe no longer forces a 0.158 minimum effective roughness on water.
As the pixel footprint grows, 6.3 m and 23.7 m irregular bands take over distant normals.
Coherent swell slopes blend into roughness between 0.5 and 3 m per pixel, preventing
the aerial sun reflection from resolving only four repetitive wave trains. This adds no textures.

The dispersion uses the deep-water gravity-wave relation described in
[NOAA's wave measurement procedures](https://www.ndbc.noaa.gov/wavemeas.pdf).
The shoreline is fixed at the datum. There is no tide or shoreline wash. Every fragment
uses the same geographic elevation field; it does not blend with interpolated vertex heights
as mesh LOD changes. Near sea level, monotone cubic survey interpolation rounds cell corners
without overshooting the survey or removing its island peaks. Physics uses the same curve.
The coastal path uses 16 texel fetches, blending back to four full-precision bilinear fetches
between 8 and 28 metres from sea level. No vertex texture fetches or extra geometry are added.
The wet-sand band is static. Offshore waves taper to zero at the coast
and cannot flood dry land. This does not model beach erosion, refracting/breaking surf,
rain, lightning, or a circulating ocean.

Water normals follow the ocean's radial surface and wave gradient even on triangles next to
sloping land. Roughness derivatives use that same normal, avoiding reflection bands shaped like
terrain triangles. Shallow tint is limited to 12% strength, a 12-metre depth scale, and a shore
distance fade from 40 to 180 metres. It no longer paints a green shelf across broad shallow areas.

Water exhaust contact uses a small Gaussian pressure depression, local aeration, and a low,
wind-swept spray sheet. It fades with a 0.25-second time constant after contact stops and
expires after 1.5 seconds. Water does not emit the old spherical steam puffs. Dry-ground
dust and launch-pad deluge retain their volumetric emitters. Effects follow simulation time,
including pause, and clustered nozzles aggregate only within the same rendered frame.
These effects are bounded visual approximations rather than a fluid solver.

Water contact integrates displaced volume over 24 hull slices, with circular segment
centroids for tilted hulls. Buoyancy acts at those centroids; exponential point-drag impulses
damp translation and rotation without reversing slip on light debris. Outer hulls are treated
as watertight: flooding, open engine bays, and compartment leaks are not modeled.
Capsules and empty stages can float, while bodies denser than their displaced water can sink
and contact the actual seabed. Soft entry remains playable. Water-relative closing speed above
10 m/s, or tangential speed above 25 m/s while immersed, is damaging; these are gameplay
thresholds, not a structural splashdown certification model. Invulnerability suppresses damage
while retaining forces. The flight-ending altitude guard now checks solid terrain/seabed.

The ground drift fix consumes the remainder of each swept contact step, which was previously
lost when a resting vessel was rewound to its starting position. Sliding friction and finite
contact-patch rolling resistance dissipate surface motion. Contact and depenetration tolerances
overlap, avoiding the previous cycle of tiny falls and corrections at rest.

## Validation

- `dotnet build game/FullThrust.Game.csproj --no-restore`
- `dotnet run --project tests --no-restore`: weather continuity, aerodynamic wind, wave velocity,
  depth limits, fixed shoreline, capsule and detached-stage flotation, hard/invulnerable entries,
  rotating-ground rest, and sliding friction alongside the existing simulation suite.
- Godot with Vulkan, `res://Tests/OceanChecks.tscn`: CPU/GPU height and slope comparison at five
  global positions, three depths and three times (through ten million seconds), actual
  `Flight.Advance` splashdown, ripple phase after origin changes, and F1 preset/layout checks.
- Godot with Vulkan, `res://Tests/OceanVisualChecks.tscn`: fair/gale/F1 screenshots in `.artifacts`,
  coastal water frame time, and terrain worker failure count.
- Existing `CloudWindChecks.tscn` and `FlightTerrainChecks.tscn` cover cloud motion and ground contact.
- `WaterRegressionChecks.tscn` samples the rendered land/water mask against geographic
  coordinates at near range, after 6.5 km of camera travel/rebasing, and after another camera
  direction and elapsed time. It also captures close moving exhaust and verifies water wakes
  contain no spherical puffs. Pass `-- --close-only` to run just the close exhaust capture.
- `WaterRegressionChecks.tscn -- --stress` exercises 4,500 frames of rapid coastal travel,
  dives from 2 km, below-surface crossings, and stops for terrain uploads to finish.
- `OceanVisualChecks.tscn -- --glint` faces the sun reflection at close range; add `--native`
  to compare against the selected upscaling quality without changing saved settings.
- `tools/trace-water.ps1` launches normal gameplay with timestamped engine and console logs
  under `.artifacts`, including camera coordinates, altitude, terrain job counts, and process
  exit code. It enables `FT_SURFACE_TRACE` only for that launch. Use these logs to investigate
  native faults that do not reach the usual managed error handler.

GPU comparisons allow 12 cm height and 0.035 slope error for planet-sized float coordinates;
near the launch coast the measured errors are smaller. Physics always uses doubles and full
wave resolution; coarse distant geometry intentionally suppresses waves it cannot represent.
