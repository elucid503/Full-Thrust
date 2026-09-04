namespace FullThrust.Sim;

/// <summary>Where a vehicle stands before it flies. Holds the ground frame at the pad and the level
/// circle worked into the terrain under it, so the complex, the vehicle and the physics all read
/// one datum.</summary>
public sealed class LaunchSite {

    /// <summary>Cape Meridian: a barrier cape on the eastern seaboard, with open water downrange.</summary>
    public static LaunchSite Home => new LaunchSite {

        Name = "Cape Meridian",

        Latitude = 28.52 * Math.PI / 180.0,
        Longitude = -80.62 * Math.PI / 180.0,

        Azimuth = Math.PI * 0.5,

    };

    // The complex stands on this much dead-level ground, and the natural survey has fully returned
    // this far out. Two kilometres of blend on flat coastal ground is invisible from the pad.
    private const double LevelRadius = 420.0;
    private const double BlendRadius = 2600.0;

    /// <summary>Standoff of the pad deck over the natural ground, metres.</summary>
    private const double Standoff = 3.0;

    public string Name { get; init; }

    public double Latitude { get; init; }
    public double Longitude { get; init; }

    /// <summary>Which way the vehicle faces and flies, radians east of north.</summary>
    public double Azimuth { get; init; }

    /// <summary>Height of the pad deck above the datum, metres. Fixed when the site is commissioned.</summary>
    public double Height { get; private set; }

    /// <summary>Body-fixed frame at the pad: out of the ground, and the two compass axes on it.</summary>
    public Vector3d Up { get; private set; }
    public Vector3d East { get; private set; }
    public Vector3d North { get; private set; }

    /// <summary>The direction the vehicle is pointed downrange, body-fixed.</summary>
    public Vector3d Downrange => North * Math.Cos(Azimuth) + East * Math.Sin(Azimuth);

    /// <summary>Levels the ground under the complex and takes the pad's height off the result.
    /// Nothing about the site is usable until this has run.</summary>
    public void Commission(CelestialBody body) {

        double cosine = Math.Cos(Latitude);

        Up = new Vector3d(cosine * Math.Cos(Longitude), cosine * Math.Sin(Longitude), Math.Sin(Latitude));

        East = Vector3d.Cross(Vector3d.UnitZ, Up).Normalized;
        North = Vector3d.Cross(Up, East);

        if (body.Terrain == null) {

            Height = 0.0;

            return;

        }

        // Standing the deck on the natural ground rather than on a chosen figure keeps the pad on
        // the survey: move the site and the complex follows the coast up or down with it.
        Height = Math.Max(body.Terrain.NaturalElevation(Up), 0.0) + Standoff;

        body.Terrain.Add(new Terrain.Plateau {

            Centre = Up,
            Height = Height,

            InnerRadius = LevelRadius,
            OuterRadius = BlendRadius,

        });

    }

    /// <summary>The pad deck in the inertial frame, at a mission time.</summary>
    public Vector3d PositionAt(CelestialBody body, double time) => body.ToInertial(Up * (body.Radius + Height), time);

    /// <summary>Straight up out of the pad, inertial.</summary>
    public Vector3d UpAt(CelestialBody body, double time) => body.ToInertial(Up, time);

    /// <summary>A vehicle standing on the pad, nose up and rolled so its dorsal looks downrange.</summary>
    public QuaternionD AttitudeAt(CelestialBody body, double time) {

        return QuaternionD.LookAlong(body.ToInertial(Up, time), body.ToInertial(Downrange, time));

    }

    /// <summary>The velocity a body standing on the pad already has, from the planet's own spin.</summary>
    public Vector3d VelocityAt(CelestialBody body, double time) => body.AirVelocityAt(PositionAt(body, time));

}
