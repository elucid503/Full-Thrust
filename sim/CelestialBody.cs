namespace FullThrust.Sim;

public sealed class CelestialBody {

    public string Name { get; init; }

    public double Radius { get; init; }
    public double Mu { get; init; }

    public double RotationPeriodSeconds { get; init; }

    /// <summary>The air over the body, or null where there is none.</summary>
    public Atmosphere Atmosphere { get; init; }

    public double SurfaceGravity => Mu / (Radius * Radius);
    public double CircularVelocityAtSurface => Math.Sqrt(Mu / Radius);
    public double EscapeVelocityAtSurface => Math.Sqrt(2.0 * Mu / Radius);

    public double CircularVelocityAt(double altitude) => Math.Sqrt(Mu / (Radius + altitude));

    public double AltitudeOf(Vector3d position) => position.Length - Radius;

    public bool HasAtmosphere => Atmosphere != null && Atmosphere.Top > 0.0;

    /// <summary>Altitude the air ends at, or zero for an airless body.</summary>
    public double AtmosphereTop => HasAtmosphere ? Atmosphere.Top : 0.0;

    public double AirDensityAt(Vector3d position) => HasAtmosphere ? Atmosphere.DensityAt(AltitudeOf(position)) : 0.0;

    /// <summary>Spin rate about the polar axis, radians per second.</summary>
    public double SpinRate => RotationPeriodSeconds > 0.0 ? Math.PI * 2.0 / RotationPeriodSeconds : 0.0;

    /// <summary>Velocity of the air itself, which turns with the body. Only a hundred metres a
    /// second here, but it is the difference between air-relative and inertial speed and every
    /// aerodynamic figure is taken against the air.</summary>
    public Vector3d AirVelocityAt(Vector3d position) => Vector3d.Cross(Vector3d.UnitZ * SpinRate, position);

}
