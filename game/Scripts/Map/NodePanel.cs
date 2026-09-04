using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>What the planned burn is: the impulse the hand dialled, broken back into the three axes
/// the sim carries it in, what it costs to fly and what the tank has left afterwards.</summary>
public sealed partial class NodePanel : Control {

    private const float Margin = 10.0f;

    // The title sits on its own margin rather than on the hairline, the way the popover's does.
    private const float TitleTop = 9.0f;
    private const float Rule = 34.0f;

    private const float TotalTop = 40.0f;
    private const float TotalHeight = 26.0f;

    private const float RowHeight = 20.0f;

    private const float TriadTop = 74.0f;
    private const float Divide = 140.0f;

    private const float InfoTop = 144.0f;

    private const float ActionTop = 212.0f;
    private const float ActionHeight = 24.0f;

    // The buttons sit against the bottom hairline on the panel's own margin, which reads as a crop.
    private const float Foot = 16.0f;

    public static readonly Vector2 Extent = new Vector2(236.0f, ActionTop + ActionHeight + Foot);

    private static readonly string[] Axes = { "PROGRADE", "NORMAL", "RADIAL" };

    private Flight _flight;

    private TickReadout _total;

    private Button _aim;
    private Button _clear;

    public void Build(Flight flight) {

        _flight = flight;

        CustomMinimumSize = Extent;
        Size = Extent;

        MouseFilter = MouseFilterEnum.Stop;

        _total = new TickReadout {

            Position = new Vector2(Margin, TotalTop),
            Size = new Vector2(Extent.X - Margin * 2.0f - 34.0f, TotalHeight),

        };

        AddChild(_total);

        _total.Dress(HudTheme.NumeralStrong, HudTheme.Large, HudTheme.Ink, HorizontalAlignment.Right);

        float width = (Extent.X - Margin * 2.0f - 8.0f) * 0.5f;

        _aim = Action("AIM", new Vector2(Margin, ActionTop), width);
        _clear = Action("CLEAR", new Vector2(Extent.X - Margin - width, ActionTop), width);

        _aim.Pressed += Aim;
        _clear.Pressed += _flight.ClearNode;

    }

    public void Sync() {

        Maneuver node = _flight.Node;

        Visible = node != null;

        if (!Visible) {

            return;

        }

        _total.Set(node.DeltaV, $"{node.DeltaV:N0}");

        HudTheme.Light(_aim, _flight.Autopilot.Hold == AttitudeHold.Maneuver);

        _aim.Disabled = node.IsEmpty;

        QueueRedraw();

    }

    public override void _Draw() {

        Maneuver node = _flight.Node;

        if (node == null) {

            return;

        }

        Vessel vessel = _flight.Vessel;

        DrawStyleBox(HudTheme.Panel(0.0f), new Rect2(Vector2.Zero, Extent));

        HudTheme.WriteIn(this, HudTheme.Strong, HudTheme.Head, new Rect2(Margin, TitleTop, Extent.X - Margin * 2.0f, 20.0f), "MANEUVER", HudTheme.Ink, HorizontalAlignment.Left);

        Hairline(Rule);

        HudTheme.WriteIn(this, HudTheme.Label, HudTheme.Body, new Rect2(Extent.X - Margin - 30.0f, TotalTop, 30.0f, TotalHeight), "m/s", HudTheme.Faint, HorizontalAlignment.Right);

        double[] axes = { node.Prograde, node.Normal, node.Radial };

        for (int index = 0; index < axes.Length; index++) {

            Row(TriadTop + RowHeight * index, Axes[index], Signed(axes[index]), Math.Abs(axes[index]) > 0.5 ? HudTheme.Ink : HudTheme.Faint);

        }

        Hairline(Divide);

        double ignition = _flight.TimeToIgnition;
        double left = vessel.DeltaV - node.DeltaV;

        Row(InfoTop, "BURN", Hud.Clock(node.BurnSeconds(vessel)), node.IsEmpty ? HudTheme.Faint : HudTheme.Ink);

        // A node with nothing dialled in has no ignition to count to. NaN is not a countdown that
        // has run out, so it must not fall through to the reading that says the engine should be lit.
        if (double.IsNaN(ignition)) {

            Row(InfoTop + RowHeight, "IGNITION", "—", HudTheme.Faint);

        }
        else {

            Row(InfoTop + RowHeight, "IGNITION", ignition > 0.0 ? $"T–{Hud.Clock(ignition)}" : "BURNING", ignition > 0.0 ? HudTheme.Ink : HudTheme.Caution);

        }

        // The one figure that says whether the plan is flyable at all, so it is the one allowed a hue.
        Row(InfoTop + RowHeight * 2.0f, "REMAINING", Hud.Speed(Math.Max(left, 0.0)), left < 0.0 ? HudTheme.Caution : HudTheme.Ink);

    }

    private void Row(float y, string label, string value, Color ink) {

        Rect2 box = new Rect2(Margin, y, Extent.X - Margin * 2.0f, RowHeight);

        HudTheme.WriteIn(this, HudTheme.Label, HudTheme.Body, box, label, HudTheme.Faint, HorizontalAlignment.Left);
        HudTheme.WriteIn(this, HudTheme.Numeral, HudTheme.Body, box, value, ink, HorizontalAlignment.Right);

    }

    private void Hairline(float y) {

        DrawLine(new Vector2(Margin, y), new Vector2(Extent.X - Margin, y), HudTheme.Edge, 1.0f);

    }

    private void Aim() {

        if (_flight.Node != null && !_flight.Node.IsEmpty) {

            _flight.Autopilot.Hold = AttitudeHold.Maneuver;

        }

    }

    private Button Action(string text, Vector2 at, float width) {

        Button button = HudTheme.Button(text, new Vector2(width, ActionHeight));

        button.Position = at;
        button.Size = new Vector2(width, ActionHeight);

        AddChild(button);

        return button;

    }

    private static string Signed(double value) => value > 0.0 ? $"+{value:N0}" : $"{value:N0}";

}
