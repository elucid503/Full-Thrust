# Vehicle effects

Engine and RCS flames use native Godot CylinderMesh geometry with a small additive shader.
Edit `game/Effects/Plume.tres` in the Inspector to tune length in bell radii, atmospheric
and vacuum spread, and brightness. Each emitter duplicates that material so throttle,
fuel colour and startup/shutdown remain independent. Gimbal pivots carry the flame.
Simulation time drives animation, including pause and time warp.

`Chemistry` defines fuel colours and luminosity. `VesselView.Volumes.cs` reads engine
state and sizes the mesh; it does not apply forces to the simulation.

Removed: volumetric exhaust, interacting/merging jets, plume crossflow, mesh impingement,
exhaust forces on other vessels, soot, smoke, dust, steam, spray and exhaust disturbances
of clouds/water. Flames remain depth-tested but no longer spread around struck surfaces.
Engine combustion, propulsion, RCS forces, gimbal actuation and vessel collisions remain.

Re-entry retains its separate hull field and atmospheric emission shaders.
Run `Tests/EngineVisualChecks.tscn` with a rendered Godot .NET instance to capture the pad,
atmospheric and vacuum burns, individual-engine shutdown, staging and RCS.
