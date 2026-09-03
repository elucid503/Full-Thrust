namespace FullThrust.Sim;

public sealed class CelestialBody {

    public string Name { get; init; }

    public double Radius { get; init; }
    public double Mu { get; init; }

    public double RotationPeriodSeconds { get; init; }

    public double SurfaceGravity => Mu / (Radius * Radius);
    public double CircularVelocityAtSurface => Math.Sqrt(Mu / Radius);
    public double EscapeVelocityAtSurface => Math.Sqrt(2.0 * Mu / Radius);

    public double CircularVelocityAt(double altitude) => Math.Sqrt(Mu / (Radius + altitude));

    public double AltitudeOf(Vector3d position) => position.Length - Radius;

}
