using System.Collections.Generic;

namespace FullThrust.Sim;

/// <summary>How a vessel stopped flying. Everything that ends a flight ends it as one of these.</summary>
public enum VesselFate {

    Flying,
    BurnedUp,
    Impacted,

}

/// <summary>A stack of stages flown as one rigid body. Mass, geometry and aerodynamics are all
/// re-derived from whatever stages are still attached, so separating one changes every figure
/// aboard without anything having to be told about it.</summary>
public sealed class Vessel {

    public const double StandardGravity = 9.80665;

    // Separation springs push the spent stage clear at about this, shared between the two by their
    // masses so the pair's momentum comes out of the release unchanged.
    private const double SeparationSpeed = 0.7;

    // Springs never let go quite together, and the tip-off that leaves is why a spent stage tumbles
    // rather than trailing the vehicle it came off nose first.
    private const double TipOffRate = 0.02;

    private readonly List<Stage> _stages;

    public string Name { get; init; }

    /// <summary>True for a stage nobody is flying any more. Debris is tracked, not controlled.</summary>
    public bool IsDebris { get; set; }

    public VesselFate Fate { get; set; } = VesselFate.Flying;

    public bool Intact => Fate == VesselFate.Flying;

    public Vector3d Position { get; set; }
    public Vector3d Velocity { get; set; }

    public QuaternionD Orientation { get; set; } = QuaternionD.Identity;
    public Vector3d AngularVelocity { get; set; }

    /// <summary>Centre of mass measured from the stack datum, along the nose axis.</summary>
    public double CentreOfMassZ { get; private set; }

    // Diagonal only; every stack modelled so far is a solid of revolution about its nose axis.
    public Vector3d Inertia { get; set; } = Vector3d.UnitX + Vector3d.UnitY + Vector3d.UnitZ;

    /// <summary>The mould line reduced to what the air cares about, rebuilt whenever the stack changes.</summary>
    public AeroProfile Profile { get; private set; }

    /// <summary>What the air is doing to the vessel this instant. Written by the integrator, read
    /// by everything else; empty in vacuum.</summary>
    public AeroForces Aero { get; set; }

    /// <summary>Temperature of the leading skin, kelvin.</summary>
    public double SkinTemperature { get; set; } = Thermal.AmbientTemperature;

    public double Throttle { get; set; }
    public Vector3d ControlTorque { get; set; }

    public bool RcsEnabled { get; set; } = true;

    /// <summary>Pilot demand for RCS translation about the body axes, each in [-1, 1].</summary>
    public Vector3d TranslationCommand { get; set; }

    public Vessel(string name, IEnumerable<Stage> stages) {

        _stages = new List<Stage>(stages);

        if (_stages.Count == 0) {

            throw new ArgumentException("a vessel needs at least one stage", nameof(stages));

        }

        Name = name;

        Assemble();

    }

    /// <summary>The stages still attached, bottom first. The bottom one is always the live one.</summary>
    public IReadOnlyList<Stage> Stages => _stages;

    /// <summary>The stage whose tank and engines the vessel is flying on.</summary>
    public Stage Active => _stages[0];

    /// <summary>The stage at the nose of the stack.</summary>
    public Stage Forward => _stages[_stages.Count - 1];

    public int StageCount => _stages.Count;

    public bool CanSeparate => _stages.Count > 1;

    public double Base => _stages[0].Hull.Base;
    public double Tip => Forward.Hull.Tip;

    public double Length => Tip - Base;

    /// <summary>Radius of the stack's mould line at a station, from whichever stage spans it.</summary>
    public double RadiusAt(double z) {

        for (int index = 0; index < _stages.Count; index++) {

            Hull hull = _stages[index].Hull;

            if (z <= hull.Tip || index == _stages.Count - 1) {

                return z < hull.Base ? 0.0 : hull.RadiusAt(z);

            }

        }

        return 0.0;

    }

    public double Mass {

        get {

            double total = 0.0;

            foreach (Stage stage in _stages) {

                total += stage.Mass;

            }

            return total;

        }

    }

    /// <summary>Everything that will still be here when the live stage's tank runs dry.</summary>
    public double BurnoutMass => Mass - Active.PropellantMass;

    public double DryMass {

        get {

            double total = 0.0;

            foreach (Stage stage in _stages) {

                total += stage.DryMass;

            }

            return total;

        }

    }

    public Vector3d Nose => Orientation.Rotate(Vector3d.UnitZ);

    // The live stage is the only one plumbed to an engine; everything above it is payload until
    // the stage under it is gone.
    public double PropellantMass { get => Active.PropellantMass; set => Active.PropellantMass = value; }

    public double PropellantCapacity => Active.PropellantCapacity;

    public double ThrustNewtons => Active.ThrustNewtons;
    public double SpecificImpulse => Active.SpecificImpulse;

    public double MixtureRatio => Active.MixtureRatio;

    public Propellant Fuel => Active.Fuel;
    public Propellant Oxidiser => Active.Oxidiser;

    public double OxidiserMass => Active.OxidiserMass;
    public double FuelMass => Active.FuelMass;

