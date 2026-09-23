using Godot;

namespace FullThrust.Game;

// One emissive volume; lengths and expansion are authored in nozzle exit radii.
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
    [Export] public float VacuumOpening { get; set; } = 0.55f;
    [Export] public bool VacuumEnvelope { get; set; }
    [Export] public bool ShockOnly { get; set; }
    [Export] public bool ResidualGas { get; set; }
    [Export] public bool DiffuseTail { get; set; }

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

        material.SetParameter("vacuum_opening", VacuumOpening);
        material.SetParameter("vacuum_envelope", VacuumEnvelope ? 1.0f : 0.0f);
        material.SetParameter("shock_only", ShockOnly);
        material.SetParameter("residual_gas", ResidualGas ? 1.0f : 0.0f);
        material.SetParameter("diffuse_tail", DiffuseTail ? 1.0f : 0.0f);
        material.SetParameter("start_tint", StartTint);
        material.SetParameter("end_tint", EndTint);
        material.SetParameter("tint_falloff", TintFalloff);
        material.SetParameter("brightness", Brightness);
        material.SetParameter("falloff", Falloff);
        material.SetParameter("falloff_start", FalloffStart);
        material.SetParameter("fresnel", Fresnel);
        material.SetParameter("fresnel_invert", FresnelInvert);
        material.SetParameter("fade_in", FadeIn);
        material.SetParameter("fade_out", FadeOut);
        material.SetParameter("symmetry", Symmetry);
        material.SetParameter("symmetry_strength", SymmetryStrength);

        material.SetParameter("expand_offset", ExpandOffset);
        material.SetParameter("expand_linear", ExpandLinear);
        material.SetParameter("expand_square", ExpandSquare);
        material.SetParameter("expand_bounded", ExpandBounded);

        material.SetParameter("noise_strength", Noise);
        material.SetParameter("tile", Tile);
        material.SetParameter("scroll", Scroll);

        material.SetParameter("cell_strength", CellStrength);
        material.SetParameter("cell_pinch", CellPinch);
        material.SetParameter("cell_decay", CellDecay);

    }

}
