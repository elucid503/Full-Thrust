# Contact performance checks

Run `dotnet build game/FullThrust.Game.csproj --no-restore`, then launch Godot headless with `--path game res://Tests/Vessel/ContactPerformanceChecks.tscn`. The fixture builds the imported vehicle profiles and loads the real terrain survey. It warms each operation and reports the best of three batches in milliseconds per operation. These are CPU microbenchmarks; full-scene GPU time and FPS require an in-game check.

Measured locally during the optimization pass:

| Operation | Before | After |
| --- | ---: | ---: |
| Near-ground physics step | 8.43 ms | 1.69 ms |
| Deep vehicle overlap query | 79.57 ms | 0.15 ms |
| Exhaust contact step | 10.56 ms | 1.08 ms |
| Ground profile samples | 568 | 124 |

The additional levelled-pad fixture takes about 0.073 ms per ground step after the optimization. No matching pad-only baseline was recorded. Timings vary with system load.

Changes retain mounted/gimbaling engine colliders, open bays, and thin-wall substeps. Sampled contours are simplified within 5 mm radial error; contiguous convex sections are grouped without filling concave throats. Collision queries cache transformed bounds, reject pairs that cannot improve the current contact, and reuse penetration-solver scratch lists. Exhaust uses analytic ray/profile intersections instead of repeated distance marching. Fully levelled terrain bypasses unused natural-detail calculations while preserving subsequent plateau blends.

Rendering skips receivers beyond each layer's reach, caches hardware bindings and poses, and bakes normals from existing distance samples. Visibility runs per sample to prevent leaks behind curved hulls; the layer and receiver bounds, texture/pose caches, and solid rejection still avoid unnecessary work. The production plume and distortion shaders share surface wrapping, blocked-streamline rejection, and footprint-filtered contact boundaries. Visual distance extrapolation is separate from conservative ray stepping, and contact volumes use midpoint sampling to avoid rim stipple without increasing the 64-sample count.

Validation: simulation suite (953 checks), `PlumeContinuityChecks.tscn` for imported contact geometry, receiver gimbals, material bindings, and shader parsing. No bridge calls are required by these fixtures.