    public double OxidiserCapacity => Active.OxidiserCapacity;
    public double FuelCapacity => Active.FuelCapacity;

    public double FuelVolume => Active.FuelVolume;
    public double OxidiserVolume => Active.OxidiserVolume;

    /// <summary>Share of the loaded volume that is oxidiser, which is where the bulkhead sits.</summary>
    public double OxidiserVolumeFraction {

        get {

            double total = Active.FuelVolume + Active.OxidiserVolume;

            return total > 0.0 ? Active.OxidiserVolume / total : 0.0;

        }

    }

    public IReadOnlyList<bool> Engines => Active.Engines;

    public int EngineCount => Active.EngineCount;
    public int EnginesLit => Active.EnginesLit;

    public double ThrustFraction => Active.ThrustFraction;

    public bool IsEngineLit(int index) => Active.IsEngineLit(index);

    public void SetEngine(int index, bool lit) => Active.SetEngine(index, lit);

    public double MassFlowRate => SpecificImpulse > 0.0 ? ThrustNewtons / (SpecificImpulse * StandardGravity) : 0.0;

    /// <summary>What the live stage can still spend, by the rocket equation.</summary>
    public double DeltaV {

        get {

            double burnout = BurnoutMass;

            return burnout > 0.0 && SpecificImpulse > 0.0 ? SpecificImpulse * StandardGravity * Math.Log(Mass / burnout) : 0.0;

        }

    }

    /// <summary>What the whole stack can spend, each stage carrying everything above it.</summary>
    public double StackDeltaV {

        get {

            double total = 0.0;
            double above = 0.0;

            for (int index = _stages.Count - 1; index >= 0; index--) {

                Stage stage = _stages[index];

                double loaded = above + stage.Mass;
                double burnout = loaded - stage.PropellantMass;

                if (burnout > 0.0 && stage.SpecificImpulse > 0.0 && stage.PropellantMass > 0.0) {

                    total += stage.SpecificImpulse * StandardGravity * Math.Log(loaded / burnout);

                }

                above = loaded;

            }

            return total;

        }

    }

    public double CurrentThrust => Active.PropellantMass > 0.0 ? ThrustNewtons * ThrustFraction * Math.Clamp(Throttle, 0.0, 1.0) : 0.0;

    /// <summary>Propellant the engines are drawing right now. Taken from the thrust actually being
    /// made, so a shut engine cannot burn and the two can never disagree.</summary>
    public double CurrentMassFlow => SpecificImpulse > 0.0 ? CurrentThrust / (SpecificImpulse * StandardGravity) : 0.0;

    /// <summary>Fraction of the rating actually being made. This, not the throttle lever, is what
    /// the plume follows: a shut or dry engine is making nothing however far the lever is open.</summary>
    public double ThrustSetting => ThrustNewtons > 0.0 ? CurrentThrust / ThrustNewtons : 0.0;

    /// <summary>Every stage's cluster fires together, so the stack's authority is their sum and a
    /// stage that has run its bottle dry stops contributing to it.</summary>
    public double ControlTorqueLimit {

        get {

            return (RcsEnabled ? ThrusterTorqueLimit : 0.0) + GimbalTorqueLimit;

        }

    }

    /// <summary>What the clusters alone can raise, whether or not they are switched on.</summary>
    public double ThrusterTorqueLimit {

        get {

            double total = 0.0;

            foreach (Stage stage in _stages) {

                if (stage.HasReactionControl) {

                    total += stage.ControlTorque;

                }

            }

            return total;

        }

    }

    /// <summary>What the live engine can raise by swinging on its mount. Zero the moment it shuts
    /// down, which is exactly when a real vehicle hands its attitude back to the thrusters.</summary>
    public double GimbalTorqueLimit {

        get {

            Stage stage = Active;

            if (stage == null || stage.GimbalRange <= 0.0) {

                return 0.0;

            }

            return CurrentThrust * Math.Abs(CentreOfMassZ - stage.GimbalPlane) * Math.Sin(stage.GimbalRange);

        }

    }

    public double RcsThrustNewtons {

        get {

            double total = 0.0;

            foreach (Stage stage in _stages) {

                if (stage.HasReactionControl) {

                    total += stage.RcsThrustNewtons;

                }

            }

            return total;

        }

    }

    public double RcsMassFlowRate {

        get {

            double total = 0.0;

            foreach (Stage stage in _stages) {

                if (stage.HasReactionControl) {

                    total += stage.RcsMassFlowRate;

                }

            }

            return total;

        }

    }

    public double RcsPropellantMass {

        get {

            double total = 0.0;

            foreach (Stage stage in _stages) {

                total += stage.RcsPropellantMass;

            }

            return total;

        }

    }

    public double RcsPropellantCapacity {

        get {

            double total = 0.0;

            foreach (Stage stage in _stages) {

                total += stage.RcsPropellantCapacity;

            }

            return total;

        }

    }

    /// <summary>Whether anything aboard can raise an attitude moment at all.</summary>
    public bool HasControl => ControlTorqueLimit > 0.0;

    public bool HasRcs => RcsEnabled && ThrusterTorqueLimit > 0.0;

