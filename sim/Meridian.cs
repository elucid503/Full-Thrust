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

    public const double MixtureRatio = 2.56;

    // Kerosene at ambient and oxygen at its boiling point. Everything the tank knows about density
    // comes from these two, so the mould line and the readouts cannot end up disagreeing about it.
    public static readonly Propellant Fuel = new Propellant {

        Name = "RP-1",

        Density = 820.0,
        Temperature = 288.0,

    };

    public static readonly Propellant Oxidiser = new Propellant {

        Name = "Liquid Oxygen",

        Density = 1141.0,
        Temperature = 90.0,

        IsCryogenic = true,

    };

    // The average over the common-bulkhead tank, which is the two species at the ratio they load at.
    public static readonly double PropellantDensity =
        (1.0 + MixtureRatio) / (1.0 / Fuel.Density + MixtureRatio / Oxidiser.Density);

    // Four RCS quads on the forward tank, sized so the stage slews a right angle in about twenty seconds.
    public const double ControlTorque = 7000.0;

    // Where the hardware that is not on the mould line sits. The lathe, the part list and the
    // craft diagram all read these, so none of them can place a nozzle the others disagree with.
    public const int RcsPorts = 4;

    public const double RcsHeight = 6.90;
    public const double RcsHalfHeight = 0.30;
    public const double RcsPortRadius = 0.18;

    public const double EngineDeck = 0.36;
    public const double EngineLength = 2.55;

    public const double EngineMouthRadius = 0.62;
    public const double EngineThroatRadius = 0.115;
    public const double EngineChamberRadius = 0.30;

    // Where the chamber ends and where the throat sits, as fractions of the engine's length below
    // the deck. The bell is everything under the throat.
    private const double ChamberDepth = 0.26;
    private const double ThroatDepth = 0.34;

    private const int BellStations = 12;

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

    /// <summary>The nozzle in section: a cylindrical chamber, a throat, and a bell opening out
    /// from it. Drawn from the same figures the engine is rated on rather than sketched.</summary>
    private static Hull.Station[] BuildBell() {

        double top = EngineDeck;
        double chamber = top - EngineLength * ChamberDepth;
        double throat = top - EngineLength * ThroatDepth;
        double mouth = top - EngineLength;

        List<Hull.Station> stations = new List<Hull.Station> {

            new Hull.Station(top, EngineChamberRadius * 0.72),
            new Hull.Station(top - 0.06, EngineChamberRadius),

            new Hull.Station(chamber, EngineChamberRadius),
            new Hull.Station(throat, EngineThroatRadius),

        };

        // Bell-shaped rather than conical: the radius opens fastest just past the throat and flattens
        // towards the mouth, which is what an actual contour does and what reads as an engine.
        for (int index = 1; index <= BellStations; index++) {

            double fraction = (double)index / BellStations;

            double radius = EngineThroatRadius + (EngineMouthRadius - EngineThroatRadius) * Math.Sqrt(fraction);

            stations.Add(new Hull.Station(throat + (mouth - throat) * fraction, radius));

        }

        return stations.ToArray();

    }

    /// <summary>A quad in section: one pocket in the tank wall with a nozzle mouth at each end of it.</summary>
    private static Hull.Station[] BuildQuad() {

        double low = RcsHeight - RcsHalfHeight;
        double high = RcsHeight + RcsHalfHeight;

        return new[] {

            new Hull.Station(low, RcsPortRadius * 0.55),
            new Hull.Station(low + 0.06, RcsPortRadius),
            new Hull.Station(RcsHeight - 0.06, RcsPortRadius),

            new Hull.Station(RcsHeight, RcsPortRadius * 0.42),

            new Hull.Station(RcsHeight + 0.06, RcsPortRadius),
            new Hull.Station(high - 0.06, RcsPortRadius),
            new Hull.Station(high, RcsPortRadius * 0.55),

        };

    }

    /// <summary>The stage broken into the pieces a pilot can point at, tail to nose.</summary>
    public static Part[] BuildParts() {

        return new[] {

            new Part {

                Name = "Main Engine",

                Kind = PartKind.Engine,

                Bottom = EngineDeck - EngineLength,
                Top = EngineDeck,

                Profile = BuildBell(),

            },

            new Part {

                Name = "Aft Skirt",

                Kind = PartKind.Structure,

                Bottom = 0.0,
                Top = SkirtTop,

            },

            new Part {

                Name = "Propellant Tank",

                Kind = PartKind.Tank,

                Bottom = SkirtTop,
                Top = TankTop,

            },

            new Part {

                Name = "RCS Quad",

                Kind = PartKind.Thruster,

                Bottom = RcsHeight - RcsHalfHeight,
                Top = RcsHeight + RcsHalfHeight,

                Count = RcsPorts,
                RingRadius = BodyRadius - RcsPortRadius,

                Profile = BuildQuad(),

            },

            new Part {

                Name = "Nose Fairing",

                Kind = PartKind.Structure,

                Bottom = TankTop,
                Top = OverallLength,

            },

        };

    }

    public static Vessel Build() {

        Hull hull = BuildHull();

        double capacity = hull.TankVolume * PropellantDensity;

        Vessel vessel = new Vessel {

            Name = "Meridian",

            Hull = hull,
            Parts = BuildParts(),

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

            MixtureRatio = MixtureRatio,

            Fuel = Fuel,
            Oxidiser = Oxidiser,

        };

        vessel.CommissionEngines();
        vessel.RecomputeMassProperties();

        return vessel;

    }

}
