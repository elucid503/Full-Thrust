# Cloud rendering

Clouds and coastal fog run in a Godot CompositorEffect before transparent geometry.
The compute pass reads the current camera projection and resolved scene depth, and writes
HDR colour/opacity plus distance at half the scene resolution in each dimension. This runs
the expensive integration for one quarter of the pixels. Native terrain, vessels, HUD and
other effects retain their normal resolution.

The spatial cloud pass combines four nearby samples using scene-depth weights. Unmatched
silhouettes use the same integration at full resolution, preserving small vessels and terrain
edges. CloudMarch.gdshaderinc is shared by the compute pass, this fallback and GPU regressions;
there is no second cloud model or previous-frame reprojection. Noise filtering uses the actual cloud-buffer pixel footprint to suppress distant aliasing.
Sampling noise is fixed in screen space and fades to midpoint samples at long ray distances;
there is no frame-varying jitter without a cloud history buffer to accumulate it.
Local coastal fog fades out from 10 to 20 km altitude. The main atmosphere still renders
from orbit; disabling the local pass there removes its grid-shaped precision artifacts.

CloudRender publishes immutable scene parameters to the render thread. Target allocation,
compute commands and resource retirement happen there; texture bindings change on the main
thread before spatial draw lists are prepared. A short retirement delay protects in-flight
bindings during resize. Planet teardown explicitly releases all compositor allocations.

The existing spherical deck, coherent wind, volume mip filtering and shadow/lighting caches
remain. A sky-only cloud pass cannot obscure terrain from above the deck. Local fog volumes
would need another representation for orbital views. This path preserves the existing
surface-to-orbit cloud model while using Godot's lower-resolution compute and compositing APIs.

The game always starts fullscreen. Rendering has one authored configuration in project.godot:
75% 3D scale with FSR, TAA, no MSAA, and a half-resolution cloud target. There is no F2 panel,
quality selector, fullscreen toggle, graphics.cfg loading/saving, or debug resolution override.
Test scenes may explicitly resize their window to exercise rendering transitions.

## Verification

Build game/FullThrust.Game.csproj and run the scenes with Godot .NET / Vulkan:

- CloudPipelineChecks: fullscreen startup, absence of settings controls, half/full-resolution
  image comparison below/inside/above clouds and in orbit, and target resize.
- CloudVolumeChecks: density, deck limits, precise radial height, horizon and foreground clipping.
- RenderingStabilityChecks: 3D texture mips and premultiplied atmosphere/cloud/fog composition.
- CloudWindChecks: GPU/CPU frame agreement, poles and long simulation times.
- TransitionChecks: map/free-camera changes, rebasing, terrain retention and restart.
- CloudPerformanceChecks: full-scene GPU timing and captures at six views; set
  FT_CLOUD_BENCHMARK to label the output under game/.artifacts/cloud-<label>.

The image comparison uses fixed exposure and paused simulation. At 1280 by 720, mean absolute
RGB differences from full-resolution integration are 0.5–1.1% across the four tested views.
This is an image-equivalence check, not a guarantee of identical appearance during all motion.

## Measured performance

RTX 4060 Ti, Vulkan Forward+, 2560 by 1440 display, fixed 75% scene scale.
Each view uses 120 warm-up frames followed by 120 measured frames. These are whole-viewport
GPU timings, including terrain, lighting and other effects—not isolated cloud-shader timings.

| View | Before median ms | After median ms | After p95 ms | Median reduction |
| --- | ---: | ---: | ---: | ---: |
| Below | 15.40 | 10.42 | 11.66 | 32.4% |
| Inside | 11.93 | 9.42 | 10.12 | 21.1% |
| Above | 12.31 | 9.62 | 10.73 | 21.8% |
| Grazing | 15.10 | 9.92 | 10.81 | 34.3% |
| Orbit | 6.49 | 5.76 | 6.25 | 11.3% |
| Powered | 12.18 | 10.52 | 11.87 | 13.6% |

Captures and raw logs are in game/.artifacts/cloud-before-native, cloud-half-native,
cloud-before-native-console.log and final-CloudPerformanceChecks-console.log. The moving
powered-flight case also depends on the evolving flight state; timings are local measurements,
not a frame-rate guarantee. Runs retain the existing sandbox certificate/cache warnings and
seven shutdown texture warnings; the compositor adds no remaining allocation warnings.

Final verification also passed all 914 simulation checks, 24 rendering-stability GPU checks,
9 water regressions, and engine/staging/RCS/re-entry captures. One engine-test launch exited
with a native access violation during startup without a crash trace; two subsequent complete
runs passed. That intermittent startup failure remains undiagnosed. Its output is preserved
in game/.artifacts/cloud-engine-startup-crash.log.

After the distant-cloud and surface cleanup, cloud image/resize checks, 9 coastline checks,
50 ocean checks and all 914 simulation checks passed. Three terrain/water captures and
map/restart checks passed on retry; initial runs encountered the previously observed native
access violations in Forest/GroundScatter workers. Those worker implementations were not
changed. Follow-up logs use the simplified- prefix under game/.artifacts.
