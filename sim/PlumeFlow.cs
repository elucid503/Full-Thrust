namespace FullThrust.Sim;

/// <summary>Bounded jet geometry from nozzle expansion and the ratio of ambient to exhaust momentum.</summary>
public readonly record struct PlumeFlow(double Length, double Spread, double ExitRadius, Vector3d Bend, double Opposing, double Standoff, double BlanketRadius) {

    public static PlumeFlow Solve(Exhaust exhaust, double bellRadius, double power, double density, Vector3d localWind, bool rcs = false) {

        double air = Math.Clamp(density / 1.225, 0.0, 1.0);
        double rarefied = exhaust.AmbientPressure > 0.0 ? Math.Clamp(Math.Log10(Math.Max(exhaust.PressureRatio, 1.0)) / 4.0, 0.0, 1.0) : 1.0;
        double spread = (0.075 + air * 0.055) * (1.0 - rarefied)
            + Math.Tan(Math.Clamp(exhaust.TurnAngle * 0.60, 0.0, 0.95)) * rarefied;
        double length = bellRadius * (rcs ? 28.0 : 105.0) * (0.35 + 0.65 * Math.Sqrt(Math.Clamp(power, 0.0, 1.0)));
        double exit = bellRadius * Math.Max(exhaust.Contraction, 0.2);
        double momentum = Math.Max(exhaust.ExitPressure * exhaust.ExitMach * exhaust.ExitMach * 1.22, 1.0);
        Vector3d transverse = new(localWind.X, localWind.Y, 0.0);
        double sideRatio = density * transverse.LengthSquared / momentum;
        Vector3d bend = transverse.Normalized * (length * Math.Clamp(Math.Sqrt(sideRatio) * 1.8, 0.0, 0.8));
        // Local +Z points back toward the chamber, opposing the outgoing -Z exhaust.
        double ram = density * Math.Pow(Math.Max(localWind.Z, 0.0), 2.0);
        double balance = exit * (Math.Sqrt(momentum / Math.Max(ram, 0.001)) - 1.0) / Math.Max(spread, 0.03);
        double standoff = Math.Clamp(balance, exit * 3.0, length);
        double opposing = Math.Clamp((length - standoff) / Math.Max(length * 0.65, 0.01), 0.0, 1.0);
        double blanket = (exit + standoff * spread) * (1.0 + opposing * 2.5);
        return new(length, spread, exit, bend, opposing, standoff, blanket);

    }

    public double SootExposure(Vector3d point) {

        double x = -point.Z;
        double u = Math.Clamp(x / Length, 0.0, 1.0);
        Vector3d centre = Bend * (u * u * u);
        double radial = Math.Sqrt(Math.Pow(point.X - centre.X, 2.0) + Math.Pow(point.Y - centre.Y, 2.0));
        double radius = ExitRadius + Math.Max(x, 0.0) * Spread;
        double core = x >= 0.0 && x < Math.Min(Length, Standoff * 1.3) && radial < radius * 1.5
            ? Math.Exp(-2.0 * radial * radial / (radius * radius)) * ExitRadius / radius : 0.0;
        double wrap = Opposing * Math.Exp(-Math.Pow((radial - BlanketRadius * 0.72) / Math.Max(BlanketRadius * 0.6, 0.01), 2.0))
            * Math.Exp(-Math.Pow((x - Standoff * 0.2) / Math.Max(Standoff + BlanketRadius * 2.0, 0.01), 2.0));
        return Math.Clamp(core + wrap * 0.45, 0.0, 1.0);

    }

}
