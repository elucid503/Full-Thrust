using Godot;

namespace FullThrust.Game;

/// <summary>One additive cylinder of a plume. Lengths and radii are in nozzle exit radii so a
/// template fits any bell; the expansion terms are extra radii gained by the tail.</summary>
[GlobalClass]
public sealed partial class PlumeLayer : Resource {

    [Export] public string Name { get; set; } = "Layer";

    [ExportGroup("Shape")]
    [Export] public float Length { get; set; } = 20.0f;
    [Export] public float Radius { get; set; } = 1.0f;
    [Export] public float Offset { get; set; }
    [Export] public float ExpandOffset { get; set; }
    [Export] public float ExpandLinear { get; set; }
    [Export] public float ExpandSquare { get; set; }
    [Export] public float ExpandBounded { get; set; }

    [ExportGroup("Shading")]
    [Export] public Color StartTint { get; set; } = Colors.White;
    [Export] public Color EndTint { get; set; } = Colors.White;
    [Export] public float TintFalloff { get; set; } = 1.0f;
    [Export] public float Brightness { get; set; } = 1.0f;
    [Export] public float Falloff { get; set; } = 1.0f;
    [Export] public float FalloffStart { get; set; }
    [Export] public float Fresnel { get; set; } = 1.0f;
    [Export] public float FresnelInvert { get; set; }
    [Export] public float FadeIn { get; set; }
    [Export] public float FadeOut { get; set; }
    [Export] public float Symmetry { get; set; }
    [Export] public float SymmetryStrength { get; set; }

    [ExportGroup("Noise")]
    [Export] public float Noise { get; set; } = 1.0f;
    [Export] public Vector2 Tile { get; set; } = new Vector2(1.0f, 1.0f);
    [Export] public Vector2 Scroll { get; set; } = new Vector2(0.0f, 1.0f);

    [ExportGroup("Shock Cells")]
    [Export] public float CellStrength { get; set; }
    [Export] public float CellPinch { get; set; }
    [Export] public float CellDecay { get; set; } = 3.0f;

    [ExportGroup("Response")]
    [Export] public Godot.Collections.Array<PlumeModifier> Modifiers { get; set; } = new();

    public float Base(PlumeModifier.Target target) {

        return target switch {

            PlumeModifier.Target.Brightness => Brightness,
            PlumeModifier.Target.Length => Length,
            PlumeModifier.Target.Radius => Radius,
            PlumeModifier.Target.ExpandOffset => ExpandOffset,
            PlumeModifier.Target.ExpandLinear => ExpandLinear,
            PlumeModifier.Target.ExpandSquare => ExpandSquare,
            PlumeModifier.Target.ExpandBounded => ExpandBounded,
            PlumeModifier.Target.Falloff => Falloff,
            PlumeModifier.Target.FalloffStart => FalloffStart,
            PlumeModifier.Target.Fresnel => Fresnel,
            PlumeModifier.Target.FresnelInvert => FresnelInvert,
            PlumeModifier.Target.Noise => Noise,
            PlumeModifier.Target.FadeIn => FadeIn,
            PlumeModifier.Target.FadeOut => FadeOut,
            PlumeModifier.Target.TileX => Tile.X,
            PlumeModifier.Target.TileY => Tile.Y,
            PlumeModifier.Target.ScrollY => Scroll.Y,
            PlumeModifier.Target.TintFalloff => TintFalloff,
            PlumeModifier.Target.CellStrength => CellStrength,

            _ => 0.0f,

        };

    }

    public void Write(ShaderMaterial material) {

        material.SetShaderParameter("start_tint", StartTint);
        material.SetShaderParameter("end_tint", EndTint);
        material.SetShaderParameter("tint_falloff", TintFalloff);
        material.SetShaderParameter("brightness", Brightness);
        material.SetShaderParameter("falloff", Falloff);
        material.SetShaderParameter("falloff_start", FalloffStart);
        material.SetShaderParameter("fresnel", Fresnel);
        material.SetShaderParameter("fresnel_invert", FresnelInvert);
        material.SetShaderParameter("fade_in", FadeIn);
        material.SetShaderParameter("fade_out", FadeOut);
        material.SetShaderParameter("symmetry", Symmetry);
        material.SetShaderParameter("symmetry_strength", SymmetryStrength);

        material.SetShaderParameter("expand_offset", ExpandOffset);
        material.SetShaderParameter("expand_linear", ExpandLinear);
        material.SetShaderParameter("expand_square", ExpandSquare);
        material.SetShaderParameter("expand_bounded", ExpandBounded);

        material.SetShaderParameter("noise_strength", Noise);
        material.SetShaderParameter("tile", Tile);
        material.SetShaderParameter("scroll", Scroll);

        material.SetShaderParameter("cell_strength", CellStrength);
        material.SetShaderParameter("cell_pinch", CellPinch);
        material.SetShaderParameter("cell_decay", CellDecay);

    }

}
