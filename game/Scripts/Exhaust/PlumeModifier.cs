using System;

using Godot;

namespace FullThrust.Game;

/// <summary>One curve from a flight controller to one layer parameter, after Waterfall's modifiers.
/// Controllers are all normalised to 0..1 before the curve is sampled.</summary>
[GlobalClass]
public sealed partial class PlumeModifier : Resource {

    // Expansion is the nozzle's pressure ratio: 0.25 is ideally expanded, each quarter a decade either side.
    // Sunlit is how fully the plume stands in sunlight, for exhaust that scatters it.
    public enum Input {

        Throttle,
        Air,
        Mach,
        Flicker,
        Wobble,
        Ignition,
        Cutoff,
        Purge,
        Burnoff,
        Expansion,
        Sunlit,

    }

    public enum Target {

        Brightness,
        Length,
        Radius,
        ExpandLinear,
        ExpandSquare,
        Falloff,
        FalloffStart,
        Fresnel,
        FresnelFadeIn,
        FresnelInvert,
        Noise,
        NoiseFresnel,
        FadeIn,
        FadeOut,
        TintFalloff,
        TintFresnel,
        LengthBrightness,
        TileX,
        TileY,
        SpeedY,
        ConeLength,
        ConeStretch,
        ExitLength,
        ExitStart,
        ConeExpansion,
        ConeFade,
        ConeFadeStart,
        Smoothness,
        Asymmetry,
        StartTint,
        EndTint,

    }

    public enum Mode {

        Multiply,
        Add,
        Replace,

    }

    private static readonly string[] Uniforms = {

        "brightness",
        "",
        "",
        "expand_linear",
        "expand_square",
        "falloff",
        "falloff_start",
        "fresnel",
        "fresnel_fade_in",
        "fresnel_invert",
        "noise_amount",
        "noise_fresnel",
        "fade_in",
        "fade_out",
        "tint_falloff",
        "tint_fresnel",
        "length_brightness",
        "tile",
        "tile",
        "speed",
        "cone_length",
        "cone_stretch",
        "exit_length",
        "exit_start",
        "cone_expansion",
        "cone_fade",
        "cone_fade_start",
        "smoothness",
        "asymmetry",
        "start_tint",
        "end_tint",

    };

    public static readonly int TargetCount = Enum.GetValues<Target>().Length;

    [Export] public Input Controller { get; set; }
    [Export] public Target Parameter { get; set; }
    [Export] public Mode Combine { get; set; } = Mode.Multiply;

    /// <summary>Scalar targets sample this; the controller runs along X.</summary>
    [Export] public Curve Curve { get; set; }

    /// <summary>Tint targets sample this instead and always replace.</summary>
    [Export] public Gradient Gradient { get; set; }

    public bool IsColour => Parameter is Target.StartTint or Target.EndTint;

    /// <summary>The shader uniform a target writes; length and radius become the plume's scale instead.</summary>
    public static string Uniform(Target target) => Uniforms[(int)target];

    public float Apply(float current, float input) {

        if (Curve == null) {

            return current;

        }

        float value = Curve.Sample(Mathf.Clamp(input, 0.0f, 1.0f));

        return Combine switch {

            Mode.Add => current + value,
            Mode.Replace => value,
            _ => current * value,

        };

    }

}
