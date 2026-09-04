using System.Collections.Generic;

namespace FullThrust.Sim;

/// <summary>One separable element of a vessel: its own mould line, its own tank, its own engines
/// and its own thrusters. A stage authors its geometry in the stack's coordinates, so nothing has
/// to be shifted when the stack is assembled and nothing moves when it comes apart.</summary>
public sealed class Stage {

    public string Name { get; init; }

    /// <summary>The mould line, on the stack's datum.</summary>
    public Hull Hull { get; init; }

    /// <summary>What the stage is built from, tail to nose, on the same datum.</summary>
    public IReadOnlyList<Part> Parts { get; init; } = Array.Empty<Part>();

    /// <summary>An imported model that stands in for the mould line where the stage is seen, or
    /// null to have it turned off its own stations. Mass and air still read the stations either
    /// way, so the two can only ever differ by how closely the model was fitted to them.</summary>
    public string Model { get; init; }

    /// <summary>Structure, modelled as a shell of one areal density over the whole mould line.</summary>
    public double ShellMass { get; init; }

    /// <summary>Hardware too concentrated to be a shell - an engine on the deck, a shield on the
    /// base - carried where it actually sits rather than smeared over the mould line.</summary>
    public MassProperties Ballast { get; init; } = MassProperties.Empty;

    public double PropellantMass { get; set; }
    public double PropellantCapacity { get; init; }

    public double ThrustNewtons { get; init; }
    public double SpecificImpulse { get; init; }

    /// <summary>Oxidiser to fuel by mass; the two share one tank, so this only splits the readout.</summary>
    public double MixtureRatio { get; init; } = 2.56;

    public Propellant Fuel { get; init; }
    public Propellant Oxidiser { get; init; }

    /// <summary>Thrust of the whole cluster with every thruster firing, newtons.</summary>
    public double RcsThrustNewtons { get; init; }
    public double RcsSpecificImpulse { get; init; } = 220.0;

    /// <summary>Peak torque about any one body axis this stage's cluster can raise, newton-metres.</summary>
    public double ControlTorque { get; init; }

    public double RcsPropellantMass { get; set; }
    public double RcsPropellantCapacity { get; init; }

    /// <summary>Temperature the leading skin survives, kelvin. A shield and a bare tank wall differ
    /// by this and by how much heat the skin has to soak before it gets there.</summary>
    public double HeatLimit { get; init; } = 1150.0;

    /// <summary>Heat the skin soaks per square metre per kelvin. Only the outer few millimetres
    /// take part on the timescale of an entry, which is what this stands for.</summary>
    public double HeatCapacity { get; init; } = 1400.0;

    private bool[] _engines = Array.Empty<bool>();

    private MassProperties _structure;
    private double _structureMass = double.NaN;

    public double DryMass => ShellMass + Ballast.Mass;

    public double Mass => DryMass + PropellantMass + RcsPropellantMass;

    public double FillFraction => PropellantCapacity > 0.0 ? PropellantMass / PropellantCapacity : 0.0;

    public double OxidiserMass => PropellantMass * MixtureRatio / (1.0 + MixtureRatio);
    public double FuelMass => PropellantMass / (1.0 + MixtureRatio);

    public double OxidiserCapacity => PropellantCapacity * MixtureRatio / (1.0 + MixtureRatio);
    public double FuelCapacity => PropellantCapacity / (1.0 + MixtureRatio);

    public double FuelVolume => Fuel != null && Fuel.Density > 0.0 ? FuelMass / Fuel.Density : 0.0;
    public double OxidiserVolume => Oxidiser != null && Oxidiser.Density > 0.0 ? OxidiserMass / Oxidiser.Density : 0.0;

    /// <summary>One switch per engine in the cluster. A shut engine passes neither thrust nor propellant.</summary>
    public IReadOnlyList<bool> Engines => _engines;

    public int EngineCount => _engines.Length;

    public int EnginesLit {

        get {

            int lit = 0;

            foreach (bool engine in _engines) {

                if (engine) {

                    lit++;

                }

            }

            return lit;

        }

    }

    /// <summary>Fraction of the cluster's rating currently available. A stage that declares no
    /// engine parts has no switches to throw and carries its whole rating.</summary>
    public double ThrustFraction => _engines.Length == 0 ? 1.0 : (double)EnginesLit / _engines.Length;

    public bool IsEngineLit(int index) => index >= 0 && index < _engines.Length && _engines[index];

    public void SetEngine(int index, bool lit) {

        if (index >= 0 && index < _engines.Length) {

            _engines[index] = lit;

        }

    }

    /// <summary>Whether this stage's thrusters can still raise anything.</summary>
    public bool HasReactionControl => ControlTorque > 0.0 && RcsPropellantMass > 0.0;

    public double RcsMassFlowRate => RcsSpecificImpulse > 0.0 ? RcsThrustNewtons / (RcsSpecificImpulse * Vessel.StandardGravity) : 0.0;

    /// <summary>Fits one switch to every engine the part list carries, all of them open. Separate
    /// from the mass properties because those are re-derived in flight and these must survive it.</summary>
    public void CommissionEngines() {

        int count = 0;

        foreach (Part part in Parts) {

            if (part.Kind == PartKind.Engine) {

                count += Math.Max(part.Count, 1);

            }

        }

        _engines = new bool[count];

        Array.Fill(_engines, true);

    }

    /// <summary>Mass, centre and moments of the stage as it stands, on the stack's datum.</summary>
    public MassProperties Properties {

        get {

            MassProperties dry = MassProperties.Combine(Structure, Ballast);

            return MassProperties.Combine(dry, Hull.Propellant(PropellantMass, FillFraction));

        }

    }

    // The monopropellant bottle is a third of a percent of a loaded stack, so it rides with the
    // structure rather than earning a station of its own. Cached: the sweep is the expensive part
    // of the mass properties and the shell only changes when the bottle is being spent.
    private MassProperties Structure {

        get {

            double mass = ShellMass + RcsPropellantMass;

            if (mass != _structureMass) {

                _structure = Hull.Structure(mass);
                _structureMass = mass;

            }

            return _structure;

        }

    }

}
