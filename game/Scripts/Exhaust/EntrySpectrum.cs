using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

internal readonly struct EntrySpectrum {

    public Color Hot { get; init; }
    public Color Cool { get; init; }
    public Color Ablation { get; init; }
    public float CoolingRate { get; init; }

    public static EntrySpectrum For(AeroForces air, double ambientTemperature, double skinTemperature) {

        double m2 = Math.Max(air.Mach * air.Mach, 1.0);
        double gamma = Atmosphere.HeatCapacityRatio;
        double compression = (gamma + 1.0) * m2 / ((gamma - 1.0) * m2 + 2.0);
        double pressureJump = (2.0 * gamma * m2 - gamma + 1.0) / (gamma + 1.0);

        // Frozen normal-shock temperature is an excitation proxy, not a chemical-equilibrium solution.
        double temperature = Math.Clamp(ambientTemperature * pressureJump / compression, 1200.0, 16000.0);
        double density = air.Density * compression;

        return new EntrySpectrum {

            Hot = Gas(temperature, density).LinearToSrgb(),
            Cool = Gas(Math.Max(temperature * 0.55, 1300.0), density * 0.3).LinearToSrgb(),
            Ablation = Continuum(Math.Clamp(skinTemperature + 700.0, 1500.0, 3200.0)).LinearToSrgb(),
            CoolingRate = (float)(2.0 + 5.0 * Math.Sqrt(density / (density + 0.03))),

        };

    }

    private static Color Gas(double temperature, double density) {

        const double BoltzmannEv = 8.617333262e-5;
        double energy = BoltzmannEv * temperature;

        // Relative excitation above the N2 first-positive upper state avoids underflow at the cool end.
        double red = 1.0 / (1.0 + density / 0.004);
        double blue = 180.0 * Math.Exp(-(11.03 - 7.35) / energy) / (1.0 + density / 0.15);
        double ion = 1200.0 * Math.Exp(-(18.75 - 7.35) / energy) / (1.0 + density / 0.06);
        double dissociation = Math.Clamp((temperature - 2500.0) / 4500.0, 0.0, 1.0);
        double oxygen = 25.2 * dissociation * Math.Exp(-(10.74 - 7.35) / energy) / (1.0 + density / 0.08);

        // RGB band integrals and quenching scales are a reduced visual model; see the adjacent notes.
        Vector3 bands = new Vector3(1.0f, 0.10f, 0.045f) * (float)red
            + new Vector3(0.24f, 0.20f, 1.0f) * (float)blue
            + new Vector3(0.22f, 0.30f, 1.0f) * (float)ion
            + new Vector3(1.0f, 0.018f, 0.002f) * (float)oxygen;

        bands /= Mathf.Max(bands.X, Mathf.Max(bands.Y, bands.Z));
        Color line = new Color(bands.X, bands.Y, bands.Z);
        float lineFraction = (float)(0.65 / (1.0 + density / 0.015));

        return Continuum(temperature).Lerp(line, lineFraction);

    }

    private static Color Continuum(double temperature) {

        // Planck samples, relative to a 6500 K white point; brightness is supplied by the heating model.
        double Channel(double wavelength) {

            double exponent = 0.01438776877 / (wavelength * 1.0e-9);

            return (Math.Exp(exponent / 6500.0) - 1.0) / (Math.Exp(exponent / temperature) - 1.0);

        }

        double r = Channel(650.0);
        double g = Channel(550.0);
        double b = Channel(450.0);
        double maximum = Math.Max(r, Math.Max(g, b));

        return new Color((float)(r / maximum), (float)(g / maximum), (float)(b / maximum));

    }

}
