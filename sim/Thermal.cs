namespace FullThrust.Sim;

/// <summary>The leading skin as one temperature: heated by the stagnation flux, cooled by its own
/// radiation. A shield differs from bare structure by two numbers and nothing else.</summary>
public static class Thermal {

    public const double StefanBoltzmann = 5.670374e-8;

    /// <summary>How well the skin radiates. Both an ablator and an oxidised alloy sit near this,
    /// so it is a constant rather than another figure on every stage.</summary>
    public const double Emissivity = 0.85;

    /// <summary>What the skin relaxes to with no flux on it: deep space, near enough.</summary>
    public const double AmbientTemperature = 4.0;

    /// <summary>Temperature the flux alone would hold the skin at, where radiating away as much as
    /// it takes in. A reentry surface spends most of its time within a few degrees of this.</summary>
    public static double Equilibrium(double flux) {

        double fourth = Math.Max(flux, 0.0) / (Emissivity * StefanBoltzmann) + AmbientTemperature * AmbientTemperature * AmbientTemperature * AmbientTemperature;

        return Math.Pow(fourth, 0.25);

    }

    /// <summary>Advances the skin temperature. The step is never allowed past the equilibrium it is
    /// heading for, so a long step settles on it rather than ringing about it.</summary>
    public static double Step(double temperature, double flux, double capacity, double dt) {

        if (capacity <= 0.0 || dt <= 0.0) {

            return temperature;

        }

        double radiated = Emissivity * StefanBoltzmann * (Math.Pow(temperature, 4.0) - Math.Pow(AmbientTemperature, 4.0));

        double next = temperature + (flux - radiated) / capacity * dt;

        double settled = Equilibrium(flux);

        return flux > radiated ? Math.Min(next, settled) : Math.Max(next, settled);

    }

}
