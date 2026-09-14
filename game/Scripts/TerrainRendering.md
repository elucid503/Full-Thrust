# Close terrain

The launch complex stands on a 36 metre level plateau, blending back into natural terrain by
90 metres. Its paved apron is 48 by 40 metres. Grass and stones stop 0.15 metres beyond
that rectangle; trees stop 0.6 metres beyond it. The remaining level ground supports vegetation.
Close land gains parallax occlusion mapping, height-based rock/soil blending, material cavity occlusion and roughness.
The added material treatment fades between 45â€“140 metres and also fades when its texels cannot be
resolved. At grazing angles the depth offset tapers to avoid stretched horizons. This is a shallow
surface effect, not tessellation or collision displacement.

`TerrainDetail.gdshaderinc` marches 10â€“24 layers with a final intersection interpolation. Explicit
texture gradients keep mip selection stable inside the march. Color, normal and packed surface data
use the displaced coordinates together. `close_detail_strength=0` disables the new material layer;
`parallax_depth=0` disables just its depth offset. Both are available through `/tune?target=surface`.

`GroundScatter.cs` streams two deterministic layers, each on one cancellable worker. The detailed
layer uses 48-metre cells, with grass fading between 160–290 metres and stones between 230–330 metres.
A sparse layer of larger tufts and boulders uses 192-metre cells; tufts fade between 500–850 metres
and boulders between 800–1200 metres. These are persistent objects, so approaching them does not
replace their positions. The two layers stop streaming above 360 and 1280 metres respectively.

Grass density follows vegetation, aridity and overlapping 18/95-metre colony fields. Snow, water,
steep slopes and launch clearings exclude vegetation; stones have a separate slope limit. Roots
sample the shared survey and stones are partly buried. Each cell/material uses one MultiMesh with
frustum culling, bounded instance counts, a short arrival fade and distance shrink. Three meshes per
type, varying proportions, dry foliage patches and varied stone tint break up repetition. No engine physics bodies are created; streamed trunk and stone proxies are queried by the flight simulation. Surface color also varies at 7/28-metre scales, within the detail fade.

Grass uses bent blade geometry, root-to-tip shading, backlighting and root-pinned wind motion.
Stones use irregular geometry, varied proportions and local triplanar texture coordinates. Existing
trees gain restrained crown sway nearby. Wind follows simulation time, stays stable across floating
origin changes and wraps continuously after 4096 seconds. Pausing the simulation pauses wind.

# Material sources

The packed textures use the matching displacement, AO and roughness plates from ambientCG
[Rock030](https://ambientcg.com/view?id=Rock030) and
[Ground037](https://ambientcg.com/view?id=Ground037), the same sources as the existing color/normal
textures. These are CC0 assets. The RGB channels are height, AO, roughness, stored as linear data
with mipmaps. Two 1024-square RGB maps cost approximately 8 MiB including mipmaps.

Regenerate with `bash tools/pack-terrain-detail.sh [source-directory]`. The default input directory
is `tools/.cache/terrain-materials`, containing `Rock030_2K-JPG.zip` and `Ground037_2K-JPG.zip`.
The Bash wrapper uses Godot's ZIPReader and Image APIs; no extra image library is required.

Design references were [KSP Parallax Continued](https://github.com/Gameslinx/Parallax-Continued),
[Unreal landscape material blending and distance detail](https://dev.epicgames.com/documentation/en-us/unreal-engine/landscape-materials?application_version=4.27),
and [Godot MultiMesh performance guidance](https://docs.godotengine.org/en/stable/tutorials/performance/using_multimesh.html).
The implementation and scatter geometry are authored here; no Parallax code or assets are copied.

# Verification

Run `dotnet build game/FullThrust.Game.csproj`, `dotnet run --project tests`, then
`bash tools/godot.sh --headless res://Tests/GroundScatterChecks.tscn`.
The 50 scatter checks cover determinism, grounding, finite transforms, per-cell budgets, ocean/snow
and launch exclusions, cancellation, polar selection bounds and disabling distant streaming.
The existing simulation suite has 318 checks.

The debug bridge exposes `scatterCount`, `scatterCells`, `scatterPending`, `scatterFailures` and
render/frame timings. `/camera?free=true` detaches the camera for close ground inspection;
`/camera?free=false` returns to the vessel. Render captures and timing samples live in `.artifacts`.

The final 1280Ã—720 Vulkan capture on an RTX 4060 Ti had 19,157 nearby instances, zero worker
failures/pending jobs and a 4.56 ms average total viewport GPU time over 20 samples (4.45â€“4.78 ms).
This is a single-machine scene measurement, not an isolated cost or a hardware-wide performance
guarantee. At 15 km, all scatter cells unloaded; toggling the close material layer changed a settled
central screenshot sample by only 0.009/255 mean absolute channel value, within temporal-AA variation.

The extended-range coastal capture held 112,265 instances with no pending jobs or worker failures.
Total GPU time averaged 5.76 ms at 1280×720 on the RTX 4060 Ti (20 samples, 5.57–6.20 ms).


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
