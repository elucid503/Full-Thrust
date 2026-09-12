using System.Collections.Generic;

namespace FullThrust.Sim;

/// <summary>A 2.4 m kerolox first stage. Six gimballed chambers and an open interstage deep enough
/// to swallow the Meridian's bell. It carries no thrusters of its own - the stack is trimmed from
/// the stage above - and exists to get off the pad and out of the thick air; everything after
/// staging is the Meridian's problem.</summary>
public static class Zenith {

    public const double BodyRadius = 1.20;

    public const double SkirtTop = 2.60;
    public const double TankTop = 15.14;

    /// <summary>Where the stage above sits. The interstage below it is hollow, and tall enough that
    /// the Meridian's bell hangs inside it rather than into this stage's forward dome.</summary>
    public const double PayloadDatum = 17.50;

    public const double StageTop = PayloadDatum;

    public const double DryMass = 3700.0;

    // The complete engine cluster and its turbomachinery, carried where it sits rather than smeared over
    // the shell: on a stage this size the engine is nearly half the dry mass on the bottom metre.
    public const double EngineMass = 1500.0;

    public const int EngineCount = 6;

    public const double ShellMass = DryMass - EngineMass;

    public const double MixtureRatio = 2.56;

    // Bare aluminium-lithium, the same wall the Meridian is built from and with the same limits.
    public const double HeatLimit = 900.0;
    public const double HeatCapacity = 1400.0;

    private const double BandRadius = BodyRadius + 0.025;
    private const double WeldRadius = BodyRadius + 0.012;

    public static readonly Propellant Fuel = Meridian.Fuel;
    public static readonly Propellant Oxidiser = Meridian.Oxidiser;

    public static readonly double PropellantDensity = Meridian.PropellantDensity;

    // The chamber swings on its mount, which is the whole of this stage's attitude control and what
    // actually flies the ascent: it costs nothing but the thrust that was already being made. Once
    // the engines are shut the stack is trimmed from the Meridian's rings alone.
    public const double GimbalRange = 6.0 * Math.PI / 180.0;

    /// <summary>Wall of the interstage. It is what the Meridian's bell has to clear on its way out,
    /// so the cavity the lathe draws and the cavity the collider uses both come off it.</summary>
    public const double InterstageWall = 0.055;

    public const double EngineDeck = 0.62;
    public const double EngineLength = 3.50;

    public const double EngineMouthRadius = 1.13;
    public const double EngineThroatRadius = 0.332;
    public const double EngineChamberRadius = 0.62;

    private const double ChamberDepth = 0.24;
    private const double ThroatDepth = 0.32;

    private const int BellStations = 14;

    // Sea level thrust against a loaded stack of ninety-eight tonnes: a liftoff thrust-to-weight of
    // 1.56. Lower than this and the burn is long enough that gravity takes more of it than the drag
    // saved is worth, which is exactly what the first flown ascent did at 1.3.
    public const double ThrustNewtons = 1_500_000.0;
    public const double SpecificImpulse = 300.0;

    // A staged-combustion chamber. With the bell drawn above it the exit pressure lands a little
    // under an atmosphere: full-flowing at sea level, which is what a first stage has to be.
    public const double ChamberPressure = 11_000_000.0;

    public static readonly double ExpansionRatio = EngineMouthRadius * EngineMouthRadius / (EngineThroatRadius * EngineThroatRadius);

    public static Hull BuildHull() {

        Hull.Station[] stations = {

            new Hull.Station(0.00, BodyRadius),
            new Hull.Station(0.60, BodyRadius),

            new Hull.Station(0.63, BandRadius),
            new Hull.Station(0.75, BandRadius),
            new Hull.Station(0.78, BodyRadius),

            new Hull.Station(SkirtTop, BodyRadius),
            new Hull.Station(2.63, BandRadius),
            new Hull.Station(2.73, BandRadius),
            new Hull.Station(2.76, BodyRadius),

            new Hull.Station(6.20, BodyRadius),
            new Hull.Station(6.22, WeldRadius),
            new Hull.Station(6.24, BodyRadius),

            new Hull.Station(9.80, BodyRadius),
            new Hull.Station(9.82, WeldRadius),
            new Hull.Station(9.84, BodyRadius),

            new Hull.Station(13.10, BodyRadius),
            new Hull.Station(13.12, WeldRadius),
            new Hull.Station(13.14, BodyRadius),

            new Hull.Station(TankTop, BodyRadius),
            new Hull.Station(15.17, BandRadius),
            new Hull.Station(15.29, BandRadius),
            new Hull.Station(15.32, BodyRadius),

            // The separation joint, where the bolts that hold the upper stage down actually are.
            new Hull.Station(17.24, BodyRadius),
            new Hull.Station(17.27, BandRadius),
            new Hull.Station(17.39, BandRadius),
            new Hull.Station(17.42, BodyRadius),

            new Hull.Station(StageTop, BodyRadius),

        };

        return new Hull(stations, SkirtTop, TankTop) {

            WallThickness = InterstageWall,

            // Open from the tank's forward dome to the separation plane: the Meridian's bell hangs
            // in the cavity rather than resting on a deck, and can foul its wall on the way out.
            BayFloor = TankTop,

        };

    }

    /// <summary>The nozzle in section, drawn from the same figures the engine is rated on.</summary>
    private static Hull.Station[] BuildBell() {

        double top = EngineDeck;
        double chamber = top - EngineLength * ChamberDepth;
        double throat = top - EngineLength * ThroatDepth;
        double mouth = top - EngineLength;

        List<Hull.Station> stations = new List<Hull.Station> {

            new Hull.Station(top, EngineChamberRadius * 0.70),
            new Hull.Station(top - 0.10, EngineChamberRadius),

            new Hull.Station(chamber, EngineChamberRadius),
            new Hull.Station(throat, EngineThroatRadius),

        };

        for (int index = 1; index <= BellStations; index++) {

            double fraction = (double)index / BellStations;

            double radius = EngineThroatRadius + (EngineMouthRadius - EngineThroatRadius) * Math.Sqrt(fraction);

            stations.Add(new Hull.Station(throat + (mouth - throat) * fraction, radius));

        }

        return stations.ToArray();

    }

    public static Part[] BuildParts() {

        return new[] {

            new Part {

                Name = "Main Engines",

                Count = EngineCount,

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

                Name = "Interstage",

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

            Name = "Zenith",

            Hull = hull,
            Parts = BuildParts(),

            ShellMass = ShellMass,

            // A metre-and-a-half powerhead sitting on the thrust structure: a solid of that size
            // about its own centre, a little under the deck it bolts to.
            Ballast = new MassProperties(EngineMass, EngineDeck - 0.55, new Vector3d(420.0, 420.0, 320.0)),

            PropellantMass = capacity,
            PropellantCapacity = capacity,

            ThrustNewtons = ThrustNewtons,
            SpecificImpulse = SpecificImpulse,

            ChamberPressure = ChamberPressure,
            ExpansionRatio = ExpansionRatio,

            MixtureRatio = MixtureRatio,

            Fuel = Fuel,
            Oxidiser = Oxidiser,

            GimbalRange = GimbalRange,

            HeatLimit = HeatLimit,
            HeatCapacity = HeatCapacity,

        };

        stage.CommissionEngines();

        return stage;

    }

}
