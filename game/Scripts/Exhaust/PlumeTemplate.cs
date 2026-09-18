using System.Collections.Generic;

using Godot;

namespace FullThrust.Game;

/// <summary>Everything one propellant's exhaust looks like: the layer stack per nozzle, the shared
/// cluster tail, the light it throws, how the bell heats and what it kicks up off the ground.</summary>
[GlobalClass]
public sealed partial class PlumeTemplate : Resource {

    private static readonly Dictionary<string, PlumeTemplate> Cache = new();

    [Export] public Godot.Collections.Array<PlumeLayer> Layers { get; set; } = new();

    /// <summary>Drawn once per stage at the cluster centre when more than one nozzle is lit.</summary>
    [Export] public Godot.Collections.Array<PlumeLayer> ClusterLayers { get; set; } = new();

    [ExportGroup("Light")]
    [Export] public Color LightColour { get; set; } = new Color(1.0f, 0.84f, 0.66f);
    [Export] public Color LightAirColour { get; set; } = new Color(1.0f, 0.56f, 0.16f);
    [Export] public float LightEnergy { get; set; } = 1.15f;
    [Export] public float LightRange { get; set; } = 16.0f;

    [ExportGroup("Bell")]
    [Export] public Color GlowColour { get; set; } = new Color(1.0f, 0.36f, 0.10f);
    [Export] public float GlowEnergy { get; set; } = 2.5f;
    [Export] public float HeatRiseSeconds { get; set; } = 2.5f;
    [Export] public float HeatFallSeconds { get; set; } = 9.0f;

    [ExportGroup("Flow")]
    [Export] public float Distortion { get; set; } = 1.0f;
    [Export] public float IgnitionSeconds { get; set; } = 1.2f;
    [Export] public float CutoffSeconds { get; set; } = 1.0f;
    [Export] public float FlickerHertz { get; set; } = 6.0f;

    /// <summary>Scales the shock cell contrast derived from the nozzle's pressure mismatch.</summary>
    [Export] public float CellContrast { get; set; } = 1.0f;

    [ExportGroup("Surface")]
    [Export] public Color SmokeColour { get; set; } = new Color(0.36f, 0.33f, 0.30f);
    [Export] public float SmokeAmount { get; set; } = 1.0f;

    public static PlumeTemplate For(Sim.Propellant fuel) {

        string name = fuel?.Name switch {

            "Liquid Hydrogen" => "Hydrolox",
            "Liquid Methane" => "Methalox",
            "Hydrazine" => "Hydrazine",

            _ => "Kerolox",

        };

        return Load(name);

    }

    public static PlumeTemplate Load(string name) {

        if (!Cache.TryGetValue(name, out PlumeTemplate template)) {

            template = GD.Load<PlumeTemplate>($"res://Effects/Exhaust/{name}.tres");
            Cache[name] = template;

        }

        return template;

    }

}