    /// <summary>Translation force the clusters are currently commanding, in world axes.</summary>
    public Vector3d RcsForce {

        get {

            if (!HasRcs) {

                return Vector3d.Zero;

            }

            Vector3d demand = Clamped(TranslationCommand);

            // Only a third of a cluster points along any one axis, so a pure translation is a third of the rating.
            return demand.LengthSquared > 0.0 ? Orientation.Rotate(demand) * (RcsThrustNewtons / 3.0) : Vector3d.Zero;

        }

    }

    /// <summary>Fraction of the clusters' rating currently being drawn, for attitude and translation together.</summary>
    public double RcsDuty {

        get {

            if (!HasControl) {

                return 0.0;

            }

            double limit = ControlTorqueLimit;

            // Only the share of the moment the thrusters are actually raising is charged to the
            // bottle; the gimbal's share is paid for out of the main tank like any other thrust.
            double thrusters = RcsEnabled ? ThrusterTorqueLimit : 0.0;

            double share = limit > 0.0 ? thrusters / limit : 0.0;

            double attitude = limit > 0.0 ? ControlTorque.Length / (limit * Math.Sqrt(3.0)) : 0.0;

            return Math.Clamp(attitude * share + Clamped(TranslationCommand).Length / Math.Sqrt(3.0), 0.0, 1.0);

        }

    }

    /// <summary>Draws a duty cycle from every live cluster at once, each at its own flow.</summary>
    public void SpendReactionControl(double duty, double dt) {

        if (duty <= 0.0 || dt <= 0.0) {

            return;

        }

        foreach (Stage stage in _stages) {

            if (stage.HasReactionControl) {

                stage.RcsPropellantMass = Math.Max(0.0, stage.RcsPropellantMass - duty * stage.RcsMassFlowRate * dt);

            }

        }

    }

    public bool IsAccelerating => CurrentThrust > 0.0 || RcsForce.LengthSquared > 0.0;

    /// <summary>The stage taking the flow. Which end of the stack is forward decides it, which is
    /// why a capsule keeps its shield's rating whichever way round it happens to be pointing.</summary>
    public Stage Leading => Aero.InAir && Aero.AngleOfAttack > Math.PI * 0.5 ? Active : Forward;

    public double SkinLimit => Leading.HeatLimit;

    /// <summary>How near the leading skin is to what it can survive. One is the moment it fails.</summary>
    public double HeatLoad => SkinLimit > 0.0 ? SkinTemperature / SkinLimit : 0.0;

    public Orbit OrbitAround(CelestialBody body, double time) => Orbit.FromStateVectors(Position, Velocity, body.Mu, time);

    /// <summary>Fits engine switches, builds the aerodynamic profile and derives the mass
    /// properties. Everything that has to be true of a stack after it changes shape.</summary>
    public void Assemble() {

        foreach (Stage stage in _stages) {

            if (stage.EngineCount == 0) {

                stage.CommissionEngines();

            }

        }

        Profile = AeroProfile.Build(Base, Tip, RadiusAt);

        RecomputeMassProperties();

    }

    /// <summary>Re-derives inertia and centre of mass from every stage still attached.</summary>
    public void RecomputeMassProperties() {

        MassProperties total = MassProperties.Empty;

        foreach (Stage stage in _stages) {

            total = MassProperties.Combine(total, stage.Properties);

        }

        CentreOfMassZ = total.CentreZ;
        Inertia = total.Inertia;

    }

    /// <summary>Drops the bottom stage and hands it back as a vessel of its own. Both pieces come
    /// away on their own centres of mass and share the springs' impulse by their masses, so nothing
    /// jumps and no momentum appears out of the release.</summary>
    public Vessel Separate() {

        if (!CanSeparate) {

            return null;

        }

        Stage spent = _stages[0];

        double spentMass = spent.Mass;
        double spentCentre = spent.Properties.CentreZ;

        double centre = CentreOfMassZ;

        Vector3d nose = Nose;
        Vector3d position = Position;
        Vector3d velocity = Velocity;

        _stages.RemoveAt(0);

        Assemble();

        // A lit engine under a stage that is coming off is not staging, it is a collision. The
        // lever is cut with the bolts.
        Throttle = 0.0;

        double remaining = Mass;
        double total = spentMass + remaining;

        Position = position + nose * (CentreOfMassZ - centre);
        Velocity = velocity + nose * (SeparationSpeed * spentMass / total);

        Vessel debris = new Vessel(spent.Name, new[] { spent }) {

            IsDebris = true,

            Orientation = Orientation,
            AngularVelocity = AngularVelocity + new Vector3d(TipOffRate, 0.0, 0.0),

            SkinTemperature = SkinTemperature,

            RcsEnabled = false,

        };

        debris.Position = position + nose * (spentCentre - centre);
        debris.Velocity = velocity - nose * (SeparationSpeed * remaining / total);

        return debris;

    }

    private static Vector3d Clamped(Vector3d command) {

        return new Vector3d(

            Math.Clamp(command.X, -1.0, 1.0),
            Math.Clamp(command.Y, -1.0, 1.0),
            Math.Clamp(command.Z, -1.0, 1.0)

        );

    }

}
