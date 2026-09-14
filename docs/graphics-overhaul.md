# Graphics overhaul requirements

Requirements gathered on 2026-09-13. Implementation proceeds in stages, beginning with performance and pad/liftoff visuals.

## Visual direction

- Full visual overhaul, including the interface and presentation.
- Cinematic realism grounded in natural lighting and physical behavior. References: heavily modded KSP and real SpaceX footage.
- Highest priority: launch pad and liftoff. Terrain, water, rocket materials/plumes, clouds, and reentry need significant improvement. High orbital views already work relatively well.
- Cape Canaveral-like coastal wetlands, scrub, grass, and sandy ground around the launch site.
- Detailed launch pad, tower, and exhaust handling, surrounded mostly by nature.
- One excellent natural cloud system and lighting setup first; a broader weather system is not the initial objective.
- Rework vehicle appearance toward Falcon 9 / Dragon: painted tanks, dark interstage, detailed engines, and capsule.
- Simulated dimension changes are allowed where necessary, while keeping broadly similar flight behavior.
- Reentry should improve the shock layer and wake, heat-shield glow, scorching, and illumination on the vehicle.
- Natural camera treatment, including subtle motion blur and launch shake.
- SpaceX-inspired minimal interface with a small core flight HUD and expandable contextual telemetry panels.
- Free licensed assets and original project assets are acceptable.

## Performance target

- 60 FPS at 2560 x 1440 fullscreen on this machine.
- Highest quality that sustains the target, with configurable settings.
- Optional lower internal resolution and upscaling are acceptable if the image remains sharp and temporally stable.
- No specific user-reported slowdown; establish a baseline across representative flight scenes before choosing optimizations.

Machine inspected locally:

| Component | Observed specification |
| --- | --- |
| CPU | Intel Core i9-13900K, 24 cores / 32 logical processors |
| GPU | NVIDIA GeForce RTX 4060 Ti, 8 GB VRAM (8188 MiB reported) |
| RAM | Approximately 32 GB (34,087,919,616 bytes OS-visible) |
| Display | 2560 x 1440, approximately 165 Hz (164 Hz reported by WMI) |
| OS | Windows 11 Home, 10.0.26200 |
| NVIDIA driver | 610.74 |
| Engine | Godot 4.7.2 .NET, Vulkan Forward+ |

## Initial measurements

Existing source built successfully with zero warnings and zero errors using `dotnet build game/FullThrust.Game.csproj --no-restore`.

The game was run in fullscreen at the exact requested resolution. These are preliminary debug-build observations from the existing debug bridge, not controlled release-build benchmark results or isolated shader costs. Other desktop GPU activity was not controlled. GPU times measure the main viewport and are not interchangeable with total frame time.

| Scene | GPU mean | GPU sample range | Last reported FPS | Last rolling frame P95 / P99 |
| --- | --- | --- | --- | --- |
| Settled default pad | 25.10 ms | 23.82-27.25 ms | 35 | 29.10 / 29.92 ms |
| Liftoff, approximately 23-297 m altitude | 25.11 ms | 18.78-32.80 ms | 34 | 34.99 / 37.08 ms |
| Paused cloud scene, 3500 m altitude | 16.55 ms | 14.90-18.06 ms | 54 | 19.86 / 20.41 ms |

Pad/cloud observations used 20 samples each; liftoff used 36, with approximately 250 ms between requests. The bridge's frame percentiles come from a rolling buffer of up to 600 frames, so liftoff percentiles can include preceding pad frames. Pad and cloud streaming queues were empty at the final samples. Pad had 127,331 scatter instances and 3,272 trees. Last pad render CPU time was 1.07 ms and simulation flight time was 0.07 ms. These observations suggest GPU rendering is the first bottleneck to investigate, without establishing the cost of individual passes.

Raw JSON measurements and PNG captures are saved locally under `game/.artifacts/graphics-baseline-*-1440.*`; that directory is ignored by Git. The benchmark game instance was closed after capture. A prior 1280 x 720 terrain visual check measured 9.75 ms mean GPU time; its different camera and resolution prevent direct comparison with the fullscreen pad measurements.

