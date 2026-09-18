using Godot;

namespace FullThrust.Game;

/// <summary>One curve from a flight input to one layer parameter, after Waterfall's modifiers.
/// Inputs are all normalised to 0..1 before the curve is sampled.</summary>
[GlobalClass]
public sealed partial class PlumeModifier : Resource {

    public enum Input {

        Throttle,
        Air,
        Mach,
        Flicker,
        Ignition,
        Cutoff,
        Purge,
        Burnoff,
        Cluster,

    }

    public enum Target {

        Brightness,
        Length,
        Radius,
        ExpandOffset,
        ExpandLinear,
        ExpandSquare,
        ExpandBounded,
        Falloff,
        FalloffStart,
        Fresnel,
        FresnelInvert,
        Noise,
        FadeIn,
        FadeOut,
        TileX,
        TileY,
        ScrollY,
        TintFalloff,
        CellStrength,
        StartTint,
        EndTint,

    }

    public enum Mode {

        Multiply,
        Add,
        Replace,

    }

    [Export] public Input Controller { get; set; }
    [Export] public Target Parameter { get; set; }
    [Export] public Mode Combine { get; set; } = Mode.Multiply;

    /// <summary>Scalar targets sample this; the input runs along X.</summary>
    [Export] public Curve Curve { get; set; }

    /// <summary>Tint targets sample this instead and always replace.</summary>
    [Export] public Gradient Gradient { get; set; }

    public bool IsColour => Parameter is Target.StartTint or Target.EndTint;

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
