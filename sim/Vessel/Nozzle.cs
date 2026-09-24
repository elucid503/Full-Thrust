namespace FullThrust.Sim;

/// <summary>Quasi-one-dimensional isentropic flow through a convergent-divergent nozzle.</summary>
public static class Nozzle {

    /// <summary>Ratio of specific heats typical of hot combustion products.</summary>
    public const double ExhaustGamma = 1.2;

    /// <summary>Exit static pressure over chamber pressure for a fully supersonic nozzle with the
    /// given exit-to-throat area ratio. A ratio of one or less is the sonic throat itself.</summary>
    public static double PressureRatio(double areaRatio, double gamma = ExhaustGamma) {

        double mach = 1.0;

        if (areaRatio > 1.0) {

            // Area grows monotonically with Mach on the supersonic branch; bisect in log Mach.
            double low = 0.0;
            double high = Math.Log(60.0);

            for (int i = 0; i < 64; i++) {

                double middle = 0.5 * (low + high);

                if (AreaRatio(Math.Exp(middle), gamma) < areaRatio) {

                    low = middle;

                }
                else {

                    high = middle;

                }

            }

            mach = Math.Exp(0.5 * (low + high));

        }

        return Math.Pow(1.0 + 0.5 * (gamma - 1.0) * mach * mach, -gamma / (gamma - 1.0));

    }

    private static double AreaRatio(double mach, double gamma) {

        double stagnation = 2.0 / (gamma + 1.0) * (1.0 + 0.5 * (gamma - 1.0) * mach * mach);

        return Math.Pow(stagnation, (gamma + 1.0) / (2.0 * (gamma - 1.0))) / mach;

    }

}
