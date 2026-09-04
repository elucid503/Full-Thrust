using System.Collections.Generic;

namespace FullThrust.Sim;

/// <summary>A 2.4 m entry capsule, proportioned off NASA's own Orion crew module: the same
/// fineness ratio, the same wall angle, the same dished shield. Every radius after those is fixed
/// by making the next arc tangent to the last, so the shield, the shoulder, the wall and the dome
/// meet without a crease - and the shape that falls out is the one that flies shield first without
/// being told to.</summary>
public static class Aegis {

    public const double BaseRadius = 1.20;

    /// <summary>Height over diameter, measured off the model the mould line is taken from.</summary>
    public const double Fineness = 0.652;

    /// <summary>Radius the shield is dished to - one and a fifth diameters, as a real one is.
    /// Wider than the capsule, which is what puts the shock stand-off out where it belongs and
    /// keeps the stagnation heating survivable.</summary>
    public const double ShieldRadius = BaseRadius * 2.0 * 1.208;

    public const double ShoulderRadius = 0.15;

    /// <summary>The wall's angle off the nose axis.</summary>
    public const double WallAngle = 32.5 * Math.PI / 180.0;

    private const int ShieldStations = 16;
    private const int ShoulderStations = 8;
    private const int DomeStations = 10;

    // Where the shoulder's arc centre sits: a shoulder radius in from the widest point, and the
    // same distance inside the shield sphere. Everything else on the capsule follows from it.
    private static readonly double ShoulderCentreRadius = BaseRadius - ShoulderRadius;

    private static readonly double ShieldAngle = Math.Asin(ShoulderCentreRadius / (ShieldRadius - ShoulderRadius));

    private static readonly double ShoulderCentreHeight = ShieldRadius - (ShieldRadius - ShoulderRadius) * Math.Cos(ShieldAngle);

    /// <summary>Where the shoulder hands over to the conical wall.</summary>
    public static readonly double WallBase = ShoulderCentreHeight + ShoulderRadius * Math.Sin(WallAngle);

    private static readonly double WallBaseRadius = ShoulderCentreRadius + ShoulderRadius * Math.Cos(WallAngle);

    public static readonly double Height = BaseRadius * 2.0 * Fineness;

    // The dome is whatever radius closes the wall off at the height the real capsule stands at.
    // Solving for it rather than declaring it is what keeps the tangency and the proportion both.
    private static readonly double DomeRadius =
        (WallBase + WallBaseRadius / Math.Tan(WallAngle) - Height) * Math.Sin(WallAngle) / (1.0 - Math.Sin(WallAngle));

    private static readonly double DomeSeatRadius = DomeRadius * Math.Cos(WallAngle);

    /// <summary>Where the wall hands over to the dome, which is the same tangency condition again.</summary>
    public static readonly double DomeSeat = WallBase + (WallBaseRadius - DomeSeatRadius) / Math.Tan(WallAngle);

    public const double ShellMass = 850.0;

    /// <summary>The ablator, which is a third of the capsule and all of it on the base. This is why
    /// the centre of mass sits forward of the centre of pressure and the capsule is stable.</summary>
    public const double ShieldMass = 300.0;

    public const double DryMass = ShellMass + ShieldMass;

    // Four pods of two on the backshell. Small: they trim a capsule, they do not fly a stack.
    public const int RcsPods = 4;

    public const double RcsHeight = 0.85;
    public const double RcsHalfHeight = 0.12;
    public const double RcsPortRadius = 0.09;

    public const double RcsThrustNewtons = 400.0;
    public const double RcsSpecificImpulse = 210.0;
    public const double RcsPropellantMass = 45.0;

    public const double ControlTorque = 700.0;

    /// <summary>What the ablator holds. It soaks three times what bare structure does before it
    /// gets there, which is the difference between an entry and a burn-up.</summary>
    public const double HeatLimit = 1650.0;
    public const double HeatCapacity = 4200.0;

    /// <summary>Radius of the mould line at a station measured from the shield's own apex.</summary>
    public static double RadiusAt(double height) {

        if (height <= 0.0) {

            return 0.0;

        }

        if (height <= ShieldRadius * (1.0 - Math.Cos(ShieldAngle))) {

            return Math.Sqrt(Math.Max(ShieldRadius * ShieldRadius - (height - ShieldRadius) * (height - ShieldRadius), 0.0));

        }

        if (height <= WallBase) {

            double offset = height - ShoulderCentreHeight;

            return ShoulderCentreRadius + Math.Sqrt(Math.Max(ShoulderRadius * ShoulderRadius - offset * offset, 0.0));

        }

        if (height <= DomeSeat) {

            return WallBaseRadius - (height - WallBase) * Math.Tan(WallAngle);

        }

        double domeCentre = DomeSeat - DomeRadius * Math.Sin(WallAngle);

        return Math.Sqrt(Math.Max(DomeRadius * DomeRadius - (height - domeCentre) * (height - domeCentre), 0.0));

    }

