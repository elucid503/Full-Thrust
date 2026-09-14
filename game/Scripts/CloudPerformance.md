# Cloud performance

The current cloud path supersedes the older quality-preset and fog-ordering descriptions in
EnvironmentRendering.md. F2 remains limited to Native, Quality, Performance, and Fullscreen;
this pass does not change render scales, TAA, cloud coverage, or the source volume textures.

Cloud rays have a fixed budget of 96 coarse intervals, with four density samples in occupied
intervals and sixteen where an exhaust wake intersects. The nearest interval is 192 metres;
subsequent strides grow by 4%, or with the pixel footprint when that is larger. A geometric
budget reserves traversal for both sides of the cloud shell. Nearby sample positions therefore
do not change when distant terrain clips the ray. This replaces the previous upper bound of
640 intervals with eight density samples each. Early opacity termination normally ends much sooner.

Coarse bounds fetch only the shape volume, accounting for the maximum possible contribution of
the omitted detail octaves. Fine density still evaluates the original field. Rays outside the wake
bound skip all capsule-density work. The solar-cache projection is calculated once per ray, and
broad sunlight is interpolated across each occupied interval; local density still shades each sample.

The lighting cache is now 256 by 256 over 64 km, sufficient to sample its 550-metre density footprint
twice per feature. This quarters refresh pixel work. HDR storage avoids quantizing optical depth
to 1/255 of its 16-unit range. The sharper terrain-shadow cache remains 512 by 512 over 16 km.
Stratified sample offsets cycle through the existing TAA history to suppress march banding.

Only real planet intersections clip cloud rays. Opaque termination absorbs the final sub-1%
transmission so a background horizon cannot bleed through. Fog extinction is split at the cloud's
optical centroid in one march; only the remaining cloud transmission exposes fog farther away.
Descending fog bins composite in reverse order. The centroid remains an approximation for mixed,
partially transparent layers, rather than a full joint cloud/fog transport solution.

## Validation

Build `game/FullThrust.Game.csproj`, then run these scenes using Vulkan, not the headless renderer:

- `res://Tests/CloudVolumeChecks.tscn`: density, exhaust wakes, compensated heights, horizon
  continuity, opaque termination and foreground depth clipping using the production marcher.
- `res://Tests/RenderingStabilityChecks.tscn`: volume mips and premultiplied air/cloud/fog compositing.
- `res://Tests/CloudPerformanceChecks.tscn`: full-scene GPU timings and captures below, inside and
  above the deck, along a grazing ray, in orbit, and during powered flight with up to eight cloud wakes.

Set `FT_CLOUD_BENCHMARK` to a short label to separate captures under `.artifacts/cloud-<label>`.
The benchmark uses the 75% Quality scale, waits for cloud textures and 120 warm-up frames per view,
then records 120 viewport GPU samples. It prints the actual viewport size and GPU. GPU timings
exclude initial compilation and do not assert a hardware-independent frame-time threshold.

Measured on an RTX 4060 Ti, Vulkan Forward+, 2560 by 1440 viewport at 75% scale:

| View | Previous median GPU ms | Optimized median GPU ms | Optimized p95 ms |
| --- | ---: | ---: | ---: |
| Below clouds | 19.75 | 16.79 | 19.61 |
| Inside clouds | 13.18 | 12.41 | 13.34 |
| Above clouds | 26.62 | 12.55 | 13.91 |
| Grazing | 30.15 | 15.63 | 17.49 |
| Orbit | 6.16 | 6.22 | 6.91 |
| Powered cloud flight | Not measured | 13.48 | 14.75 |

These are short local runs, not guarantees for every camera position or resolution. The in-cloud
capture was checked for the removed dark horizon and sampling stripes. Initial engine startup
reported sandbox certificate/debug-listener and shader-cache-write warnings; the GPU rendered normally.
