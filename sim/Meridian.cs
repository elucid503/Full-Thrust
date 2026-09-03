using System.Collections.Generic;

namespace FullThrust.Sim;

/// <summary>A 2.4m kerolox upper stage; one mould line for mesh and mass.</summary>
public static class Meridian {

    public const double OverallLength = 11.55;
    public const double BodyRadius = 1.20;

    public const double SkirtTop = 0.95;
    public const double TankTop = 7.70;

    public const double NoseBase = 9.75;
    public const double NoseRadius = 0.90;

    public const double DryMass = 2400.0;

    // LOX and RP-1 at their loaded mixture ratio, averaged over the common-bulkhead tank.
    public const double PropellantDensity = 1029.0;

    public const double ThrustNewtons = 180_000.0;
    public const double SpecificImpulse = 342.0;

    private const int NoseStations = 24;

    public static Hull BuildHull() {

        List<Hull.Station> stations = new List<Hull.Station> {

            new Hull.Station(0.00, BodyRadius),
            new Hull.Station(0.55, BodyRadius),

            new Hull.Station(SkirtTop, BodyRadius),
            new Hull.Station(1.05, BodyRadius + 0.02),
            new Hull.Station(1.15, BodyRadius),

            new Hull.Station(TankTop, BodyRadius),
            new Hull.Station(7.80, BodyRadius + 0.02),
            new Hull.Station(7.90, BodyRadius),

            new Hull.Station(8.55, 0.92),
            new Hull.Station(9.60, 0.92),
            new Hull.Station(NoseBase, NoseRadius),

        };

        AppendTangentOgive(stations);

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

        };

        vessel.RecomputeMassProperties();

        return vessel;

    }

    // Tangent ogive: the profile radius is a circular arc that meets the body wall without a crease.
    private static void AppendTangentOgive(List<Hull.Station> stations) {

        double length = OverallLength - NoseBase;
        double arc = (NoseRadius * NoseRadius + length * length) / (2.0 * NoseRadius);

        for (int index = 1; index <= NoseStations; index++) {

            double along = length * index / NoseStations;

            double radius = Math.Sqrt(arc * arc - along * along) + NoseRadius - arc;

            stations.Add(new Hull.Station(NoseBase + along, Math.Max(0.0, radius)));

        }

    }

}
