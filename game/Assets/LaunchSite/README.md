# Standalone launch tower

Source: **NASA / Michael D. Carbajal**, [Gantry](https://science.nasa.gov/3d-resources/gantry/).
The fixed service tower is extracted from NASA's authored LC-39 model and adapted
for Cape Meridian's fictional vehicle. The rotating service structure and mobile
launcher are omitted. This is not a scale reconstruction of LC-39.

Original download:
https://assets.science.nasa.gov/content/dam/science/cds/3d/resources/model/gantry/Gantry.glb

Original SHA-256: `AD89678F80CA08102FA8C730FDE6B93A0B700C60A15D69D5DE422F839C66EB82`.
Retrieved September 14, 2026. The source contains no textures, logos, or third-party credits.

## Usage and credit

Used under [NASA Images and Media Usage Guidelines](https://www.nasa.gov/nasa-brand-center/images-and-media/),
which cover polygon data and graphical simulations. Acknowledge NASA as the source;
do not imply NASA endorsement. NASA identifiers have separate restrictions. This
attribution does not place the game's original code or other assets under NASA terms.

## Adaptation

- Decoded Draco compression, which Godot's importer does not support.
- Selected 373 complete fixed-tower components within the documented source bounds.
  Preserved the authored columns, braces, ladders, landings, railings, lift and pipes.
- Uniform scale `2.4`, source centre `(0.771, -1.267, 5.076)` and game offset
  `(-10, 0, 3)` metres. Overall tower height is about 43 m, including its mast.
- Rebuilt normals with a 35-degree crease threshold: sharp beam edges retain their
  shape while round service pipes remain smooth.
- Six materials distinguish warm light-grey columns, blue-grey decks, yellow safety
  rails, galvanized lines, a blue lift housing and an oxide-red upper structural band.
  The source had three identical greys and no textures. These finishes are game art,
  not a claim about the facility's historical paint scheme.
- Combined the selected components into one mesh with six material surfaces:
  57,976 triangles before Godot's automatic import LODs. No runtime conversion.
  The previous full-gantry adaptation had 171,238 triangles.
- A compact open launch stand, concrete piers and braced hold-downs replace the mobile
  launcher's large box platform. The deck remains at the simulation's 4.5 m datum,
  with a 6.2 m clear exhaust opening. The concrete apron retains its existing size.
- Footprint stays inside the cleared apron. Visibility fades at 3.5 km.

## Rebuild

Download the original to a scratch directory, then run:

```powershell
npx --yes @gltf-transform/cli copy NASA-Gantry.glb Gantry-decoded.glb
python tools/prepare-launch-site.py Gantry-decoded.glb game/Assets/LaunchSite/Gantry.glb
```

The Python adapter uses only the standard library and rejects transformed or
compressed input. Keep the converted GLB and its Godot import settings in source
control. `res://Tests/LaunchSiteChecks.tscn` checks the footprint, support datum,
surface budget and exhaust clearance, then captures three views in the flight scene.
