namespace FullThrust.Sim;

/// <summary>A planned impulse: a time on the orbit and a delta-v in the orbital frame there.</summary>
public sealed class Maneuver {

    /// <summary>Universal time the burn is centred on.</summary>
    public double Time { get; set; }

    public double Prograde { get; set; }
    public double Normal { get; set; }
    public double Radial { get; set; }

    public double DeltaV => Math.Sqrt(Prograde * Prograde + Normal * Normal + Radial * Radial);

    public bool IsEmpty => DeltaV < 1e-6;

    /// <summary>The orbital triad at the node: prograde, normal, radial-out.</summary>
    public static (Vector3d Prograde, Vector3d Normal, Vector3d Radial) Frame(Vector3d position, Vector3d velocity) {

        Vector3d prograde = velocity.Normalized;
        Vector3d normal = Vector3d.Cross(position, velocity).Normalized;

        return (prograde, normal, Vector3d.Cross(prograde, normal).Normalized);

    }

    public Vector3d WorldDeltaV(Orbit orbit) {

        (Vector3d position, Vector3d velocity) = orbit.StateAt(Time);

        (Vector3d prograde, Vector3d normal, Vector3d radial) = Frame(position, velocity);

        return prograde * Prograde + normal * Normal + radial * Radial;

    }

    public Orbit Result(Orbit orbit) => orbit.WithImpulse(Time, WorldDeltaV(orbit));

    /// <summary>Seconds at full throttle to spend this delta-v, or infinity if the vessel cannot.</summary>
    public double BurnSeconds(Vessel vessel) {

        double exhaustVelocity = vessel.SpecificImpulse * Vessel.StandardGravity;

        if (exhaustVelocity <= 0.0 || vessel.MassFlowRate <= 0.0) {

            return double.PositiveInfinity;

        }

        double burnt = vessel.Mass * (1.0 - Math.Exp(-DeltaV / exhaustVelocity));

        if (burnt > vessel.PropellantMass) {

            return double.PositiveInfinity;

        }

        return burnt / vessel.MassFlowRate;

    }

    /// <summary>Half the burn placed either side of the node, which is where an impulse is assumed to land.</summary>
    public double IgnitionTime(Vessel vessel) {

        double burn = BurnSeconds(vessel);

        return double.IsInfinity(burn) ? Time : Time - burn * 0.5;

    }

}
