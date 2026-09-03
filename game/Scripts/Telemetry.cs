using System;
using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>Flight readouts. Reads the sim, formats, draws; Main decides when.</summary>
public sealed partial class Telemetry : CanvasLayer {

    private const int Margin = 26;
    private const int FontSize = 15;

    private static readonly Color Ink = new Color(0.88f, 0.93f, 0.97f);
    private static readonly Color Dim = new Color(0.58f, 0.66f, 0.74f);
    private static readonly Color Live = new Color(0.55f, 0.86f, 0.62f);

    private Label _labels;
    private Label _values;

    private Label _mode;

    private ColorRect _throttleTrack;
    private ColorRect _throttleFill;

    public override void _Ready() {

        AddChild(Frame(Control.GrowDirection.End, out HBoxContainer readout));

        _labels = Text(Dim, HorizontalAlignment.Right);
        _values = Text(Ink, HorizontalAlignment.Right);

        _labels.CustomMinimumSize = new Vector2(104.0f, 0.0f);
        _values.CustomMinimumSize = new Vector2(124.0f, 0.0f);

        readout.AddChild(_labels);
        readout.AddChild(_values);

        _labels.Text = string.Join('\n', "ALTITUDE", "SPEED", "APOAPSIS", "PERIAPSIS", "INCLINATION", "PERIOD", "MASS", "DELTA-V");

        AddChild(Frame(Control.GrowDirection.Begin, out HBoxContainer status));

        _mode = Text(Ink, HorizontalAlignment.Left);

        status.AddChild(_mode);

        _throttleTrack = new ColorRect {

            Color = new Color(1.0f, 1.0f, 1.0f, 0.14f),
            CustomMinimumSize = new Vector2(9.0f, 96.0f),

        };

        _throttleFill = new ColorRect {

            Color = new Color(1.0f, 0.62f, 0.28f),

            AnchorTop = 1.0f,
            AnchorBottom = 1.0f,
            AnchorRight = 1.0f,

            OffsetTop = 0.0f,

        };

        _throttleTrack.AddChild(_throttleFill);

        status.AddChild(_throttleTrack);

    }

    public void Sync(Flight flight) {

        Vessel vessel = flight.Vessel;
        Orbit orbit = flight.Orbit;

        _values.Text = string.Join(

            '\n',

            Distance(flight.Altitude),
            $"{vessel.Velocity.Length:N0} m/s",
            Distance(orbit.ApoapsisRadius - flight.Body.Radius),
            Distance(orbit.PeriapsisRadius - flight.Body.Radius),
            $"{Mathf.RadToDeg(orbit.Inclination):F2}°",
            Clock(orbit.Period),
            $"{vessel.Mass / 1000.0:F2} t",
            $"{vessel.DeltaV:N0} m/s"

        );

        string hold = flight.Autopilot.Hold switch {

            AttitudeHold.Off => "SAS OFF",
            AttitudeHold.Stability => "SAS HOLD",
            AttitudeHold.Prograde => "SAS PROGRADE",
            AttitudeHold.Retrograde => "SAS RETROGRADE",
            AttitudeHold.Normal => "SAS NORMAL",
            AttitudeHold.Antinormal => "SAS ANTI-NORMAL",
            AttitudeHold.RadialOut => "SAS RADIAL OUT",
            AttitudeHold.RadialIn => "SAS RADIAL IN",

            _ => "SAS OFF",

        };

        _mode.Text = $"{hold}\nWARP {flight.Warp:N0}x\nTHROTTLE {vessel.Throttle * 100.0:F0}%\nMET {Clock(flight.Time)}";
        _mode.Modulate = vessel.CurrentThrust > 0.0 ? Live : Ink;

        _throttleFill.OffsetTop = (float)(-96.0 * Math.Clamp(vessel.Throttle, 0.0, 1.0));

    }

    private static string Distance(double metres) {

        if (Math.Abs(metres) >= 1_000_000.0) {

            return $"{metres / 1000.0:N0} km";

        }

        return $"{metres / 1000.0:N2} km";

    }

    private static string Clock(double seconds) {

        if (double.IsInfinity(seconds) || double.IsNaN(seconds)) {

            return "—";

        }

        TimeSpan span = TimeSpan.FromSeconds(seconds);

        if (span.TotalHours >= 1.0) {

            return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";

        }

        return $"{span.Minutes:00}:{span.Seconds:00}";

    }

    private static PanelContainer Frame(Control.GrowDirection vertical, out HBoxContainer content) {

        StyleBoxFlat backing = new StyleBoxFlat {

            BgColor = new Color(0.02f, 0.04f, 0.06f, 0.42f),

            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,

            ContentMarginLeft = 16.0f,
            ContentMarginRight = 16.0f,
            ContentMarginTop = 12.0f,
            ContentMarginBottom = 12.0f,

        };

        PanelContainer panel = new PanelContainer();

        panel.AddThemeStyleboxOverride("panel", backing);

        // Anchored to one corner and grown away from it, so the box sizes itself to its text and never runs off screen.
        float edge = vertical == Control.GrowDirection.Begin ? 1.0f : 0.0f;

        panel.AnchorLeft = 0.0f;
        panel.AnchorRight = 0.0f;
        panel.AnchorTop = edge;
        panel.AnchorBottom = edge;

        panel.OffsetLeft = Margin;
        panel.OffsetRight = Margin;
        panel.OffsetTop = vertical == Control.GrowDirection.Begin ? -Margin : Margin;
        panel.OffsetBottom = panel.OffsetTop;

        panel.GrowHorizontal = Control.GrowDirection.End;
        panel.GrowVertical = vertical;

        content = new HBoxContainer();

        content.AddThemeConstantOverride("separation", 14);

        panel.AddChild(content);

        return panel;

    }

    private static Label Text(Color colour, HorizontalAlignment alignment) {

        Label label = new Label {

            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Top,

        };

        label.AddThemeFontSizeOverride("font_size", FontSize);
        label.AddThemeColorOverride("font_color", colour);

        label.AddThemeConstantOverride("outline_size", 4);
        label.AddThemeColorOverride("font_outline_color", new Color(0.0f, 0.0f, 0.0f, 0.55f));

        return label;

    }

}
