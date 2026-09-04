namespace FullThrust.Sim;

public sealed class CelestialBody {

    public string Name { get; init; }

    public double Radius { get; init; }
    public double Mu { get; init; }

    public double RotationPeriodSeconds { get; init; }

    /// <summary>The air over the body, or null where there is none.</summary>
    public Atmosphere Atmosphere { get; init; }

    /// <summary>The ground under the air. Null until the survey is loaded, in which case the body
    /// is a sphere - which is what the physics suite runs against and what a body with no terrain
    /// data is anyway.</summary>
    public Terrain Terrain { get; set; }

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

    /// <summary>Angle the body has turned through since the epoch, radians.</summary>
    public double SpinAt(double time) => SpinRate * time;

    /// <summary>An inertial vector read in the frame the ground is drawn in.</summary>
    public Vector3d ToBodyFixed(Vector3d inertial, double time) => TurnAboutPole(inertial, -SpinAt(time));

    /// <summary>A body-fixed vector read back in the inertial frame the vehicles fly in.</summary>
    public Vector3d ToInertial(Vector3d bodyFixed, double time) => TurnAboutPole(bodyFixed, SpinAt(time));

    private static Vector3d TurnAboutPole(Vector3d value, double angle) {

        double sine = Math.Sin(angle);
        double cosine = Math.Cos(angle);

        return new Vector3d(value.X * cosine - value.Y * sine, value.X * sine + value.Y * cosine, value.Z);

    }

    /// <summary>Distance from the centre to the surface under a point: the terrain where there is
    /// a survey, the datum sphere where there is not.</summary>
    public double SurfaceRadiusUnder(Vector3d position, double time) {

        if (Terrain == null) {

            return Radius;

        }

        return Terrain.SurfaceRadius(ToBodyFixed(position, time));

    }

    /// <summary>Height over the ground rather than over the datum. What decides contact.</summary>
    public double HeightAboveGround(Vector3d position, double time) => position.Length - SurfaceRadiusUnder(position, time);

    /// <summary>Velocity of the air itself, which turns with the body. Only a hundred metres a
    /// second here, but it is the difference between air-relative and inertial speed and every
    /// aerodynamic figure is taken against the air.</summary>
    public Vector3d AirVelocityAt(Vector3d position) => Vector3d.Cross(Vector3d.UnitZ * SpinRate, position);

}
