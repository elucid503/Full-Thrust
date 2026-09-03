namespace FullThrust.Sim;

/// <summary>Attitude references the autopilot can hold, expressed in the orbital frame.</summary>
public enum AttitudeHold {

    Off,
    Stability,
    Prograde,
    Retrograde,
    Normal,
    Antinormal,
    RadialOut,
    RadialIn,
    Maneuver,

}

/// <summary>Turns a wanted attitude into the control torque the vessel's thrusters apply.</summary>
public sealed class Autopilot {

    // Above this the hold is considered converged and only the residual rate is damped.
    private const double Deadband = 1e-4;

    /// <summary>Ceiling on the slew rate a hold will command, radians per second.</summary>
    public double MaxSlewRate { get; init; } = 0.35;

    /// <summary>Fraction of the theoretical braking rate actually commanded, so the approach never overshoots.</summary>
    public double Damping { get; init; } = 0.8;

    public AttitudeHold Hold { get; set; } = AttitudeHold.Off;

    /// <summary>Pilot demand about the body axes, each in [-1, 1]; non-zero input drops the hold.</summary>
    public Vector3d ManualCommand { get; set; }

    /// <summary>World direction the maneuver hold points at; set from the planned node each frame.</summary>
    public Vector3d ManeuverDirection { get; set; }

    public void Update(Vessel vessel, double dt) {

        if (dt <= 0.0) {

            return;

        }

        // The quads are the only attitude authority aboard, so a dry or disabled cluster means no control at all.
        if (!vessel.HasRcs) {

            vessel.ControlTorque = Vector3d.Zero;

            return;

        }

        double maxTorque = vessel.ControlTorqueLimit;

        Vector3d manual = new Vector3d(

            Math.Clamp(ManualCommand.X, -1.0, 1.0),
            Math.Clamp(ManualCommand.Y, -1.0, 1.0),
            Math.Clamp(ManualCommand.Z, -1.0, 1.0)

        );

        if (manual.LengthSquared > 0.0) {

            Hold = AttitudeHold.Off;

            vessel.ControlTorque = manual * maxTorque;

            return;

        }

        if (Hold == AttitudeHold.Off) {

            vessel.ControlTorque = Vector3d.Zero;

            return;

        }

        Vector3d error = Hold == AttitudeHold.Stability ? Vector3d.Zero : PointingError(vessel);

        vessel.ControlTorque = Brake(error, vessel.AngularVelocity, vessel.Inertia, maxTorque, dt);

    }

    /// <summary>Unit direction the nose should point for a hold, or zero where the frame is degenerate.</summary>
    public static Vector3d Reference(AttitudeHold hold, Vector3d position, Vector3d velocity, Vector3d maneuver = default) {

        Vector3d prograde = velocity.Normalized;
        Vector3d normal = Vector3d.Cross(position, velocity).Normalized;

        // Radial-out is completed from the other two so the triad stays orthogonal on an eccentric orbit.
        Vector3d radial = Vector3d.Cross(prograde, normal).Normalized;

        return hold switch {

            AttitudeHold.Prograde => prograde,
            AttitudeHold.Retrograde => -prograde,

            AttitudeHold.Normal => normal,
            AttitudeHold.Antinormal => -normal,

            AttitudeHold.RadialOut => radial,
            AttitudeHold.RadialIn => -radial,

            AttitudeHold.Maneuver => maneuver.Normalized,

            _ => Vector3d.Zero,

        };

    }

    private Vector3d PointingError(Vessel vessel) {

        Vector3d wanted = Reference(Hold, vessel.Position, vessel.Velocity, ManeuverDirection);

        if (wanted.LengthSquared <= 0.0) {

            return Vector3d.Zero;

        }

        Vector3d bodyWanted = vessel.Orientation.Conjugate.Rotate(wanted);

        QuaternionD rotation = QuaternionD.FromTo(Vector3d.UnitZ, bodyWanted);

        double sine = rotation.VectorPart.Length;

        if (sine <= Deadband) {

            return Vector3d.Zero;

        }

        double angle = 2.0 * Math.Atan2(sine, Math.Abs(rotation.W));

        return rotation.VectorPart * ((rotation.W < 0.0 ? -angle : angle) / sine);

    }

    // Bang-bang with a braking-distance rate limit: no gains to tune, and it cannot overshoot.
    private Vector3d Brake(Vector3d error, Vector3d rate, Vector3d inertia, double maxTorque, double dt) {

        return new Vector3d(

            AxisTorque(error.X, rate.X, inertia.X, maxTorque, dt),
            AxisTorque(error.Y, rate.Y, inertia.Y, maxTorque, dt),
            AxisTorque(error.Z, rate.Z, inertia.Z, maxTorque, dt)

        );

    }

    private double AxisTorque(double error, double rate, double inertia, double maxTorque, double dt) {

        double acceleration = maxTorque / inertia;

        double wanted = Math.Sign(error) * Math.Min(MaxSlewRate, Math.Sqrt(2.0 * acceleration * Math.Abs(error)) * Damping);

        double demand = (wanted - rate) * inertia / dt;

        return Math.Clamp(demand, -maxTorque, maxTorque);

    }

}
