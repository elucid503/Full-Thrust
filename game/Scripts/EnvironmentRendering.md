# Environment rendering

The terrain survey drives both geometry and collision. Ground materials combine imported
textures with body-fixed procedural detail, with footprint filtering and distant LOD.
See [TerrainRendering.md](TerrainRendering.md) for terrain, vegetation and coastal geometry.

Cloud shape and erosion use native Godot NoiseTexture3D/FastNoiseLite resources:
`game/Effects/CloudShape.tres` and `game/Effects/CloudDetail.tres`. Edit their dimensions,
noise and seamless settings in the Inspector. Shape dimensions must remain a power-of-two
cube because FilteredVolume builds a 3D mip chain to suppress distant aliasing.
The planetary cloud shell and light caches remain custom shaders to support ground-to-orbit
flight, floating origins and the same weather in flight and map views.
Clouds and coastal fog now render at half resolution through Godot’s compositor API.
See [CloudPerformance.md](CloudPerformance.md) for the rendering path and verification.

Water retains the shared CPU/GPU gravity-wave field, native Godot material lighting and
reflection integration, shoreline depth and buoyancy. A generic flat water plane would
break the spherical world and agreement with collision heights.
See [WaterBehavior.md](WaterBehavior.md) for controls, rendering and physics.

Exhaust no longer disturbs clouds or water and creates no smoke, dust, steam or spray.
This removes the secondary density volumes, screen buffers, contact masks and wake tracking.
Weather, wave displacement, coastal detail, cloud shadows and atmospheric scattering remain.
