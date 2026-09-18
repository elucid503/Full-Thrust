# Terrain rendering

The terrain mesh, geographic survey, coastlines, LOD morphing and collision queries share
one surface. Rendering keeps the biome palette, slope-based rock/soil selection, snow,
wet sand and one mip-filtered colour/normal texture pair per material.

Surface parallax, packed height/AO/roughness maps, three-scale material layering,
triplanar cliff detail and procedural micro-relief are restored. TerrainDetail.gdshaderinc
uses 8-16 parallax layers with explicit texture gradients and grazing-angle fading.
The packed soil/rock maps contain height, ambient occlusion and roughness, using the
CC0 ambientCG Ground037 and Rock030 materials. The parallax_depth and close_detail_strength
uniforms control the effect. Individual night-light dots remain removed; night imagery
still supplies city lights. Forest and ground-scatter streaming are unchanged.

The launch complex stands on a 36 metre level plateau, blending back into natural terrain
by 90 metres. Grass, stones and trees retain their launch-clearing exclusions.

## Verification

Build the game, run the simulation suite, then run WaterRegressionChecks, OceanChecks,
PolishVisualChecks and TransitionChecks with Godot .NET / Vulkan. These cover coastline
agreement, wave/physics agreement, surface captures, healthy terrain workers, map transitions
and scene restart. Captures and logs are written under game/.artifacts.

# Trees, impacts and lighting

Quaternius's CC0 Stylized Tree Pack replaces the procedural trees with authored broadleaf, birch and
pine FBX models and their original leaf textures. See Assets/Trees/README.md and License.txt for
source and licensing. TreeAssets bakes scene transforms, normalizes each tree to 10.5 m, retains leaf
UVs and merges each model into one indexed surface with generated distance LODs. Leaf textures use mipmaps and alpha-border
correction; bark uses a procedural material. They are lightweight stylized assets, not photogrammetry.

Flight now queries the hull against the same terrain height field as rendering. GroundCollision
sweeps hull support points, finds first contact, resolves penetration and applies impulses before the
vessel centre enters terrain. Hard ground contacts above 8 m/s cause impact damage. Invulnerability
suppresses damage, not physical contact. Wrecks remain visible and fixed to the rotating surface.
Supported vessels can lift off.

Before scatter queries, Forest/GroundScatter ensure the cells intersecting the swept hull exist.
Only missing contact-critical cells generate synchronously; normal visual streaming remains on
workers. Teleports and detached cameras can no longer leave holes in nearby tree/rock collision.
Cells remain cached and are released through normal selection. Trees use trunk capsules and crown ellipsoids sized from the imported mesh bounds. Hard scatter impacts above 12 m/s damage the vessel; sufficient energy also destroys
the obstacle. Small pebbles below 20 cm radius and grass remain cosmetic.

Swept hull spheres are conservative collision approximations, not triangle-mesh collision.
Broad-phase cell/instance checks reject distant obstacles; hull samples use stack storage.
Effects retain instancing, with four visual rock fragments and root-pivoted tree falls. Effects have no
physics bodies, stop with simulation time, and are capped at 64 per layer. Tree effects expire after
12 seconds, rock effects after 1.2 seconds; cell unload releases effects. Destruction IDs survive
cell reload within a Planet session but are not saved to disk.

Terrain applies cloud attenuation to direct light rather than material albedo, with Burley diffuse,
GGX highlights and cavity AO on ambient fill. Sky fill continues through dusk; two blended shadow
cascades cover 180–300 metres.

Verification:
- dotnet build game/FullThrust.Game.csproj --no-restore
- dotnet run --project tests --no-restore (337 checks)
- Godot res://Tests/GroundScatterChecks.tscn (84 collision, destruction and asset-budget checks)
- Godot --headless res://Tests/FlightTerrainChecks.tscn (11 actual-flight regression checks)
- Godot res://Tests/TerrainVisualChecks.tscn (Vulkan screenshot and 60-sample GPU mean)

FlightTerrainChecks creates the real Main/Flight, flies the vessel into terrain and an unstreamed tree
cell, and verifies damage, hull clearance, invulnerable collision and wreck anchoring. The cold-cell
tree scenario measured 9–22 ms for the entire multi-step scenario on this machine, including first
generation. That is a cold teleport cost, not a per-frame budget.

The first imported-tree Vulkan capture measured 6.62 ms total viewport GPU time at 1280×720 on an
RTX 4060 Ti, with 3,067 trees and 125,623 scatter instances, no pending jobs and no terrain failures.
This is a single-scene measurement; it is not an isolated tree cost or a hardware-wide guarantee.

The mipmapped imported-tree pass measured 5.95 ms under the same setup before adding mesh LODs.

With mesh LODs enabled, the final viewport GPU mean was 6.13 ms (60 samples, same scene).

# September terrain and transition polish

Coastal relief approaches the datum continuously instead of being clipped at the shoreline.
Steep faces use triplanar rock color and normals, with extra texture reads only for resolved steep
land. Noise-contour canals have been removed; smaller coves, broad marsh depressions and restrained
bars retain surveyed waterways. Monotone cubic survey sampling rounds sharp coastal corners while
preserving sample heights; collision and fragment shading use the same curve.

Map view has its own coarse terrain tree. The flight tree and vegetation keep tracking the flight
camera while the map is open. A loading cover waits for terrain, vegetation and cloud textures,
then fades in. Simulation time stays still during loading; floating-origin rebasing continues.

Restart shares immutable survey storage, the shoreline GPU texture, authored tree meshes and the
atmosphere lookup. Launch plateaus remain local to each flight. Vegetation workers return up to
eight cells per handoff during loading and keep their usual single-cell rate during normal flight.
Workers retain their own flight's terrain reference and canceled work is never adopted.

TransitionChecks.tscn verifies retained patches, repeated map round trips, free-camera restoration
after a distant paused move, loading-time simulation hold, and a real scene restart. Local restart
checks measured 5.7-9.2 seconds; map-to-flight returns took 0.25-0.36 seconds including the fade.
These timings depend on rendering load. PolishVisualChecks.tscn captures coastal water, southern
Florida and island water. CoastalLandformChecks.tscn compares procedural detail and curved survey
sampling against the GPU; maximum measured curved-survey disagreement was 0.038 metres.

Native-crash validation: the full-scene capture encountered intermittent AccessViolation failures
inside terrain/vegetation calculations on the installed .NET 10 host. A process with tiered JIT
disabled completed all three captures with zero worker failures. The game runtime configuration
now selects single-tier compilation; the following normal launch also completed those captures.
This is a runtime workaround supported by local reproduction, not a diagnosis of an upstream bug.
Configuration reference: https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation
