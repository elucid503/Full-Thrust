# Engine and exhaust model

## Engine state and flight

`Stage.EngineStates` carries a chamber and actuator for every main engine. The simulation
advances them before integrating attitude and translation. The rendered bells, individual HUD
readouts, exhaust light and plume all read those states. Turning off an outer engine changes the
sum of forces and moments about the current centre of mass. The remaining chambers keep their
commanded power. Opposed gimbals can provide roll; a single axial engine hands roll to RCS.

The actuator approaches its requested angle exponentially, with a default 0.12-second time
constant. Its response is independent of the integration step. Mechanical limits constrain the
combined deflection, and the autopilot accounts for actuator response when braking a turn.
Attitude-only coasting steps also update the actuators and RCS torque allocation.

Startup has a 0.075-second chamber response. Cutoff collapses chamber output with a 0.018-second
time constant and ends useful thrust after 0.09 seconds. A separate residual-fuel pulse peaks at
0.10 seconds; cool purge gas peaks at 0.22 seconds and ends at 0.9 seconds. These are authored
generic kerolox timings, not measured Merlin valve telemetry. Residual combustion is strongest
for kerosene in air and is suppressed in vacuum. RCS uses a faster 0.012-second visual valve
response, a faint residual gas pulse, and no kerosene burnoff. Its existing aggregate force and
propellant model remains; roll allocation and propellant accounting now follow the actual RCS
share of demanded torque.

Ratings remain vacuum ratings for existing vehicles. Ambient pressure subtracts pressure thrust
using the fitted nozzle exit area; mass flow stays tied to chamber power. Specific impulse and
the HUD follow the resulting thrust-to-flow ratio. Full-stack sea-level thrust is approximately
1.269 MN, with a thrust-to-weight ratio of 1.245; vacuum thrust remains 1.5 MN. Dry masses and
fuel capacities are unchanged. An exhausted tank cannot provide an entire integration step of
extra impulse, and wrecks cannot keep producing thrust.

### Future engine restrictions

Author restrictions on a stage when constructing a new engine type:

```csharp
OperatingLimits = new EngineLimits {

    MinimumThrottle = 0.30,
    IgnitionDelaySeconds = 0.5,
    RestartLimit = 2,

},
```

A positive command below the minimum runs at that minimum; zero shuts the engine down. The
delay produces no thrust or fuel consumption. RestartLimit excludes the first ignition;
attempting to ignite consumes an ignition, including an attempt cancelled during its delay.
`null` allows unlimited restarts. Defaults are zero minimum, zero delay, and unlimited restarts.
**Existing engines use all three unrestricted defaults.**

## Exhaust, soot and air

Nozzle pressure mismatch determines shock spacing, separation and expansion. Shock cells change
the hot-core width and emission near individual nozzles, fading before the shared cluster field.
Cluster mixing follows nozzle positions and power, with coherent downstream turbulence.
Kerosene has incandescent soot and appreciable absorption, hydrogen has no soot, methane has a
small soot contribution, and monopropellant remains faint. Cryogenic storage alone does not
determine whether an exhaust contains carbon.

`PlumeFlow` compares ambient momentum with the exhaust momentum. Transverse wind bends the
downstream tail progressively, leaving the high-momentum near field relatively straight.
Opposing flow sets a stagnation distance, truncates the jet, and produces a spreading cap and
return flow. Atmospheric density going to zero removes those aerodynamic effects and leaves
pressure-driven vacuum expansion. The same bounded field supplies soot exposure estimates.

Expanded plumes have a separate, outward-advected density field in angular/axial coordinates.
It begins within a few nozzle radii and remains active in vacuum, where ambient shear vanishes.
Two filtered noise samples produce irregular streaks along the expanding streamlines; small
RCS jets use one. Its visible advection speed scales with nozzle radius and plume length rather
than claiming to resolve thermodynamic exhaust velocity. The transition from atmospheric shear
is smooth, and phase follows simulation time, including pause. `FT_ENGINE_CHECK=expansion`
captures a paused reference, four frames 50 ms apart and an expanded plume with RCS active.

Soot accumulates in a small cylindrical coating map per stage on the simulation clock. Both
other vessels' plumes and the source's own retropropulsion return flow can contaminate it.
Other-vessel exposure checks the first mesh hit, so a nearer vehicle shields one behind it.
Coatings remain after shutdown and travel with the stage's meshes during separation. This is
an exposure model, not particle transport or a prediction of deposited soot mass. Surface
resolution is 24 angular by 48 axial samples; tiny hardware shares its parent stage's map.
The coating uses the accumulated opacity directly. Periodic angular modulation and the fine
quantized pigment pattern have been removed: they exposed the bright hull in repeated axial
stripes even under a nearly saturated coating.

## Dust and steam

Ground strikes share nearby emitters into at most eight wakes, each with up to 80 transported
parcels and a 12-second lifetime. A continuous, shallow source layer grows at the actual impact
over 0.45 seconds. The old launch-pad shortcut that moved emission 14 metres sideways is removed.
The pad still supplies steam and outward momentum, but material must travel from the contact.

Parcels leave that source at irregular 0.155–0.245 second intervals, start small and dilute as they expand. Their
visibility develops over 0.7 seconds while outward acceleration takes roughly 0.5 seconds;
reduced initial optical depth avoids distinct opaque balls. The source layer fills the connection
to newly entrained material. Steam rises and weather carries both dust and steam away. Parcels
retain their body-fixed birthplaces; a stable wake anchor avoids coordinate jumps when old parcels expire.
Per-parcel aspect ratios preserve volume while varying the silhouette. Sizes, expansion and
rise speeds vary independently, and emitted density accounts for the interval so irregular timing
does not deliberately change the mean source strength.

`SurfaceCloudVolume` blends parcels, broad advected density detail and cached incident lighting into a 40-by-24-by-40
RGBA-half density texture (300 KiB per wake). One background bake can be requested per frame,
with oldest-due scheduling and at most one outstanding job per wake, targeting 20 Hz. Workers
only touch managed arrays; completed slices are uploaded on the main thread without waiting.
Volume bounds and density data are published together. Texture data and slices are reused,
and resources are disposed when a wake expires or the planet exits the tree.

The shader takes at most 32 ray steps, with a single volume lookup per step. Density warping and
direct/multiple-scattering attenuation are baked in the worker, replacing the shader's extra noise
reads, second density read and three lighting exponentials. Detail uses differently seeded,
large-period world-scale noise per wake, replacing the visibly tiled smoke texture. Shared density
and lighting make overlapping material one cloud rather than individually lit sphere surfaces.
Source growth remains continuous at render rate independently of background density updates.
Opaque scene depth, the contact plane, and opacity termination clip the march; existing TAA
sample jitter is retained. Water spray and surface deformation remain separate effects.

### Close-up rendering and overflow

Large visible smoke volumes now march into an HDR buffer at half the main 3D render resolution
in each dimension. `SurfaceCloudScreen` restricts drawing to the projected volume bounds and
disables the buffer for hidden, off-screen, small or orthographic views. The ordinary volume
shader composites this premultiplied result. Pixels whose scene depth intersects the smoke
still use full-resolution, depth-clipped integration, preserving hull and terrain occlusion.
Both paths share `SurfaceCloud.gdshaderinc`; density, source growth and lighting are unchanged.
Buffers follow camera/resolution changes and are freed when their wake expires. GPU timing
queries are enabled only by the profiling fixture.

At the eight-wake limit, a new source must exceed the least significant retained source's
power/distance score by 50% before replacing it. Inactive sources lose priority gradually.
Previously the ninth source evicted the oldest every frame; continuing sources then evicted
each other, repeatedly allocating textures and abandoning background builds. The new admission
rule keeps steady sources resident and prioritizes nearby effects under overload.

`FT_ENGINE_CHECK=smoke-perf` measures live 2560-by-1440 scenes, then freezes the same smoke and
camera for direct, buffered and hidden comparisons. `smoke-isolate` also excludes the background
scene and tests nine competing contacts against eight retained volumes. It asserts that all
eight identities survive 120 advancing frames, and that all wakes and buffers expire after cutoff.
GPU totals include the additional smoke viewports. In `smoke-stable-final.log`, the mature
eight-volume comparison measured 2.42 ms GPU with direct rendering, 1.64 ms buffered, and
1.28 ms hidden (RTX 4060 Ti). This is a rendering-cost comparison, not a whole-game FPS claim:
full-scene 1440p testing also found substantial sky-cloud and other background rendering cost.

The older eight-contact timings below predate the admission fix. With the live engine's ninth
contact they could measure continually recreated, immature volumes; use the retained-volume
fixture above for multi-source comparisons.

`FT_ENGINE_CHECK=surface` covers pad ignition, sustained burn and clearance, water ignition and
dispersal, eight synthetic simultaneous water contacts, and dust. It also freezes each comparison
view and hides only the smoke meshes to isolate GPU cost, leaving spray and the rest of the scene
unchanged. At 1280-by-720, 0.75 render scale, Vulkan/RTX 4060 Ti, `smoke-field-audit.log` records:

| Frozen view | GPU with smoke | GPU without smoke | Added smoke cost |
|---|---:|---:|---:|
| Close pad | 7.49 ms | 7.06 ms | 0.43 ms |
| Close water steam | 6.65 ms | 6.30 ms | 0.35 ms |
| Eight water impacts | 8.85 ms | 7.99 ms | 0.86 ms |

