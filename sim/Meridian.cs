using System.Collections.Generic;

namespace FullThrust.Sim;

/// <summary>A 2.4m kerolox upper stage; one mould line for mesh and mass.</summary>
public static class Meridian {

    public const double OverallLength = 9.95;
    public const double BodyRadius = 1.20;

    public const double SkirtTop = 0.95;
    public const double TankTop = 7.70;

    // The capsule is seated straight on the tank's forward flange; there is no adapter between them.
    public const double NoseBase = 7.90;

    // A crew capsule closes on a flat deck carrying the hatch and the parachute bay, not on a point.
    public const double DeckRadius = 0.46;

    public const double DryMass = 2400.0;

    // LOX and RP-1 at their loaded mixture ratio, averaged over the common-bulkhead tank.
    public const double PropellantDensity = 1029.0;

    // Four RCS quads on the forward tank, sized so the stage slews a right angle in about twenty seconds.
    public const double ControlTorque = 7000.0;

    public const double ThrustNewtons = 180_000.0;
    public const double SpecificImpulse = 342.0;

    public static Hull BuildHull() {

        List<Hull.Station> stations = new List<Hull.Station> {

            new Hull.Station(0.00, BodyRadius),
            new Hull.Station(0.55, BodyRadius),

            new Hull.Station(SkirtTop, BodyRadius),
            new Hull.Station(1.05, BodyRadius + 0.02),
            new Hull.Station(1.15, BodyRadius),

            new Hull.Station(TankTop, BodyRadius),
            new Hull.Station(7.80, BodyRadius + 0.02),
            new Hull.Station(NoseBase, BodyRadius),

            // The capsule is a straight truncated cone off the tank flange; one station describes it.
            new Hull.Station(OverallLength, DeckRadius),

        };

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

}