    public static Hull BuildHull(double datum) {

        List<Hull.Station> stations = new List<Hull.Station>();

        // The shield, swept from its own apex. Stations bunch towards the shoulder, where the
        // profile turns hardest.
        for (int index = 0; index <= ShieldStations; index++) {

            double angle = ShieldAngle * index / ShieldStations;

            stations.Add(new Hull.Station(datum + ShieldRadius * (1.0 - Math.Cos(angle)), ShieldRadius * Math.Sin(angle)));

        }

        // The shoulder, as the normal swings from the shield's to the wall's. Both ends of the
        // sweep land exactly on the surfaces it joins, so neither meeting is a corner.
        double from = ShieldAngle - Math.PI * 0.5;

        for (int index = 1; index <= ShoulderStations; index++) {

            double angle = from + (WallAngle - from) * index / ShoulderStations;

            stations.Add(new Hull.Station(

                datum + ShoulderCentreHeight + ShoulderRadius * Math.Sin(angle),
                ShoulderCentreRadius + ShoulderRadius * Math.Cos(angle)));

        }

        stations.Add(new Hull.Station(datum + DomeSeat, DomeSeatRadius));

        double domeCentre = DomeSeat - DomeRadius * Math.Sin(WallAngle);

        for (int index = 1; index <= DomeStations; index++) {

            double angle = WallAngle + (Math.PI * 0.5 - WallAngle) * index / DomeStations;

            stations.Add(new Hull.Station(datum + domeCentre + DomeRadius * Math.Sin(angle), DomeRadius * Math.Cos(angle)));

        }

        // The tank span is nominal: the capsule carries no bulk propellant, only its bottle.
        return new Hull(stations.ToArray(), datum + WallBase, datum + DomeSeat) { WallThickness = 0.045 };

    }

    /// <summary>A pod in section: two nozzle mouths on a common base.</summary>
    private static Hull.Station[] BuildPod(double datum) {

        double low = datum + RcsHeight - RcsHalfHeight;
        double high = datum + RcsHeight + RcsHalfHeight;

        double middle = datum + RcsHeight;

        return new[] {

            new Hull.Station(low, RcsPortRadius * 0.5),
            new Hull.Station(low + 0.03, RcsPortRadius),
            new Hull.Station(middle - 0.03, RcsPortRadius),

            new Hull.Station(middle, RcsPortRadius * 0.4),

            new Hull.Station(middle + 0.03, RcsPortRadius),
            new Hull.Station(high - 0.03, RcsPortRadius),
            new Hull.Station(high, RcsPortRadius * 0.5),

        };

    }

    public static Part[] BuildParts(double datum) {

        return new[] {

            new Part {

                Name = "Heat Shield",

                Kind = PartKind.Shield,

                Bottom = datum,
                Top = datum + WallBase,

            },

            new Part {

                Name = "Crew Module",

                Kind = PartKind.Structure,

                Bottom = datum + WallBase,
                Top = datum + DomeSeat,

            },

            new Part {

                Name = "RCS Pod",

                Kind = PartKind.Thruster,

                Bottom = datum + RcsHeight - RcsHalfHeight,
                Top = datum + RcsHeight + RcsHalfHeight,

                Count = RcsPods,
                RingRadius = RadiusAt(RcsHeight),

                Profile = BuildPod(datum),

            },

            new Part {

                Name = "Forward Bay",

                Kind = PartKind.Structure,

                Bottom = datum + DomeSeat,
                Top = datum + Height,

            },

        };

    }

    public static Stage BuildStage(double datum) {

        Stage stage = new Stage {

            Name = "Aegis",

            Hull = BuildHull(datum),
            Parts = BuildParts(datum),

            // Its outside is a real crew module rather than a lathed one, so the model stands in
            // for the mould line where it is seen. The two agree to a couple of centimetres.
            Model = "capsule",

            ShellMass = ShellMass,

            // The ablator, as the dished slab it is, about its own centre.
            Ballast = new MassProperties(ShieldMass, datum + 0.16, new Vector3d(108.0, 108.0, 216.0)),

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
