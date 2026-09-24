using System;

using Godot;

namespace FullThrust.Game;

// One Waterfall volume; length, radius and offset are authored in nozzle exit radii. Everything
// else is in Waterfall's own plume space, where the exit is y = 0 and the authored length y = -1.
[GlobalClass]
public sealed partial class PlumeLayer : Resource {

    public enum Kind {

        Volumetric,
        Cones,

    }

    [Export] public string Name { get; set; } = "Layer";
    [Export] public Kind Model { get; set; }

    [ExportGroup("Shape")]
    [Export] public float Length { get; set; } = 20.0f;
    [Export] public float Radius { get; set; } = 1.0f;
    [Export] public float Offset { get; set; }
    [Export] public float ExpandLinear { get; set; }
    [Export] public float ExpandSquare { get; set; }

    [ExportGroup("Cones")]
    [Export] public float ConeLength { get; set; } = 0.5f;
    [Export] public float ConeStretch { get; set; }
    [Export] public float ExitLength { get; set; } = 1.0f;
    [Export] public float ExitStart { get; set; } = 0.5f;
    [Export] public float ConeExpansion { get; set; }
    [Export] public float ConeFade { get; set; }
    [Export] public float ConeFadeStart { get; set; }
    [Export] public float Smoothness { get; set; } = 0.1f;
    [Export] public float Asymmetry { get; set; }

    [ExportGroup("Shading")]
    [Export] public Color StartTint { get; set; } = Colors.White;
    [Export] public Color EndTint { get; set; } = Colors.White;
    [Export] public float TintFalloff { get; set; }
    [Export] public float TintFresnel { get; set; }
    [Export] public float Brightness { get; set; } = 1.0f;
    [Export] public float LengthBrightness { get; set; } = 1.0f;
    [Export] public float Falloff { get; set; }
    [Export] public float FalloffStart { get; set; }
    [Export] public float Fresnel { get; set; }
    [Export] public float FresnelFadeIn { get; set; }
    [Export] public float FresnelInvert { get; set; }
    [Export] public float FadeIn { get; set; }
    [Export] public float FadeOut { get; set; }

    /// <summary>How much of the layer is sunlight scattered forward by the exhaust rather than its own glow.</summary>
    [Export] public float SunPhase { get; set; }

    [ExportGroup("Noise")]
    [Export] public float Noise { get; set; }
    [Export] public float NoiseFresnel { get; set; }
    [Export] public Vector2 Tile { get; set; } = new Vector2(1.0f, 1.0f);
    [Export] public Vector2 Speed { get; set; } = new Vector2(0.0f, 1.0f);

    [ExportGroup("Response")]
    [Export] public Godot.Collections.Array<PlumeModifier> Modifiers { get; set; } = new();

    public float Base(PlumeModifier.Target target) {

        return target switch {

            PlumeModifier.Target.Brightness => Brightness,
            PlumeModifier.Target.Length => Length,
            PlumeModifier.Target.Radius => Radius,
            PlumeModifier.Target.ExpandLinear => ExpandLinear,
            PlumeModifier.Target.ExpandSquare => ExpandSquare,
            PlumeModifier.Target.Falloff => Falloff,
            PlumeModifier.Target.FalloffStart => FalloffStart,
            PlumeModifier.Target.Fresnel => Fresnel,
            PlumeModifier.Target.FresnelFadeIn => FresnelFadeIn,
            PlumeModifier.Target.FresnelInvert => FresnelInvert,
            PlumeModifier.Target.Noise => Noise,
            PlumeModifier.Target.NoiseFresnel => NoiseFresnel,
            PlumeModifier.Target.FadeIn => FadeIn,
            PlumeModifier.Target.FadeOut => FadeOut,
            PlumeModifier.Target.TintFalloff => TintFalloff,
            PlumeModifier.Target.TintFresnel => TintFresnel,
            PlumeModifier.Target.LengthBrightness => LengthBrightness,
            PlumeModifier.Target.TileX => Tile.X,
            PlumeModifier.Target.TileY => Tile.Y,
            PlumeModifier.Target.SpeedY => Speed.Y,
            PlumeModifier.Target.ConeLength => ConeLength,
            PlumeModifier.Target.ConeStretch => ConeStretch,
            PlumeModifier.Target.ExitLength => ExitLength,
            PlumeModifier.Target.ExitStart => ExitStart,
            PlumeModifier.Target.ConeExpansion => ConeExpansion,
            PlumeModifier.Target.ConeFade => ConeFade,
            PlumeModifier.Target.ConeFadeStart => ConeFadeStart,
            PlumeModifier.Target.Smoothness => Smoothness,
            PlumeModifier.Target.Asymmetry => Asymmetry,

            _ => 0.0f,

        };

    }

    public void Write(ShaderMaterial material) {

        foreach (PlumeModifier.Target target in Enum.GetValues<PlumeModifier.Target>()) {

            if (target is PlumeModifier.Target.Length or PlumeModifier.Target.Radius or PlumeModifier.Target.TileX
                or PlumeModifier.Target.TileY or PlumeModifier.Target.SpeedY or PlumeModifier.Target.StartTint
                or PlumeModifier.Target.EndTint) {

                continue;

            }

            material.SetParameter(PlumeModifier.Uniform(target), Base(target));

        }

        material.SetParameter("start_tint", StartTint);
        material.SetParameter("end_tint", EndTint);
        material.SetParameter("tile", Tile);
        material.SetParameter("speed", Speed);
        material.SetParameter("sun_phase", SunPhase);

    }

}
