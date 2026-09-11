# Volumetric effects

The renderer integrates emission and extinction in local metres, clips rays against opaque scene depth, and uses camera-relative transforms to retain precision after floating-origin rebases. Gas is excluded from the reflection probe's six camera passes. A shared, seamless 3D noise field is sampled at independently drifting scales and domain-warped to avoid an obvious repeating translation.

## Exhaust

The simulation's nozzle exit Mach, pressure mismatch, wall separation and shock-cell spacing drive the field. Exit Mach is cached because it depends on fixed geometry and chemistry. Expanding gas thins with cross-sectional area; ambient density controls afterburning and shear mixing. Crossflow bends the centreline according to the ratio of ambient to jet momentum. Ground contact clips against the local planet tangent plane and adds a wall jet, integrated separately so its thin layer cannot be skipped by plume samples. The surface remains the game's spherical datum, not a terrain collision mesh.

Kerosene, hydrogen, methane and decomposed hydrazine retain distinct emission colours and luminosities. RCS valves retain their chamber pressure as duty varies. The final RCS adjustment is 20% more luminosity and 17% more length; the main plume was lengthened by 16%.

At the exit plane, emission and extinction taper to zero at the effective nozzle radius. The soft outer field widens over the first four exit radii downstream, keeping the luminous base inside the bell while retaining pressure-driven expansion farther out.

A vehicle standing in the jet is sphere-traced against its own baked hull distance field. The trace
returns a penumbra rather than a binary shadow, because the jet is a spreading cone and not a point
source; fourteen marches suffice once the answer is soft. Gas the hull stops is not discarded. It is
carried into a sheet that stays attached over the windward skin and past the shoulder, and separates
into a recirculating, turbulent wake on the lee side, with the extinction it was carrying following
it. The compressed layer against the windward skin remains the brightest part.

Generated RCS emitters start at their bell lips. Imported vehicles instead intersect the fitted mesh along each jet axis at construction, so their built-in hardware does not receive an extra procedural nozzle offset.

## Entry

The hull's actual lathe profile is baked into a radial/axial signed distance and normal texture. A rasterized flow footprint provides front and rear depth and a signed silhouette distance. Its projection follows angle of attack and is rebuilt after staging. The shock and warm shoulder layer use the hull distance and fade to zero before reaching their proxy bounds, while the wake is fed continuously from the projected perimeter. Each volume has its own bounds and sampling schedule.

A diffuse, low-intensity bow front uses a lighter version of the local spectrum. Its own elliptical paraboloid stands ahead of the leading point and arcs outward past the projected shoulders; it rotates with flow and resizes after staging. Both its lateral and normal falloffs reach zero within explicit bounds. A brighter layer of compressed gas peaks directly against the windward skin, fading outward and into the shoulder-to-wake connection; the detached bow remains a subtle secondary glow. This bow geometry is an authored approximation, not a solved shock surface.

Wake length is increased by 50% through upper and middle entry, then tapers smoothly to 40% of that length as density rises from 0.025 to 0.18 kg/m³ (roughly 22–11 km in the current atmosphere). Existing heat and Mach fade still extinguish it as the vehicle slows. This visibility taper is an artistic adjustment, not a chemical relaxation calculation.

Weak heating uses a square-root visibility response below the 150 kW/m² reference flux, making early entry apparent higher in the atmosphere without raising peak brightness. The fade follows the outermost 2% of the atmosphere's configured height (1.5 km below Terra's 75 km ceiling), so it also works in the extended, thin upper atmosphere. Mach 2.5–5 still controls its speed fade. Density, drag, heating and entry markers all use the same extended atmosphere column; its existing 5.6 km scale height retains a thin upper tail.

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

Build with dotnet build game/FullThrust.Game.csproj. Physics checks run with dotnet run --project tests/FullThrust.Sim.Tests.csproj (256 checks passed).

The existing localhost bridge now supports pause=true, aoa=<degrees>, aim=up and rcsTorque=<fraction>, alongside altitude and speed placement. Pause freezes flight state while allowing gas animation and camera control. Set pause=false to resume flight. /state exposes measured viewport render CPU/GPU milliseconds.

Verification captures and timing records are in game/.artifacts/volumes and game/.artifacts/plume-wrap. Tested states include vacuum and atmospheric burns, low throttle, crossflow, ground contact, RCS, staged and full-stack entry, 0/45/90/135/180-degree entry and cameras inside the volume.
