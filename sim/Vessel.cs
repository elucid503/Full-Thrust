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

    /// <summary>The mould line this vessel's mass properties and geometry are both derived from.</summary>
    public Hull Hull { get; init; }

    /// <summary>Centre of mass measured from the hull datum, along the nose axis.</summary>
    public double CentreOfMassZ { get; private set; }

    // Diagonal only; every stage modelled so far is a solid of revolution about its nose axis.
    public Vector3d Inertia { get; set; } = Vector3d.UnitX + Vector3d.UnitY + Vector3d.UnitZ;

    public double Throttle { get; set; }
    public Vector3d ControlTorque { get; set; }

    public double Mass => DryMass + PropellantMass;

    public Vector3d Nose => Orientation.Rotate(Vector3d.UnitZ);

    public double MassFlowRate => ThrustNewtons / (SpecificImpulse * StandardGravity);
    public double DeltaV => SpecificImpulse * StandardGravity * Math.Log(Mass / DryMass);

    public double CurrentThrust => PropellantMass > 0.0 ? ThrustNewtons * Math.Clamp(Throttle, 0.0, 1.0) : 0.0;

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
