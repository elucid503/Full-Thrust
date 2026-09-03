using System.Collections.Generic;

namespace FullThrust.Sim;

/// <summary>A 2.4m kerolox upper stage; one mould line for mesh and mass.</summary>
public static class Meridian {

    public const double BodyRadius = 1.20;

    public const double SkirtTop = 0.95;
    public const double TankTop = 7.70;

    // The nosecone is seated straight on the tank's forward flange; there is no adapter between them.
    public const double NoseBase = 7.90;

    // The ogive the nose is cut from. Its own length, before the tip is rounded off.
    public const double NoseLength = 3.60;

    public const double TipRadius = 0.16;

    // Radius of the generating arc of a tangent ogive: the one value that makes it meet the body wall
    // without a crease. Any other radius leaves a visible kink at the nose base.
    public const double OgiveRadius = (BodyRadius * BodyRadius + NoseLength * NoseLength) / (2.0 * BodyRadius);

    // Centre of the sphere the tip is rounded to, and the height where the ogive hands over to it.
    // Both fall out of requiring the two arcs to share a tangent, which is what keeps the join invisible.
    private static readonly double TipCentre = Math.Sqrt(
        (OgiveRadius - TipRadius) * (OgiveRadius - TipRadius) -
        (OgiveRadius - BodyRadius) * (OgiveRadius - BodyRadius));

    private static readonly double TipHandover = TipCentre * OgiveRadius / (OgiveRadius - TipRadius);

    public static readonly double NoseHeight = TipCentre + TipRadius;

    public static readonly double OverallLength = NoseBase + NoseHeight;

    private const int OgiveStations = 16;
    private const int TipStations = 6;

    // Proud rings. Each is a step rather than a ramp, so the lathe reads a hard edge off it instead
    // of smoothing the whole thing into a bulge.
    private const double BandRadius = BodyRadius + 0.025;
    private const double WeldRadius = BodyRadius + 0.012;

    public const double DryMass = 2400.0;

    // LOX and RP-1 at their loaded mixture ratio, averaged over the common-bulkhead tank.
    public const double PropellantDensity = 1029.0;

    // Four RCS quads on the forward tank, sized so the stage slews a right angle in about twenty seconds.
    public const double ControlTorque = 7000.0;

    // Hydrazine monoprop in its own spherical bottle, enough for a few hundred slews before the stage is deaf.
    public const double RcsThrustNewtons = 1_600.0;
    public const double RcsSpecificImpulse = 224.0;
    public const double RcsPropellantMass = 120.0;

    public const double ThrustNewtons = 180_000.0;
    public const double SpecificImpulse = 342.0;

    public static Hull BuildHull() {

        List<Hull.Station> stations = new List<Hull.Station> {

            new Hull.Station(0.00, BodyRadius),
            new Hull.Station(0.55, BodyRadius),

            new Hull.Station(SkirtTop, BodyRadius),
            new Hull.Station(0.98, BandRadius),
            new Hull.Station(1.06, BandRadius),
            new Hull.Station(1.09, BodyRadius),

            new Hull.Station(3.08, BodyRadius),
            new Hull.Station(3.10, WeldRadius),
            new Hull.Station(3.12, BodyRadius),

            new Hull.Station(5.38, BodyRadius),
            new Hull.Station(5.40, WeldRadius),
            new Hull.Station(5.42, BodyRadius),

            new Hull.Station(TankTop, BodyRadius),
            new Hull.Station(7.73, BandRadius),
            new Hull.Station(7.87, BandRadius),
            new Hull.Station(NoseBase, BodyRadius),

        };

        // Stations bunch towards the tip, where the profile turns hardest and evenly spaced ones
        // would flatten it into a cone.
        for (int index = 1; index <= OgiveStations; index++) {

            double fraction = (double)index / OgiveStations;
            double rise = TipHandover * (1.0 - (1.0 - fraction) * (1.0 - fraction));

            double radius = Math.Sqrt(OgiveRadius * OgiveRadius - rise * rise) - (OgiveRadius - BodyRadius);

            stations.Add(new Hull.Station(NoseBase + rise, radius));

        }

        double handover = Math.Atan2(TipRadius * (OgiveRadius - BodyRadius) / (OgiveRadius - TipRadius), TipHandover - TipCentre);

        for (int index = 1; index <= TipStations; index++) {

            double angle = handover * (1.0 - (double)index / TipStations);

            stations.Add(new Hull.Station(NoseBase + TipCentre + TipRadius * Math.Cos(angle), TipRadius * Math.Sin(angle)));

        }

        return new Hull(stations.ToArray(), SkirtTop, TankTop);

    }

    public static Vessel Build() {

        Hull hull = BuildHull();

        double capacity = hull.TankVolume * PropellantDensity;

        Vessel vessel = new Vessel {

            Name = "Meridian",

            Hull = hull,

            DryMass = DryMass,
            PropellantMass = capacity,
            PropellantCapacity = capacity,

            ThrustNewtons = ThrustNewtons,
            SpecificImpulse = SpecificImpulse,

            RcsPropellantMass = RcsPropellantMass,
            RcsPropellantCapacity = RcsPropellantMass,

            RcsThrustNewtons = RcsThrustNewtons,
            RcsSpecificImpulse = RcsSpecificImpulse,

            ControlTorqueLimit = ControlTorque,

        };

        vessel.RecomputeMassProperties();

        return vessel;

    }

}
