namespace FullThrust.Sim;

/// <summary>The air over a body: an exponential density column, a layered temperature curve, and
/// the speed of sound that falls out of it. One instance per body, loaded with the body.</summary>
public sealed class Atmosphere {

    // Dry air is the only gas anything is ever flown through here, so the ratio of specific heats
    // and the specific gas constant are constants rather than another two figures in the data file.
    public const double HeatCapacityRatio = 1.4;
    public const double GasConstant = 287.05;

    /// <summary>Density at datum level, kilograms per cubic metre.</summary>
    public double SeaLevelDensity { get; init; }

    /// <summary>The e-folding height of the density column, metres.</summary>
    public double ScaleHeight { get; init; }

    /// <summary>Altitude the air is taken to end at. Above it there is vacuum, exactly.</summary>
    public double Top { get; init; }

    public double SeaLevelTemperature { get; init; }

    /// <summary>Kelvin lost per metre of climb, up to the tropopause; isothermal above it.</summary>
    public double LapseRate { get; init; }

    public double TropopauseAltitude { get; init; }

    /// <summary>Density at the ceiling of a plain exponential, which is what the profile subtracts
    /// off itself so that the column reaches zero at the top instead of stepping off a cliff.</summary>
    private double Ceiling => Math.Exp(-Top / ScaleHeight);

    /// <summary>Density at an altitude. Exponential in shape, but shifted so it meets zero at the
    /// top of the air rather than being cut off there - a step in density is a step in drag.</summary>
    public double DensityAt(double altitude) {

        if (altitude >= Top || ScaleHeight <= 0.0) {

            return 0.0;

        }

        double height = Math.Max(altitude, 0.0);
        double ceiling = Ceiling;

        return SeaLevelDensity * (Math.Exp(-height / ScaleHeight) - ceiling) / (1.0 - ceiling);

    }

    public double TemperatureAt(double altitude) {

        double height = Math.Clamp(altitude, 0.0, Top);

        return SeaLevelTemperature - LapseRate * Math.Min(height, TropopauseAltitude);

    }

    public double SpeedOfSoundAt(double altitude) => Math.Sqrt(HeatCapacityRatio * GasConstant * TemperatureAt(altitude));

    /// <summary>Static pressure, from the density the column carries at the temperature it is at.</summary>
    public double PressureAt(double altitude) => DensityAt(altitude) * GasConstant * TemperatureAt(altitude);

}
