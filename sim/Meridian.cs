using System.Collections.Generic;

namespace FullThrust.Sim;

/// <summary>A 2.4 m kerolox service stage; one mould line for mesh and mass. It carries the
/// capsule that stacks on top of it as far as the entry interface and is then let go.</summary>
public static class Meridian {

    public const double BodyRadius = 1.20;

    public const double SkirtTop = 0.95;
    public const double TankTop = 7.70;

    // The payload adapter is seated straight on the tank's forward flange.
    public const double AdapterBase = 7.90;

    /// <summary>Where the payload's own datum sits. The adapter stands past it and closes over the
    /// shield below the capsule's shoulder, which is what keeps the stack's mould line unbroken.</summary>
    public const double PayloadDatum = 8.30;

    public const double StageTop = 8.62;

    public const double DryMass = 2400.0;

    // The engine is a fifth of the dry mass concentrated on the deck. Modelled where it sits rather
    // than smeared over the shell, because a stage's centre of mass is mostly a question of it.
    public const double EngineMass = 520.0;

    public const double ShellMass = DryMass - EngineMass;

    public const double MixtureRatio = 2.56;

    // Bare aluminium-lithium: the tank wall is the skin, and it does not survive an entry.
    public const double HeatLimit = 900.0;
    public const double HeatCapacity = 1400.0;

    // Proud rings. Each is a step rather than a ramp, so the lathe reads a hard edge off it instead
    // of smoothing the whole thing into a bulge.
    private const double BandRadius = BodyRadius + 0.025;
    private const double WeldRadius = BodyRadius + 0.012;

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

    // Four RCS quads on the forward tank, sized so the loaded stack slews a right angle in about
    // fifteen seconds.
    public const double ControlTorque = 7000.0;

    // Where the hardware that is not on the mould line sits. The lathe, the part list and the
    // craft diagram all read these, so none of them can place a nozzle the others disagree with.
    public const int RcsPorts = 4;

    public const double RcsHeight = 6.90;
    public const double RcsHalfHeight = 0.30;
    public const double RcsPortRadius = 0.18;
    public const double RcsPocketDepth = 0.26;

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

        Hull.Station[] stations = {

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
            new Hull.Station(AdapterBase, BodyRadius),

            // The separation joint, where the bolts that hold the capsule down actually are.
            // Stepped, not ramped: without the two stations at the body radius either side, the
            // lathe reads the whole adapter as one long taper up to the band.
            new Hull.Station(8.38, BodyRadius),
            new Hull.Station(8.40, BandRadius),
            new Hull.Station(8.50, BandRadius),
            new Hull.Station(8.52, BodyRadius),

            new Hull.Station(StageTop, BodyRadius),

        };

        return new Hull(stations, SkirtTop, TankTop);

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

                Depth = RcsPocketDepth,

                Profile = BuildQuad(),

            },

            new Part {

                Name = "Payload Adapter",

                Kind = PartKind.Structure,

                Bottom = TankTop,
                Top = StageTop,

            },

        };

    }

    public static Stage BuildStage() {

        Hull hull = BuildHull();

        double capacity = hull.TankVolume * PropellantDensity;

        Stage stage = new Stage {

            Name = "Meridian",

            Hull = hull,
            Parts = BuildParts(),

            ShellMass = ShellMass,

            // A sixty centimetre powerhead on the deck: a solid of that size about its own centre.
            Ballast = new MassProperties(EngineMass, EngineDeck - 0.21, new Vector3d(75.0, 75.0, 60.0)),

            PropellantMass = capacity,
            PropellantCapacity = capacity,

            ThrustNewtons = ThrustNewtons,
            SpecificImpulse = SpecificImpulse,

            MixtureRatio = MixtureRatio,

            Fuel = Fuel,
            Oxidiser = Oxidiser,

            RcsThrustNewtons = RcsThrustNewtons,
            RcsSpecificImpulse = RcsSpecificImpulse,

            ControlTorque = ControlTorque,

            RcsPropellantMass = RcsPropellantMass,
            RcsPropellantCapacity = RcsPropellantMass,

            HeatLimit = HeatLimit,
            HeatCapacity = HeatCapacity,

        };

        stage.CommissionEngines();

        return stage;

    }

}
