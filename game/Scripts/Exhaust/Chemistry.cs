using Godot;

namespace FullThrust.Game;

/// <summary>What a propellant's exhaust looks like: the light it gives off on its own, the light
/// it gives off burning with the air, and the gas it expands as. Keyed on the fuel, because the
/// fuel is what decides all three - kerosene glows with soot, hydrogen barely glows at all.</summary>
public readonly struct Chemistry {

    /// <summary>Ratio of specific heats of the combustion products through the bell.</summary>
    public float Gamma { get; init; }

    public Color Core { get; init; }
    public Color Tail { get; init; }
    public Color Flame { get; init; }

    /// <summary>How readily the exhaust burns again in air; fuel-rich hydrocarbons do, a monopropellant does not.</summary>
    public float Afterburn { get; init; }

    /// <summary>How much light the gas gives off at all, relative to kerosene.</summary>
    public float Luminosity { get; init; }

    public static readonly Chemistry Kerosene = new Chemistry {

        Gamma = 1.22f,

        Core = new Color(1.00f, 0.84f, 0.66f),
        Tail = new Color(1.00f, 0.40f, 0.14f),
        Flame = new Color(1.00f, 0.56f, 0.16f),

        Afterburn = 1.0f,
        Luminosity = 1.0f,

    };

    public static readonly Chemistry Hydrogen = new Chemistry {

        Gamma = 1.26f,

        Core = new Color(0.78f, 0.84f, 1.00f),
        Tail = new Color(0.90f, 0.62f, 0.52f),
        Flame = new Color(1.00f, 0.66f, 0.46f),

        Afterburn = 0.35f,
        Luminosity = 0.30f,

    };

    public static readonly Chemistry Methane = new Chemistry {

        Gamma = 1.23f,

        Core = new Color(0.72f, 0.80f, 1.00f),
        Tail = new Color(0.86f, 0.52f, 0.62f),
        Flame = new Color(0.95f, 0.60f, 0.70f),

        Afterburn = 0.55f,
        Luminosity = 0.55f,

    };

    /// <summary>Decomposed monopropellant: hot nitrogen, hydrogen and ammonia, nearly colourless.</summary>
    public static readonly Chemistry Hydrazine = new Chemistry {

        Gamma = 1.27f,

        Core = new Color(0.86f, 0.90f, 1.00f),
        Tail = new Color(0.74f, 0.74f, 0.92f),
        Flame = new Color(0.90f, 0.80f, 0.90f),

        Afterburn = 0.05f,
        Luminosity = 0.12f,

    };

    /// <summary>The chemistry for a fuel by name. Anything unrecognised burns like kerosene, which
    /// is the safe reading of an unknown hydrocarbon.</summary>
    public static Chemistry For(Sim.Propellant fuel) {

        return fuel?.Name switch {

            "Liquid Hydrogen" => Hydrogen,
            "Liquid Methane" => Methane,
            "Hydrazine" => Hydrazine,

            _ => Kerosene,

        };

    }

}
