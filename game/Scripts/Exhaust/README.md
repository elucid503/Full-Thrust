> Current engine behavior and verification: [Engine overhaul](EngineOverhaul.md). Historical tuning notes below describe earlier iterations.

# Volumetric effects

The renderer integrates emission and extinction in local metres, clips rays against opaque scene depth, and uses camera-relative transforms to retain precision after floating-origin rebases. Gas is excluded from the reflection probe's six camera passes. A shared, seamless 3D noise field is sampled at independently drifting scales and domain-warped to avoid an obvious repeating translation.

## Exhaust

The simulation's nozzle exit Mach, pressure mismatch, separation and shock-cell spacing drive the field. Main-engine plumes now use 112 bell radii of reach (previously 72), with 1.25 brightness, area-based dilution, and a luminous core that fades downstream. The sea-level cone opens at 0.10 rather than 0.045, since an atmospheric plume entrains air and stands far wider than the nozzle's own turn angle accounts for. RCS uses 28 radii (previously 21) and 1.15 brightness. Chemistry still controls colour, afterburning and luminosity.

Each main engine has its own startup/shutdown envelope. Nearby firing engines blend their individual cores into a common downstream field derived from the power-weighted nozzle centre and radial variance; changes in individual valves briefly increase turbulent mixing. Remaining engines retain their commanded power when another engine shuts down. Nozzle apertures still taper to zero at the bell lip.

Ground strikes trace against the simulation's terrain survey and sea-level water surface. Land produces brown dust; water and the launch pad produce pale steam. A shallow continuous source starts at the actual contact, then feeds small expanding parcels. Nearby emitters share up to eight wakes. Each wake blends at most 80 parcels into a background-built density volume; the GPU samples the shared field instead of iterating over individual billows. The source grows over 0.45 seconds, parcels develop visibility over 0.7 seconds and disperse over 12 seconds. The launch pad no longer moves the source sideways before emission. See `EngineOverhaul.md` for the current renderer, scheduling budget and verification. Water retains depression, ripples, foam and a separate spray sheet. Cloud clearings remain bounded to eight capsules.

Exhaust obstruction uses every triangle in each target vehicle's rendered stage meshes, including imported hardware, engine bells, caps and protrusions. Fixed surfaces share one BVH per stage; gimballed engine assemblies have separate BVHs whose transforms follow the actuators. Each stage owns its cached assemblies and transfers them to the detached vessel without rebuilding geometry. A 24×24 directional depth/normal map selects the first mesh hit across all other intact vehicles and refreshes at up to 25 Hz. Gas stops behind that surface and brightens into a turbulent sheet ahead of it. Broad-phase bounds include the mesh hardware, not just the rotational hull profile.

The simulation distributes each main or RCS emitter's outgoing momentum over 61 equal-area rays and applies each ray only to the first target it encounters. The summed force cannot exceed the emitter thrust. Force acts along the exhaust, away from the nozzle; off-centre strikes generate body-frame torque. Flight refreshes loads at its contact-step boundary and the integrator applies them in translation and attitude. Shutdown clears physical loads immediately even while visual gas fades. The source's normal thrust already accounts for exhaust recoil.

These are sampled flow approximations rather than CFD: very small features are limited by the directional map and force-ray resolution, self-impingement is excluded, and target heat/damage is not modelled. Mesh contact is specific to exhaust; ordinary vessel-to-vessel contact retains its existing solver.

Generated RCS emitters start at their bell lips. Imported vehicles instead intersect the fitted mesh along each jet axis at construction, so their built-in hardware does not receive an extra procedural nozzle offset.

## Entry

The hull's actual lathe profile is baked into a radial/axial signed distance and normal texture. A rasterized flow footprint provides front and rear depth and a signed silhouette distance. Its projection follows angle of attack. Single-stage and remaining-stack distance fields are prepared when the vessel is built and selected at staging; their mutable flow projections remain separate for each resulting vessel. The shock and warm shoulder layer use the hull distance and fade to zero before reaching their proxy bounds, while the wake is fed continuously from the projected perimeter. Each volume has its own bounds and sampling schedule.

A diffuse, low-intensity bow front uses a lighter version of the local spectrum. Its own elliptical paraboloid stands ahead of the leading point and arcs outward past the projected shoulders; it rotates with flow and resizes after staging. Both its lateral and normal falloffs reach zero within explicit bounds. A brighter layer of compressed gas peaks directly against the windward skin, fading outward and into the shoulder-to-wake connection; the detached bow remains a subtle secondary glow. This bow geometry is an authored approximation, not a solved shock surface.

