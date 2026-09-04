namespace FullThrust.Sim;

/// <summary>What a nozzle's exhaust is doing as it leaves, against the air it leaves into. The
/// chamber pressure and the area ratio fix the exit state; the ambient pressure decides whether the
/// jet then bulges, necks or separates. Everything the plume's shape depends on comes from here.</summary>
public readonly struct Exhaust {

    /// <summary>Mach number at the exit plane.</summary>
    public double ExitMach { get; }

    /// <summary>Static pressure at the exit plane, pascals.</summary>
    public double ExitPressure { get; }

    public double AmbientPressure { get; }

    /// <summary>Exit over ambient. Above one the jet is under-expanded and bulges out of the bell;
    /// below one it is over-expanded and necks in. Infinite in vacuum.</summary>
    public double PressureRatio { get; }

    /// <summary>Mach the jet reaches once it has expanded or compressed to the ambient pressure. In
    /// vacuum there is no ambient to expand to and this is capped rather than infinite.</summary>
    public double JetMach { get; }

    /// <summary>Angle the flow turns through at the lip, radians. Positive turns outward, negative
    /// inward. A vacuum plume turns well past a right angle and wraps back around the bell.</summary>
    public double TurnAngle { get; }

    /// <summary>Spacing of the shock cells down the jet, metres. Zero where there are none: an
    /// ideally expanded jet has no diamonds and neither does one in vacuum.</summary>
    public double ShockCellLength { get; }

    /// <summary>Fraction of the exit radius the jet actually leaves through. One while the flow
    /// fills the bell; less once it has separated off the wall, deep in the air at low throttle.</summary>
    public double Contraction { get; }

    public bool IsSeparated => Contraction < 1.0;

    public Exhaust(double exitMach, double exitPressure, double ambientPressure, double jetMach, double turnAngle, double shockCellLength, double contraction) {

        ExitMach = exitMach;
        ExitPressure = exitPressure;
        AmbientPressure = ambientPressure;
        PressureRatio = ambientPressure > 0.0 ? exitPressure / ambientPressure : double.PositiveInfinity;
        JetMach = jetMach;
        TurnAngle = turnAngle;
        ShockCellLength = shockCellLength;
        Contraction = contraction;

    }

}

/// <summary>Quasi-one-dimensional isentropic nozzle flow. Ideal gas, frozen composition, one ratio
/// of specific heats for the whole expansion - the textbook model, which is within a few percent
/// of a real engine everywhere the plume can be seen.</summary>
public static class Nozzle {

    /// <summary>Wall pressure over ambient at which the boundary layer lets go of the bell. Summerfield's
    /// criterion, and the only figure in the separation model.</summary>
    public const double SeparationRatio = 0.4;

    /// <summary>Where the jet Mach is held in vacuum. High enough that the turning angle has
    /// converged to its limit; finite so that nothing downstream has to carry an infinity.</summary>
    public const double VacuumMach = 60.0;

    // Shock cell spacing over exit diameter goes as root of the jet Mach squared less one, with
    // this coefficient in front. Tam's fit to jet measurements.
    private const double CellCoefficient = 1.31;

    /// <summary>Exit-to-throat area ratio for a given supersonic exit Mach.</summary>
    public static double AreaRatio(double mach, double gamma) {

        double half = (gamma - 1.0) * 0.5;
        double exponent = (gamma + 1.0) / (2.0 * (gamma - 1.0));

        return Math.Pow(2.0 / (gamma + 1.0) * (1.0 + half * mach * mach), exponent) / mach;

    }

    /// <summary>Supersonic exit Mach for an area ratio. The relation is monotonic above one, so a
    /// bisection lands on it without a starting guess.</summary>
    public static double ExitMach(double areaRatio, double gamma) {

        if (areaRatio <= 1.0) {

            return 1.0;

        }

        double low = 1.0;
        double high = 2.0;

        while (AreaRatio(high, gamma) < areaRatio) {

            high *= 2.0;

        }

        for (int step = 0; step < 80; step++) {

            double middle = (low + high) * 0.5;

            if (AreaRatio(middle, gamma) < areaRatio) {

                low = middle;

            }
            else {

                high = middle;

            }

        }

        return (low + high) * 0.5;

    }