The debug bridge failed to bind inside the sandbox, then worked when the local game was run outside it. No source change was necessary.

## Implementation sequence

1. Measure rendering costs, reduce repeated atmospheric/reflection work, provide graphics settings, and improve launch-site and liftoff presentation.
2. Rework the coastal landscape, vegetation, water, and natural cloud appearance together.
3. Extend real-vehicle detailing and reentry materials, lighting, and wakes.
4. Redesign the flight HUD and contextual panels around the minimal SpaceX-inspired direction, with optional camera treatment.

Each stage needs running-game visual checks and performance measurements. A lower GPU timing alone does not establish sustained 60 FPS.

## Stage 1 implementation

- Authored and batched a 36 m service tower with platforms, stairs, railings, pipework, lightning mast, and an access platform. Added a jointed concrete apron, blast walls, a curved segmented flame deflector, deluge plumbing and tanks. Eight material batches carry the static structure rather than one draw per beam. The shared terrain plateau expands to support the apron; vegetation resumes beyond its clearing.
- Pad exhaust contacts now feed two directed steam outlets. They reuse the existing bounded volumetric billow renderer. This is an art-directed approximation of exhaust deflection and water deluge, not fluid simulation or a simulated water supply.
- Added dielectric white tank paint, a dark first-stage interstage, restrained clearcoat, stronger daylight ambient fill, wider initial camera framing and optional low-amplitude launch shake. Vehicle dimensions and flight performance remain unchanged in this stage.
- Replaced per-ray sunlight marches in the atmosphere shader with a 256 x 128 RGB float optical-depth lookup (384 KiB). Its CPU generation uses doubles and 64 integration steps. Height/cosine sampling concentrates precision near the surface and horizon. Relevant live atmosphere tuning regenerates the lookup. View scattering and planet occlusion still run per pixel.
- Changed the reflection probe to amortize cubemap updates, excluded the vessel and small vegetation from its capture, and throttled sky-radiance uniform updates to 10 Hz. Main-view objects remain visible. Godot documents the costs and update behavior of [reflection probes](https://docs.godotengine.org/en/stable/classes/class_reflectionprobe.html).
- Moved the navball's existing directional drawing into a 152 x 152 GPU viewport. Its markings, handedness, controls, and telemetry stay intact; it updates only when the flight HUD requests an update. Removed the per-frame CPU raster and image upload. Measured HUD update cost fell from about 8.5 ms to about 0.72 ms in the debug build.
- Added a native-resolution F8 graphics panel with Native/TAA, 85%/FSR 2, 75%/FSR 2, and 67%/FSR 2 choices. Quality (75%) is the default; resolution mode and launch-shake preference persist in `user://graphics.cfg`. Fullscreen can be toggled live and is the project default. FSR 2 supplies temporal antialiasing, so separate TAA and MSAA are disabled in those modes. See [Godot resolution scaling](https://docs.godotengine.org/en/stable/tutorials/3d/resolution_scaling.html).
- Added `/render` diagnostic toggles, resettable frame timings, whole-update/HUD timings, and `tools/benchmark-graphics.ps1`. Benchmark captures are kept separate from timed samples because PNG capture stalls the frame.

### Measurements after stage 1

Same machine, 2560 x 1440 fullscreen, debug build. The benchmark uses a full-site camera (12 degrees depression, bearing 295 degrees, 95 m distance); the initial baseline used the original close camera. The scene and view changed, so these are observed outcomes rather than an isolated before/after speedup ratio. Each pass uses 60 telemetry samples about 200 ms apart after streaming settles. P95/P99 describe the final rolling 600 frames; FPS is the engine's final one-second reading. Desktop activity is not controlled.

| Mode | Scene | Mean main-viewport GPU | Final FPS | Frame P95 | Frame P99 |
| --- | --- | --- | --- | --- | --- |
| Quality, 75% FSR 2 | Pad | 10.34 ms | 94 | 11.43 ms | 13.54 ms |
| Quality, 75% FSR 2 | Liftoff to about 520 m | 10.95 ms | 88 | 13.70 ms | 15.27 ms |
| Ultra quality, 85% FSR 2 | Liftoff | 13.31 ms | 77 | 15.40 ms | 29.92 ms |
| Native, TAA | Pad | 12.99 ms | 64 | 16.24 ms | 17.39 ms |
| Native, TAA | Liftoff | 15.27 ms | 70 | 18.41 ms | 19.55 ms |

Quality met the 16.67 ms target at P99 in these short pad/liftoff passes. Ultra and Native remain options but did not meet that same consistency threshold. These measurements do not establish performance across an entire mission, reentry, all camera angles, or cold shader compilation. A brief headless numerical check ran during the native liftoff pass; treat that pass as indicative rather than an isolated benchmark.

### Verification and remaining scope

- Game builds with zero warnings/errors.
- Simulation: 366/366 checks pass.
- Terrain/scatter: 83/83 checks pass, including the expanded apron clearing and nearby vegetation.
- Actual-flight terrain regression: 11/11 checks pass.
- Optical-depth lookup: 117/117 reference comparisons pass; maximum normalized error 0.48% against 2048-step integration, including low-altitude, horizon, and orbital samples.
- Vulkan captures inspected for pad structures, paint, lighting, steam outlets, and the GPU navball. F8 open/close and quality selection were driven through the debug bridge; renderer scale changed as selected and throttle commands stayed blocked while the panel was open.
- Numerical headless tests report sandbox certificate/debug-listener warnings; these did not prevent the checks. The standalone Vulkan game bound its debug listener successfully.

The broader coastal ecology, water/cloud appearance, full vehicle detailing, reentry overhaul, motion blur, and minimal HUD redesign remain in subsequent stages. New tower/platform/deflector geometry is visual scenery: this stage does not add structural collision or destruction; the terrain collision datum and flight physics still use the shared simulation. Existing planetary and volume approximations remain documented in the rendering notes.

## Stage 2: coastal terrain and clouds

- Replaced the blanket low-elevation beach material with clustered scrub/grass, sandy openings, darker damp lowlands, and woodland cover. The biome survey still controls regional climate, snow, and the coastline. Shallow coastal water now has muted green/brown variation rather than one uniform shelf tint.
- Added modest 120 m coastal hummocks, with three smoothly filtered octaves, to the shared double-precision terrain function. Mesh generation and collision use the same relief. The surveyed land/sea classification and level launch apron remain authoritative; this does not invent new lakes or tidal channels.
- Tree and grass distribution share a deterministic body-fixed landscape field with the terrain material. Trees grow in groves and openings; warm lowland trees have shorter, broader proportions and use broadleaf/pine assets. Authored seven-frond palmetto meshes with folded fan leaflets and generated mesh LODs replace oversized grass clumps in the broad coastal scatter layer. Existing clearing, streaming, and tree/rock collision behavior remains intact.
- Reworked cumulus coverage, cloud-base/top profiles, cellular billows and edge erosion, extinction, sunlight self-shadowing, and multiple-scattering fill. An authored fair-weather region surrounds the Cape, blending into the existing global weather map over 30â€“100 km. Density and lighting use the same cloud field. Short lighting marches reuse slowly varying weather inputs rather than repeating geographic texture lookups at every sample.
- Replaced the animated diagonal sample pattern with stationary integer-hash stratification. Near-cloud integration uses 128/192/256 samples for Standard/High/Ultra; orbital views blend toward 12 samples. Ground-level clouds fade into haze over 35â€“65 km; that range limit blends out between 15 and 45 km camera altitude so it does not erase orbital clouds. Sample footprints account for angular pixel size as well as integration stride.
- Added a 256 Ã— 256 local cloud-shadow texture covering 16 km, refreshed every 0.5 s. Terrain and vegetation share it; distant terrain falls back to direct cloud integration. The map follows body-fixed ground coordinates through planetary rotation and floating-origin changes, fades at its edges, and suspends updates above 12 km. It is a sea-level projection with approximately 62.5 m texels, intended for the low coastal landscape, not high-precision mountain or vehicle self-shadowing.
- Added persistent cloud-detail settings to F8, independently of resolution scaling. Verified High selection and persistence, then restored Standard and selected Balanced/67% FSR 2 for the review build. The HUD remains native-resolution. Existing saved settings remain supported.
- Preallocated and recycled the eight bounded surface-exhaust volume renderers, warming their empty shaders at startup. This removes recurring material/node allocation when a new surface wake starts. Terrain upload arrays now release their native wrappers promptly. Added vessel/planet CPU timing breakdowns and coast/cloud/orbit scenarios to the benchmark script.

### Validation and practical limits

The final source builds with zero warnings/errors. Simulation checks pass 366/366, scatter checks 83/83, and actual-flight terrain collision checks 11/11. Vulkan captures cover the pad, coastal water, flight within the cloud deck, liftoff, and the orbital view. No new third-party assets or dependencies were added. Numerical headless runs retain the environment's certificate/debug-listener warnings.

This is an authored fair-weather setup, not a weather simulation. Close cloud edges retain some volumetric sampling grain, with higher detail modes available at greater GPU cost. The local shadow cache covers terrain and vegetation; the pad structures and vehicle still use their existing material lighting. Lowland materials and relief provide the coastal character without adding surveyed wetland hydrology.

The frame-time target is not a guarantee across the entire flight. Balanced rendering provides useful headroom in dense clouds. Short launch stalls can still occur during main-thread terrain mesh adoption; preallocating exhaust effects reduced the measured launch P99 from about 31 ms to about 18 ms in the diagnostic passes. Those short passes are observations, not an isolated causal benchmark or a full-mission performance guarantee.

### Final stage 2 measurements

Same machine, Vulkan debug build, 2560 Ã— 1440 fullscreen. Standard clouds (128 near samples), Balanced/67% FSR 2. Each scene uses 60 telemetry samples about 200 ms apart after its streaming queues settle, followed by a separate screenshot. P95/P99 are the final rolling 600-frame window. Desktop GPU activity is not controlled. Source and scene content differ from stage 1; these are resulting performance measurements, not isolated speedup ratios.

| Scene | Mean main-viewport GPU | Final FPS reading | Frame P95 | Frame P99 |
| --- | --- | --- | --- | --- |
| Pad | 10.34 ms | 94 | 13.32 ms | 16.90 ms |
| Liftoff to about 502 m | 11.32 ms | 75 | 14.87 ms | 17.06 ms |
| Within clouds, 2400 m | 12.24 ms | 81 | 14.74 ms | 16.85 ms |
| Coastal water, about 800 m | 11.59 ms | 80 | 13.44 ms | 14.71 ms |

The earlier orbital check at 75% FSR 2 measured 7.30 ms GPU, 125 FPS, and 8.79/9.71 ms frame P95/P99. The default orbital camera was 120 km above the datum with an 80 km camera arm. It ran before the final exhaust preallocation and terrain-array disposal changes, which do not alter orbital cloud rendering.

All four final near-surface P95 readings are below 16.67 ms. Pad, liftoff, and cloud P99 narrowly exceed it, so sustained 60 FPS at every instant is **not** established. The final liftoff run improved further to 17.06 ms P99 after prompt native-array disposal; individual mesh-adoption stalls remain observable. At 75% FSR 2, the dense-cloud diagnostic measured about 16.04 ms GPU and 18.49/23.29 ms frame P95/P99, supporting the Balanced selection for this machine.

Final raw data: `game/.artifacts/benchmark-{pad,liftoff,clouds,coast}-0.666667.json`. Final captures: `game/.artifacts/stage2-final-{pad,liftoff,clouds,coast}.png`. Artifacts are local and Git-ignored. The game is left at the launch pad with Balanced/Standard settings for review.

## Stage 2 refinement: terrain, clean clouds, fog and distant forest

This refinement supersedes the sampling and shadow-cache descriptions above. The benchmark script
overwrites its conventional output names; the refinement measurements are also copied to
`game/.artifacts/refinement-benchmark-{pad,liftoff,clouds,coast}.json`.

- Replaced the narrow, repeated landscape threshold with a domain-warped field at several scales.
  Sand openings now favor dry, raised coastal ground; damp and woodland tints have less contrast.
  Soil/rock macro detail contributes to local terrain, and fine grain uses periodic local patch
  coordinates to avoid planet-scale floating-point steps. The CPU vegetation field matches the
  terrain material's geography. Surveyed coastline and collision relief remain authoritative.
- Doubled cloud shape resolution on each axis to 256Â³ and erosion resolution to 128Â³, adding a
  shape octave. Removed spatial jitter and discontinuous density-dependent sample spacing.
  Endpoint quadrature, pixel-filtered erosion and continuously interpolated lighting remove the
  obvious speckling and stepped contours in the supplied views. Nearby High/Ultra sampling is
  retained, while distant step spacing stays bounded. Weather lookups are interpolated over 2 km.
- Increased the 16 km shadow cache to 512Â² (31.25 m texels), filtered its lookup, and reduced
  excessive shadow contrast on terrain and vegetation. Cloud attenuation affects direct light.
- Added a stronger coastal fog layer, composited in front of clouds, with spherical height falloff,
  scene-depth clipping and cloud-cast sun shafts. The extra fog occupies the lowest 900 m and fades
  from the camera view over 800â€“1500 m altitude; the existing atmosphere continues above it.
  F8 has persistent Clear / Coastal haze / Haze & sun shafts settings. The final choice is
  Haze & sun shafts. The beams depend on viewing direction and cloud gaps; they are not an
  always-visible radial glare overlay. Structure and vehicle occlusion of fog is not simulated.
- Extended visible tree coverage from 1.6 km to 8 km with two-triangle canopy impostors, streamed
  asynchronously in 1 km cells. Near models and collision behavior remain intact. Distant crowns
  approximate coverage using the same habitat rules, with no distant shadow casting. Ground
  movement, rather than vertical ascent, triggers cell reselection. This avoids unnecessary
  forest queue rebuilding during launch.

### Refinement validation

The final game build has zero warnings/errors. Actual-flight terrain and cold-cell tree-collision
checks pass 11/11 after the forest changes; terrain/scatter checks passed 83/83 earlier in this
refinement. Vulkan captures cover the revised terrain, cloud edges toward the sun, Ultra cloud
detail, coastal fog, and the extended forest. F8 selection and persistence were exercised, then
Balanced/67% FSR 2, Standard clouds, and Haze & sun shafts were restored. No external assets or
dependencies were added.

At 2560 Ã— 1440 fullscreen on the same RTX 4060 Ti, the fixed 350 m forest view loaded about
30,853 distant canopies. An off/on/off visibility comparison measured 9.93 / 10.29 / 10.04 ms
mean GPU time: approximately **0.31 ms** for drawing the distant layer against the two off passes.
Each pass sampled 40 times at about 150 ms intervals after settling. This isolates drawing cost;
it does not measure the transient cost of generating newly visited cells. Raw comparison:
`game/.artifacts/refinement-canopy-benchmark.json`.

| Scene | Mean viewport GPU | Final FPS | Frame P95 | Frame P99 |
| --- | --- | --- | --- | --- |
| Pad | 12.23 ms | 78 | 13.18 ms | 14.17 ms |
| Clouds at 2400 m, warmed | 9.34 ms | 105 | 10.49 ms | 10.78 ms |
| Coastal water at 800 m | 6.00 ms | 150 | 7.02 ms | 7.28 ms |
| Liftoff to about 497 m | 13.04 ms | 67 | 15.53 ms | 28.39 ms |

These use the same 60-sample protocol as stage 2. The earlier cloud pass measured 13.86 ms GPU
and 18.04 ms frame P99; warm-up and uncontrolled desktop activity affect these short runs.
The final launch pass still has intermittent spikes. In the sampled slow CPU updates, vessel/effect
work dominates and forest streaming is idle; the samples do not identify the cause of every long
frame. Sustained 60 FPS through all launch frames is not established.

Two development launches crashed natively while changing shaders (one Godot fault, one NVIDIA
compiler-module fault). Subsequent direct launches and all final visual passes remained running;
the native fault cause is not established. The final review log has no shader compilation errors.
Headless tests retain the environment's certificate/debug-listener warnings.

A compound diagnostic request that teleported and restarted the running flight in one call also
logged one null-reference exception in the existing exhaust-contact path. Separate restart and
teleport requests recovered normally. This diagnostic edge case has not been fixed in this pass.


## Cloud and fog zoom stability — September 13

The cloud/fog pass now uses compensated eye-relative heights and sphere intersections, deterministic
fine density integration, and world-space cloud lighting. The fog uses fixed distance bins and
prefiltered cloud shadows, so changes to scene depth do not redistribute the entire fog march.
Clouds and fog composite together; camera-altitude fog fading and the distant-cloud opacity cutoff
are removed. Cloud shadow textures and their coordinate frames are double buffered together.

Cloud structure includes additional billow and erosion scales, with higher interior extinction.
Eight expanding, advecting plume clearings have smooth birth/recovery and compressed rims. Cloud
noise volumes are shared across flight resets rather than regenerated. A 64 km lighting cache
reduces repeated solar integration. Empty regions and fully dense samples skip unnecessary work.

Aggressively reducing the ray samples reintroduced contour bands during development. The final
configuration retains the fine integration and exposes an additional F8 Performance / 50% FSR 2
option. This is an image-quality/performance tradeoff: the interface and output stay at 2560×1440,
while 3D rendering is reconstructed from 1280×720. Existing quality options remain available.

The game builds with zero warnings/errors. Eleven Vulkan GPU checks pass for cloud density,
plume clearing, continuous recovery, rim compression, layer bounds, and agreement with double
precision within 1 cm for ray heights out to a 100 km offset. `git diff --check` passes.
Zoom captures cover 80–400 m camera distance; these are visual checks, not proof of all possible
camera paths being artifact-free. The lighting cache approximates the vertical optical-depth
profile; the wake effect is a bounded visual disturbance, not fluid simulation.

Final 60-sample cloud benchmark on the RTX 4060 Ti, Standard cloud detail, 1440p output and
50% FSR 2: **14.89 ms average GPU**, **65 FPS** final reading, **16.565 ms frame P95**,
**17.271 ms frame P99**. Occasional frames remain over the 16.67 ms target; sustained 60 FPS
in every situation is not established. This result includes the lower render scale and should
not be interpreted as a like-for-like shader-only speedup over the earlier 67% measurements.

One final-review launch failed with a native access violation while a terrain worker was reading
`Vector3d`; the replacement launch completed the cloud measurements and zoom captures. The cause
is not established. No shader errors were reported in the replacement review log.

The same final 60-sample protocol measured **16.99 ms GPU / 56 FPS** at the pad and
**4.74 ms / 161 FPS** in the coast view. The pad remains below the requested sustained-60 target;
this pass improves the cloud regression but does not claim a universal 60 FPS lock. The Performance
setting was selected through F8 and verified to persist through a flight reset. The review instance
is left paused at 2400 m with Standard clouds and Haze & sun shafts.

## Global terrain and map-cloud pass - September 13

Map camera synchronization now precedes the atmospheric eye-coordinate update. This removes the
one-frame mismatch between the map camera and the cloud ray geometry. The weather field uses
filtered large-scale coverage, nonperiodic regional distortion, and a stratiform component to join
small cells into broader formations. Fine cloud integration remains unchanged. Fog computes its
transmission first so cloud work hidden behind nearly opaque low-altitude haze can terminate early.

The existing global elevation survey now receives bounded coastal refinement shared by the CPU
terrain/collision sampler and GPU shoreline mask. Coves, headlands, tidal cuts, sandbars, coastal
dunes, and inland gullies vary by region. The surveyed inland elevations and deep ocean remain
unchanged by shoreline refinement, and launch clearings remain level. Beach width follows coastal
slope; wet sand, broken surf, mineral colours, sediment bands, and small sand ripples use continuous
world coordinates or footprint filtering. Nearby material depth sampling avoids the unused rock or
soil texture, and uses 8-16 parallax layers. Optional Terrain contact shading in F8 adds ambient
occlusion around close geometry without darkening direct sunlight.

These are procedural landforms over the existing survey, not an exhaustive geographic feature
dataset. There is no new routed freshwater river/lake network or hydrology simulation in this pass.

Validation: game build has zero warnings/errors; all 513 simulation checks pass. Forty-eight Vulkan
shoreline probes agree with the double-precision CPU shape within 0.039 m. All eleven cloud density,
wake and ray-height GPU checks pass. Visual captures cover coastal lagoons, near-ground scrub,
Appalachian hills and a map-camera pan followed by a return to its original orientation.

Two ordinary review launches terminated without a useful game-log stack. Windows reported a native
.NET access violation. Controlled review launches with DOTNET_TieredCompilation=0 completed the
terrain teleports and GPU measurements. This is a diagnostic launch setting, not a shipped fix;
the native runtime failure's cause remains unresolved.

Final pad benchmark (60 samples, RTX 4060 Ti, 2560 x 1440 output, 50% FSR 2, Standard clouds,
Haze & sun shafts and contact shading enabled): **14.96 ms average GPU**, **64 FPS** last reading,
**18.14 ms frame P95 / 21.06 ms P99**. High measured 16.76 ms / 57 FPS; Ultra measured
18.13 ms / 53 FPS in shorter 30-sample comparisons. Standard and Performance/50% are selected
through F8 for this machine; higher settings remain available. These results include quality
selection and the controlled runtime launch, so they are not a shader-only speedup claim.
The average GPU budget meets 60 FPS in this pad view, but frame spikes still exceed 16.67 ms.


## Silhouette stability, orbital filtering and shoreline cleanup — September 13

The atmosphere now uses exact premultiplied RGB extinction, with alpha representing actual
extinction instead of a fully opaque screen copy. Fullscreen carrier quads preserve the complete
horizon without shortening the camera's far plane. Native TAA and FSR 2 remain the reconstruction
choices; the rejected low-resolution TAA fallback is not used. FSR sharpening is reduced.

Godot derives FSR 2's reactive mask from the resolved scene alpha, then dilates it across neighbouring
pixels. Opaque cloud backgrounds therefore suppressed history needed by thin foreground silhouettes.
GeometryHistory runs after transparency and before FSR 2, caps reactivity at 0.08 only in a two-pixel
band around strong depth discontinuities involving geometry within approximately 5 km, and leaves
RGB bit-for-bit unchanged. It uses explicit resolved-buffer API arguments (the legacy overloads can
select multisampled textures). Smoke over continuous ground and distant cloud pixels retain their
normal reactivity. The effect is disabled for native TAA. An optional, saved 2x MSAA setting provides
additional geometric coverage; it is enabled by default. The unsuccessful cloud-depth/fog split was
removed.

The final fixed-camera tower capture measured mean frame-to-frame RGB change of 2.136 without
history correction and 0.438 with it, over the same 500x650 region. Pixels changing by more than ten
levels fell from 2.52% to 0.21%. These measurements cover a paused tower against clouds, not every
camera movement or effect. A short final sample at 2560x1440 output, 50% FSR 2, Standard clouds,
haze/shafts and 2x MSAA measured 15.14 ms average GPU, 62 FPS last reading and 19.05 ms frame P95.
A previous mask comparison added roughly 0.13 ms; the final 5x5 discontinuity test is slightly larger.
A longer reset-based pad benchmark was interrupted by another crash and is not reported as passing.

The shared 256-cubed base cloud field now has a real 3D mip chain, averaging all eight source voxels
at each level, and selects LOD from projected footprint. Near-cloud detail remains resolved; orbital
views no longer sample full-resolution noise unconditionally. A paused map capture pair differed by
0.003 RGB levels on average in the measured planet region. Coastal fog's daylight uses each sample's
location instead of the camera's hemisphere. Water's sun reflection has not been redesigned.

Shoreline slope uses fixed 32-metre world offsets evaluated per vertex, replacing unstable screen
height derivatives. Very close shoreline shading uses a separate double-sampled smooth coastal-height channel to avoid
planet-coordinate quantization and relief-clamped heights hugging triangle edges; it blends back to the surveyed per-pixel contour at coarser spacing.
All shoreline surf/foam compositing has been removed at the user's request. Wet sand and water waves
remain. Terrain texture gradients are evaluated before material branches.

Validation: clean game build; 515/515 simulation checks; 11 cloud GPU checks; 21 rendering-stability
checks, including a 256-pixel GPU test that verifies exact RGB preservation and the affected history
mask neighbourhood. Manual screenshots cover the map, tower and coast. Small remaining temporal
artifacts and sustained 60 FPS throughout flight are not ruled out by these bounded checks.

### Crash investigation

Recent Windows records include native access violations in terrain-worker Vector3d property reads
under .NET 10.0.10. Another review log contains a distinct nvoglv64.dll/Vulkan stack. Neither establishes
a single root cause. Disabling tiered compilation did not eliminate crashes. No recent WHEA entries
were returned by the queried two-day window, and no existing Godot dump was found in CrashDumps.

The i9-13900K machine reports ASUS ROG STRIX B760-I GAMING WIFI, BIOS 1205 dated June 2023.
That firmware predates Intel's instability mitigations; Intel recommends a BIOS with microcode 0x12F
or later and Intel Default Settings. This is an actionable firmware lead, not proof of hardware failure.
No firmware, driver, registry or machine-wide runtime settings were changed.
Source: https://www.intel.com/content/www/us/en/support/articles/000102331/processors.html


## Final antialiasing selection - September 14

At the user's request, the graphics panel no longer exposes antialiasing settings.
All resolution presets use temporal reconstruction only: native resolution uses TAA,
and reduced resolution uses FSR 2. MSAA and screen-space AA are disabled, including
when an older configuration contains an enabled antialiasing setting. Resolution,
cloud and atmosphere choices remain available. Benchmark defaults match this selection;
explicit debug overrides remain available for diagnosis.

The evaluated SMAA/MSAA combination is not the shipped default. Existing FSR 2
geometry-history protection remains in place. Hull roughness mipmaps now use the
matching normal texture to filter distant specular highlights without an additional
runtime pass. Validation: game build succeeds with zero warnings and errors.
The reported native crashes remain unresolved; this change does not claim to fix them.


## Cloud wind, final sampling and launch details - September 14

CloudWind carries the entire weather field with one spherical, body-fixed flow: 10 m/s
from southwest to northeast at the launch coast. Weather coverage, billows and fine erosion
all use the same precomputed inverse frame. Planet rotation is composed separately. Double
precision angle reduction avoids long-save drift before conversion to shader matrices.
There is no elapsed-time texture-offset reset, camera-relative movement, or noise regeneration.
This is steady visual weather, not a new aerodynamic wind-force model or a weather simulator.

Ground-shadow and cloud-lighting caches use the same wind frame. Their published coordinate
frames advect between refreshes; refresh stops during pause and can accelerate when simulation
time advances quickly. Plume clearing capsules advect and rotate with the cloud flow while
retaining their existing expansion, downstream transport, expiry and recovery.

The retained cloud optimization keeps Standard/High density sampling intact and uses six
instead of eight density samples per Ultra interval. Ultra retains its shorter coarse intervals;
all plume intersections retain sixteen samples. Sample count is independent of camera distance.
Phase is computed once per ray, and orbital lighting skips unavailable local-cache work.
The density-bound, lighting-interpolation and regional-coordinate experiments were removed
because their timing gains did not repeat. No cloud coverage/density reduction was retained.
A short final 67%-FSR2/Ultra comparison measured 24.24 ms versus 23.59 ms total GPU time;
these are bounded scene measurements, not a sustained-60-FPS claim or a general speedup estimate.

Vegetation exclusion now follows the actual rectangular apron instead of a 44-60 m clearing
circle. The second-stage RCS ports are dark openings recessed 0.32 m into the mould line, with
no external nozzle meshes. The aft ring moves from 0.55 m to 1.50 m, above the skirt and into
the tank section; control torque continues to derive from the actual ring separation. Exhaust
origins remain inside the ports and point out through the openings. The hull surface-normal
calculation was corrected so cylindrical-wall mounts face radially outward.

Validation: zero-warning build, 515/515 simulation checks, 410 scatter checks, 32 RCS placements,
30 wind CPU/GPU checks (including poles, timestep independence, view/cache agreement and 1e9 s
save times), and all 11 cloud-volume GPU checks. Live review covered the apron, tank ports and
moving clouds. A prior review also recorded another terrain-worker NullReferenceException;
the previously reported native/terrain stability issue remains unresolved.