Wake length is increased by 50% through upper and middle entry, then tapers smoothly to 40% of that length as density rises from 0.025 to 0.18 kg/mÂ³ (roughly 22â€“11 km in the current atmosphere). Existing heat and Mach fade still extinguish it as the vehicle slows. This visibility taper is an artistic adjustment, not a chemical relaxation calculation.

Weak heating uses a square-root visibility response below the 150 kW/mÂ² reference flux, making early entry apparent higher in the atmosphere without raising peak brightness. The fade follows the outermost 2% of the atmosphere's configured height (1.5 km below Terra's 75 km ceiling), so it also works in the extended, thin upper atmosphere. Mach 2.5â€“5 still controls its speed fade. Density, drag, heating and entry markers all use the same extended atmosphere column; its existing 5.6 km scale height retains a thin upper tail.

EntrySpectrum is a reduced RGB radiation model for the existing dry-air atmosphere. A frozen normal-shock temperature supplies the excitation proxy; post-shock density supplies collisional quenching and wake cooling. Relative excitation weights distinguish nitrogen first/second-positive bands, ionized nitrogen and atomic oxygen. Continuum colour uses Planck samples; hot ablation products contribute separately. Local cooling mixes hot and cool spectra along turbulent flow.

The band RGB integrals, relative strengths, critical-density scales, dissociation ramp and dust temperature offset are authored approximations. They are not measured reaction-rate coefficients or a chemical-equilibrium/CFD solution. The model deliberately does not assign fixed colours to altitude bands, add auroral green to dense entry flow, or invent changing atmospheric composition: the simulation currently has one dry-air column. At fixed speed, density changes the mixture; changing entry speed also changes which excitation channels contribute.

Useful primary references:

- [NASA: Stardust re-entry spectroscopy](https://ntrs.nasa.gov/citations/20100021412) separates plasma lines/bands from surface/dust continuum and identifies N2+ and CN emission.
- [NIST: oxygen emission lines](https://physics.nist.gov/PhysRefData/Handbook/Tables/oxygentable2.htm) lists the visible O I lines and the 777 nm triplet.
- [Nitrogen excitation measurements](https://pdfs.semanticscholar.org/47bf/5409bef30ddd87627106dcb98c20595b96af.pdf) identifies 7.35 eV first-positive and 11.03 eV second-positive thresholds.
- [NASA: nozzle design](https://www1.grc.nasa.gov/beginners-guide-to-aeronautics/nozzle-design/) describes the geometry, pressure and temperature relationship.
- [NASA: blunt-body shock shapes](https://ntrs.nasa.gov/api/citations/19680012472/downloads/19680012472.pdf) provides reference geometry for detached, curved bow fronts.
- [Godot: depth reconstruction](https://docs.godotengine.org/en/4.4/tutorials/shading/advanced_postprocessing.html) documents reverse-Z depth reconstruction.

## Verification

Build with dotnet build game/FullThrust.Game.csproj. Physics checks run with dotnet run --project tests/FullThrust.Sim.Tests.csproj (360 checks passed).

The existing localhost bridge now supports pause=true, aoa=<degrees>, aim=up and rcsTorque=<fraction>, alongside altitude and speed placement. Pause freezes flight state while allowing gas animation and camera control. Set pause=false to resume flight. /state exposes measured viewport render CPU/GPU milliseconds.

Verification captures and timing records are in game/.artifacts/volumes and game/.artifacts/plume-wrap. Tested states include vacuum and atmospheric burns, low throttle, crossflow, ground contact, RCS, staged and full-stack entry, 0/45/90/135/180-degree entry and cameras inside the volume.

September 2026 plume verification: ground dust, ocean steam, individual engine shutdown, and an off-centre Meridian plume striking Zenith were rendered at 1280×720. The mesh-contact case ran around 62 FPS after BVH consolidation, with a measured target load of 163 kN and 71 kN·m. Both loads fell to zero on shutdown. Captures are in game/.artifacts/plume-*.png; simulation checks cover momentum bounds, torque, shutdown, missed/out-of-range targets, surveyed terrain and water selection.

Plume refinement: reduced over-bright outer emission and cluster convergence; separated the narrow hot core from the wider, faint mixing layer and eased the tail out before the proxy boundary. Smoke now uses aged, drifting billows rather than a full-size stationary cloud. Rendered atmospheric/vacuum plumes and steam startup at 0, 2 and 5 seconds, followed by shutdown and clearance. Captures: game/.artifacts/plume-refine-*.png and smoke-refine-*.png.

Shock-cell correction: cell emission modulates each individual engine core, is masked inside 85% of its hot-core width, and disappears by 30% of plume length. Cells no longer use the expanded outer envelope or the merged cluster centre. Liftoff dust/smoke now carries more initial material and stronger outward momentum, so billows clear the launch mount before dilution makes them transparent. Verified in an actual launch, with capture game/.artifacts/liftoff-smoke-final.png.

Staging performance: an isolated headless debug-bridge benchmark measured first/second separation requests at 1886/450 ms before caching, and 28/4 ms after per-stage BVH reuse and entry-field preparation. Verified two consecutive separations and exhaust contact/force against the detached stage. The additional entry-field preparation happens during initial vessel construction.

Cluster merging: the overlap distance follows nozzle spacing. Individual cores blend continuously into a wider common field with normalized cross-sectional intensity. Shared turbulence is sampled in stage coordinates without per-engine noise seeds, so adjacent jets roll together instead of remaining parallel ribbons. Shock cells remain confined to the separate near-nozzle cores. Near-ground smoke uses larger initial billows (3.0 m minimum on land), a 40-billow budget per emitter and a ten-second lifetime. Validated cluster burns, partial engine shutdown and launch smoke in an isolated rendering instance.

Extended-tail pass: main plume reach increased from 72 to 112 bell radii; the terminal fade now begins at 76% of reach and axial dimming is gentler. Shared turbulent structures advect five times faster and individual-jet noise four times faster. Ground smoke carries three times the previous material per packet (steam twice), starts in larger billows, emits every 0.28 seconds and disperses over ten seconds. Checked liftoff and sustained ground contact; captures game/.artifacts/smoke-volume-*.png and plume-long-fast.png.

Irregular flow and volumetric billows: plume noise now uses hashed, smoothly interpolated 3D lattice noise rather than translating a repeating texture tile. Independently evolving scales warp one another; merged jets still share stage-space coordinates. Smoke emission timing, direction, radius, growth and drift are randomized per billow. Its 3D density has warped lobes and finer erosion, integrated with 40 ray samples and Beer–Lambert extinction. Sunlight attenuation is estimated analytically through overlapping billow bounds within each emitter volume, replacing flat vertical brightness; this is approximate self-shadowing, not a full light transport solver. Rendering checks cover sustained ground smoke from two angles and successive fast-flow plume frames.

Continuous-core tuning: cluster merging remains near the engines, while shared displacement and density breakup ramp from 58% to 90% of plume reach. The core expands gradually, with reduced flicker upstream and a later terminal fade. Verified the merged atmospheric plume in an isolated rendering instance; capture game/.artifacts/plume-solid.png.

Inside-plume performance: ray sampling scales smoothly from 12 samples inside the proxy to the authored budget one proxy width away. The continuous upstream core skips turbulent noise evaluation. At 1280x720, the same paused six-engine burn viewed axially from inside measured 34.2 ms GPU / 28 FPS before and 10.6-11.9 ms / 60-63 FPS after. Before/after captures: game/.artifacts/plume-inside-before.png and plume-inside-adaptive.png.

Plume diameter correction: removed downstream core contraction and capped early expansion. The hot core now widens gradually throughout its reach. Merged Gaussian width includes twice the squared radial RMS nozzle offset, preserving the cluster second moment rather than narrowing it during merging. Verified the atmospheric cluster burn in game/.artifacts/plume-width.png; shader compilation and diff checks passed.

September mass and contact polish: Zenith dry mass is 9,000 kg (including 3,000 kg of engine
hardware); Meridian dry mass is 3,600 kg. Launch thrust and fuel capacity are unchanged. Dry mass
continues through the same centre-of-mass and inertia calculations as fuel, with the Zenith engine
inertia scaled to its new hardware mass. The simulation regression bounds one-engine booster TWR
at five percent fuel between 1.5 and 2.4 while preserving full-stack launch authority.

Exhaust impingement uses 12% effective momentum coupling to represent flow escaping around the
struck vehicle. It is a gameplay coupling approximation, not a resolved fluid solution. Force and
torque share the same factor and the existing bounded ray budget, hit geometry, and shutdown rules.