With time advancing, eight contacts measured CPU p95 3.51 ms; close water steam measured 4.41 ms.
The earlier parcel renderer in `smoke-cost-before.log` measured close-steam GPU median 7.59 ms
and CPU p95 4.45 ms; the new advancing case measured 6.60 ms and 4.41 ms. These are fixture frame
timings, not a claim about end-to-end gameplay FPS on other resolutions or hardware.

The subsequent detail/lighting bake (`smoke-baked-profile.log`) reduced isolated median GPU
cost to 0.18 ms at the pad, 0.13 ms for close water steam and 0.42 ms for eight water impacts
(with/without totals 7.34/7.16, 6.60/6.47 and 8.87/8.45 ms respectively). Latest background
builds in those cases took 4.57–6.35 ms, off the frame-update thread. The surface fixture also
captures dust at 280 metres to check distant silhouettes. These paired visibility measurements
are more useful than comparing whole-scene times across runs with different background load.

`expanded-motion.log` verifies the expanded upper-stage plume and RCS with Vulkan. Captures
50–200 ms apart change throughout the inner jet and broad fan; a paused control remains stable.
In the sampled fan region, mean RGB difference was 0.043/255 paused versus 1.001–1.921/255
with advancing simulation time. Expanded-plume whole-scene GPU median was 1.52 ms in that view.

The vehicle reflection probe is restricted to hardware; it does not stamp a dark square onto
water under a low-altitude burn.

### Plume banding follow-up

Turbulence uses a stretched, rotated three-dimensional domain with transverse variation.
The individual and merged coordinate systems now advect in the same downstream direction;
interpolating opposite directions previously compressed the noise into axial bands during merging.
Density contrast is moderated without changing near-nozzle shock cells or ray-march budgets.
`FT_ENGINE_CHECK=flow` captures and measures the cluster tail close up. Optionally set
`FT_PLUME_SHADER` to a saved shader resource path to compare an earlier shader and the current
one in the same running scene. Water and dust checks now capture 0.2 s and 1 s after ignition
as well as sustained clouds and steam decay, to expose detached emission near the impact.

The follow-up comparison (`engine-flow-comparison.log`) measured GPU median 6.40 ms for the
previous plume shader and 6.44 ms for the revised shader in the same scene/session (CPU p95
2.34 and 2.31 ms). The revised smoke run (`engine-smoke-measured.log`) measured GPU median
10.60 ms sustained and 10.68 ms close up; CPU p95 was 5.54 and 4.84 ms. These are whole-scene
times; the smoke run is not a same-session comparison against the earlier smoke implementation.
These figures describe the earlier parcel renderer, before the shared-density rewrite above.

## Follow-up: retro flow, cutoff clipping and performance

The retro effect is a thin, irregular curved mixing layer instead of a filled orange cloud.
Ray intersections with its parabolic support place samples on the layer, including grazing
rays outside the central surface. It fades into swept-back wisps as the exhaust cools. Its
brightness and opacity were reduced after visual feedback. This is an exhaust mixing layer;
it does not make an otherwise invisible aerodynamic shock glow.

Purge gas uses the physical bell radius rather than the collapsing effective nozzle aperture.
Its bounds contain the whole puff, with smooth axial and radial extinction before the boundary.
The purge therefore has no flat geometric end as chamber pressure collapses.

The CPU audit found repeated full terrain traversals for high-altitude jets and 24-by-24 mesh
ray masks refreshed for every nearby nozzle. Surface queries now reject against a conservative
terrain ceiling, including procedural relief and authored pads. Ground shading and dust/steam
reuse the same surface hit. Mesh checks reject against actual geometry bounds and the exhaust
cone; segment/box tests precede the triangle BVH. Physical impingement retains its 61 momentum
samples and 12% coupling.

Visual masks use 20-by-20 samples for main engines and 12-by-12 for small jets. Relative-pose
caching avoids camera-dependent rebuilds. A FIFO queue permits one mask rebuild per view per
frame, preventing both simultaneous spikes and starvation. Moving masks refresh at up to
16.7 Hz within that budget. Main plumes clip their ray march to a finite cone and use filtered
texture turbulence; small RCS jets use 12 samples and omit expensive combustion turbulence.

### Reproducible audit

Set `FT_ENGINE_CHECK=audit` and run the visual-check scene. Each case warms for 30 frames and
collects 120 samples. Figures below are milliseconds at 1280 by 720, the unchanged 0.75 render
scale and TAA, Vulkan, RTX 4060 Ti. CPU measures the game frame update with translation frozen;
GPU measures the full rendered scene. They are not end-to-end frame latency measurements.

