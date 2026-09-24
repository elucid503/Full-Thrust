using System.Collections.Generic;

using Godot;

namespace FullThrust.Game;

/// <summary>Everything one propellant's exhaust looks like: the Waterfall layer stack per nozzle, the
/// marched tail each stage trails, the light it throws, how the bell heats and what it kicks up.</summary>
[GlobalClass]
public sealed partial class PlumeTemplate : Resource {

    private static readonly Dictionary<string, PlumeTemplate> Cache = new();

    [Export] public Godot.Collections.Array<PlumeLayer> Layers { get; set; } = new();

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

    /// <summary>Waterfall's random controllers: combustion flicker, and the slower wander of the shock train.</summary>
    [Export] public float FlickerHertz { get; set; } = 6.0f;
    [Export] public float WobbleHertz { get; set; } = 1.2f;

    // Lengths are in exit radii of the stage's merged jet; speed is exit radii per second at the exit.
    [ExportGroup("Tail")]
    [Export] public Color TailHot { get; set; } = new Color(1.0f, 0.55f, 0.2f);
    [Export] public Color TailCool { get; set; } = new Color(0.55f, 0.12f, 0.03f);
    [Export] public float TailEmission { get; set; } = 1.0f;
    [Export] public float TailLength { get; set; } = 40.0f;
    [Export] public float TailSpread { get; set; } = 0.08f;
    [Export] public float TailHandover { get; set; } = 7.0f;
    [Export] public float TailSpeed { get; set; } = 30.0f;
    [Export] public float TailTurbulence { get; set; } = 1.0f;

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
