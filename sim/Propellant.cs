namespace FullThrust.Sim;

/// <summary>A propellant as it is actually carried: what it is, how dense it is at the temperature
/// it is kept at, and what that temperature is.</summary>
public sealed class Propellant {

    public string Name { get; init; }

    /// <summary>Density at storage temperature, kilograms per cubic metre.</summary>
    public double Density { get; init; }

    /// <summary>Bulk temperature in the tank, kelvin.</summary>
    public double Temperature { get; init; }

    /// <summary>Whether the tank has to be kept cold to hold it. Storable propellants do not boil off.</summary>
    public bool IsCryogenic { get; init; }

}