| Scenario | Before CPU p95 | After CPU p95 | Before GPU median | After GPU median |
|---|---:|---:|---:|---:|
| Close engine cluster | 9.94 | 2.44 | 7.42 | 6.50 |
| Upper-stage retro burn | 6.20 | 3.09 | 5.13 | 4.09 |
| RCS, isolated | 10.32 | 2.11 | 2.71 | 2.70 |
| RCS, nearby stage | 62.50 | 2.38 | 4.57 | 2.46 |

A separate moving-contact case verified six obstructed jets: CPU update p95 was 3.24 ms and
GPU median 2.43 ms. Physics-side exhaust loads are timed separately because the fixture freezes
translation: their p95 was 3.05 ms near a vessel and 2.86 ms with moving contact. These figures
precede the final additional segment/box rejection and shared surface-query optimization.

`FT_ENGINE_CHECK=water` also measures sustained and screen-filling steam. With the final shared
surface query, sustained steam measured CPU p95 4.57 ms and GPU median 7.30 ms; close steam
measured CPU p95 4.57 ms and GPU median 7.46 ms. The source billow volume replaces the interim
per-sample procedural version, which measured roughly 11 ms GPU in the same wider water view.

## Follow-up: connected retrograde termination

The core's termination now uses the same curved, noise-displaced surface as the deflected
exhaust, including its local thickness. Fully developed opposing flow ends the axial jet at
that surface; the previous axial-only fade left visible exhaust beyond the cap. A localized
hot footprint connects the jet to the turning flow without increasing the entire cloud's
brightness. The finite tangent-ray support fades smoothly to avoid a circular clipping edge.
The sheet retains ten samples per intersection, and shares its fold lookup with the boundary.

`FT_ENGINE_CHECK=retro-soot` checks the upper stage at 1, 6 and 35 km with 1800 m/s opposing
airflow, captures side and oblique views, then accumulated soot from opposite sides with the
engine and reentry glow off. The Vulkan renders show connected termination and a continuous
coating without the repeated stripes. The game build and shader compilation pass; simulation
physics is unchanged by these two visual fixes.

## References

- [NASA/Danny Nowlin: RS-25 steam photograph](https://www.nasa.gov/wp-content/uploads/2024/04/ssc-20240403-s00298h.jpg):
  reference for nested billows, lit rims and shaded interiors.
- [NASA: supersonic retro-propulsion](https://fun3d.larc.nasa.gov/example-19.html):
  terminal shock, dividing streamlines and annular recirculation.
- [NASA: radiation/convection coupling](https://ntrs.nasa.gov/citations/19930022656): soot-dominated
  RP-1 radiation versus molecular emission from hydrogen exhaust.
- [NASA: supersonic retropropulsion flow analysis](https://www.nas.nasa.gov/publications/software/docs/cart3d/pages/publications/AIAA2011-3194_submitted_1.pdf):
  jet termination, bow shocks and recirculation structures.
- [SpaceX: Falcon user's guide](https://www.spacex.com/assets/media/falcon-users-guide-2025-05-09.pdf):
  the Merlin/kerosene/LOX reference vehicle.
- [Merlin launch closeup by the photographer](https://www.reddit.com/r/space/comments/83qie5/closeup_photograph_i_shot_of_falcon_9s_merlin_1d/):
  visual reference for the luminous near field.
- [Godot reflection masks](https://docs.godotengine.org/en/4.5/classes/class_reflectionprobe.html):
  separate reflected objects from the objects receiving the probe.

## Verification

Run `dotnet run --project tests/FullThrust.Sim.Tests.csproj --no-restore` for simulation regressions.
Engine checks cover chamber phases, optional restrictions, restart accounting, timestep-independent
gimbals, engine-out force and torque, roll allocation, autopilot settling, fuel exhaustion,
pressure-dependent performance, ascent stability, separation, crossflow, retropropulsion and vacuum limits.
The completed suite passes 991/991 checks, including conservative terrain rejection and raised launch pads.

Build `game/FullThrust.Game.csproj`, then run `res://Tests/EngineVisualChecks.tscn` with Vulkan.
This fixture freezes translation while advancing engine and environment time in a body-fixed
pose. It captures cluster and single-engine burns, fuel variants, cutoff and purge, RCS, vacuum,
crossflow, retropropulsion, persistent soot, water steam and land dust. It does not replace a
flight dynamics test. Captures and GPU timings are written to `game/.artifacts/engine-*`.

The Vulkan fixture completes without a shader compilation failure. The sandbox logs certificate,
debug-listener and shader-cache write errors; rendering still completes. The .NET build succeeds
with a NuGet vulnerability-index warning because the package server is unavailable.

The flow, chemistry, soot and scattering are bounded approximations for rendering. They do not
resolve CFD, plume heating/damage, chemical reaction kinetics, or aerodynamic shielding of the
vehicle by its retropropulsion plume. Existing 12% exhaust impingement coupling is retained.