    /// <summary>Static over stagnation pressure at a Mach number.</summary>
    public static double PressureRatio(double mach, double gamma) {

        return Math.Pow(1.0 + (gamma - 1.0) * 0.5 * mach * mach, -gamma / (gamma - 1.0));

    }

    /// <summary>Mach at which an isentropic expansion from the chamber reaches a static pressure.</summary>
    public static double MachAtPressure(double chamberPressure, double pressure, double gamma) {

        if (pressure <= 0.0) {

            return VacuumMach;

        }

        double ratio = Math.Pow(chamberPressure / pressure, (gamma - 1.0) / gamma) - 1.0;

        if (ratio <= 0.0) {

            return 0.0;

        }

        return Math.Min(Math.Sqrt(2.0 / (gamma - 1.0) * ratio), VacuumMach);

    }

    /// <summary>The Prandtl-Meyer function: how far a supersonic flow has turned in expanding from
    /// Mach one to the given Mach. The difference between two of them is the turn between two states.</summary>
    public static double PrandtlMeyer(double mach, double gamma) {

        if (mach <= 1.0) {

            return 0.0;

        }

        double root = Math.Sqrt((gamma + 1.0) / (gamma - 1.0));
        double supersonic = Math.Sqrt(mach * mach - 1.0);

        return root * Math.Atan(supersonic / root) - Math.Atan(supersonic);

    }

    /// <summary>The exhaust state for an engine at a throttle setting, in air at a pressure. The
    /// chamber pressure scales with the throttle, so an engine throttled back in thick air
    /// over-expands harder than the same engine at full thrust.</summary>
    public static Exhaust Expand(double chamberPressure, double areaRatio, double gamma, double throttle, double ambientPressure, double exitRadius) {

        return ExpandFromMach(chamberPressure, ExitMach(areaRatio, gamma), gamma, throttle, ambientPressure, exitRadius);

    }

    /// <summary>Expansion with the nozzle's fixed exit Mach already solved, for repeated environment updates.</summary>
    public static Exhaust ExpandFromMach(double chamberPressure, double exitMach, double gamma, double throttle, double ambientPressure, double exitRadius) {

        double chamber = Math.Max(chamberPressure * Math.Clamp(throttle, 0.0, 1.0), 1.0);

        double exitPressure = chamber * PressureRatio(exitMach, gamma);

        double jetMach = MachAtPressure(chamber, ambientPressure, gamma);

        // Isentropic turning both ways: an expansion fan outward or, over-expanded, a compression
        // inward. The inward turn is a shock in truth, but its angle is within a degree of this.
        double turn = PrandtlMeyer(jetMach, gamma) - PrandtlMeyer(exitMach, gamma);

        double cellLength = 0.0;

        if (ambientPressure > 0.0 && jetMach > 1.0) {

            // A jet within a percent of ideal has cells too weak to see, so the length reads zero there.
            double mismatch = Math.Abs(Math.Log(exitPressure / ambientPressure));

            if (mismatch > 0.01) {

                cellLength = CellCoefficient * 2.0 * exitRadius * Math.Sqrt(jetMach * jetMach - 1.0);

            }

        }

        double contraction = 1.0;

        if (ambientPressure > 0.0 && exitPressure < SeparationRatio * ambientPressure) {

            double separationMach = MachAtPressure(chamber, SeparationRatio * ambientPressure, gamma);

            if (separationMach > 1.0) {

                contraction = Math.Clamp(Math.Sqrt(AreaRatio(separationMach, gamma) / AreaRatio(exitMach, gamma)), 0.0, 1.0);

            }

        }

        return new Exhaust(exitMach, exitPressure, ambientPressure, jetMach, turn, cellLength, contraction);

    }

}
