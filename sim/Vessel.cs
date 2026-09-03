namespace FullThrust.Sim;

public sealed class Vessel {

    public const double StandardGravity = 9.80665;

    public string Name { get; init; }

    public Vector3d Position { get; set; }
    public Vector3d Velocity { get; set; }

    public QuaternionD Orientation { get; set; } = QuaternionD.Identity;
    public Vector3d AngularVelocity { get; set; }

    public double DryMass { get; init; }
    public double PropellantMass { get; set; }
    public double PropellantCapacity { get; init; }

    public double ThrustNewtons { get; init; }
    public double SpecificImpulse { get; init; }

    /// <summary>Oxidiser to fuel by mass; the two share one tank, so this only splits the readout.</summary>
    public double MixtureRatio { get; init; } = 2.56;

    public double RcsPropellantMass { get; set; }
    public double RcsPropellantCapacity { get; init; }

    /// <summary>Thrust of the whole cluster with every thruster firing, newtons.</summary>
    public double RcsThrustNewtons { get; init; }
    public double RcsSpecificImpulse { get; init; } = 220.0;

    /// <summary>Peak torque about any one body axis the cluster can raise, newton-metres.</summary>
    public double ControlTorqueLimit { get; init; } = 7000.0;

    public bool RcsEnabled { get; set; } = true;

    /// <summary>The mould line this vessel's mass properties and geometry are both derived from.</summary>
    public Hull Hull { get; init; }

    /// <summary>Centre of mass measured from the hull datum, along the nose axis.</summary>
    public double CentreOfMassZ { get; private set; }

    // Diagonal only; every stage modelled so far is a solid of revolution about its nose axis.
    public Vector3d Inertia { get; set; } = Vector3d.UnitX + Vector3d.UnitY + Vector3d.UnitZ;

    public double Throttle { get; set; }
    public Vector3d ControlTorque { get; set; }

    /// <summary>Pilot demand for RCS translation about the body axes, each in [-1, 1].</summary>
    public Vector3d TranslationCommand { get; set; }

    public double Mass => DryMass + PropellantMass;

    public Vector3d Nose => Orientation.Rotate(Vector3d.UnitZ);

    public double MassFlowRate => ThrustNewtons / (SpecificImpulse * StandardGravity);
    public double DeltaV => SpecificImpulse * StandardGravity * Math.Log(Mass / DryMass);

    public double CurrentThrust => PropellantMass > 0.0 ? ThrustNewtons * Math.Clamp(Throttle, 0.0, 1.0) : 0.0;

    public double OxidiserMass => PropellantMass * MixtureRatio / (1.0 + MixtureRatio);
    public double FuelMass => PropellantMass / (1.0 + MixtureRatio);

    public double OxidiserCapacity => PropellantCapacity * MixtureRatio / (1.0 + MixtureRatio);
    public double FuelCapacity => PropellantCapacity / (1.0 + MixtureRatio);

    public bool HasRcs => RcsEnabled && RcsPropellantMass > 0.0;

    public double RcsMassFlowRate => RcsSpecificImpulse > 0.0 ? RcsThrustNewtons / (RcsSpecificImpulse * StandardGravity) : 0.0;

    /// <summary>Translation force the cluster is currently commanding, in world axes.</summary>
    public Vector3d RcsForce {

        get {

            if (!HasRcs) {

                return Vector3d.Zero;

            }

            Vector3d demand = Clamped(TranslationCommand);

            // Only a third of the cluster points along any one axis, so a pure translation is a third of the rating.
            return demand.LengthSquared > 0.0 ? Orientation.Rotate(demand) * (RcsThrustNewtons / 3.0) : Vector3d.Zero;

        }

    }

    /// <summary>Fraction of the cluster's rating currently being drawn, for attitude and translation together.</summary>
    public double RcsDuty {

        get {

            if (!HasRcs) {

                return 0.0;

            }

            double attitude = ControlTorqueLimit > 0.0 ? ControlTorque.Length / (ControlTorqueLimit * Math.Sqrt(3.0)) : 0.0;

            return Math.Clamp(attitude + Clamped(TranslationCommand).Length / Math.Sqrt(3.0), 0.0, 1.0);

        }

    }

    public bool IsAccelerating => CurrentThrust > 0.0 || RcsForce.LengthSquared > 0.0;

    private static Vector3d Clamped(Vector3d command) {

        return new Vector3d(

            Math.Clamp(command.X, -1.0, 1.0),
            Math.Clamp(command.Y, -1.0, 1.0),
            Math.Clamp(command.Z, -1.0, 1.0)

        );

    }

    public Orbit OrbitAround(CelestialBody body, double time) => Orbit.FromStateVectors(Position, Velocity, body.Mu, time);

    /// <summary>Re-derives inertia and centre of mass from the hull for the propellant now aboard.</summary>
    public void RecomputeMassProperties() {

        if (Hull == null) {

            return;

        }

        double fill = PropellantCapacity > 0.0 ? PropellantMass / PropellantCapacity : 0.0;

        MassProperties total = MassProperties.Combine(Hull.Structure(DryMass), Hull.Propellant(PropellantMass, fill));

        CentreOfMassZ = total.CentreZ;
        Inertia = total.Inertia;

    }

}
