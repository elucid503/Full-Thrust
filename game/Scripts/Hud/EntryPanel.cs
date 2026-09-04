using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>What the air is doing, and how hot the leading skin has got. Up only while there is
/// air to fly through, so the resting interface never carries a row of zeroes.</summary>
public sealed partial class EntryPanel : Control {

    private const float PanelWidth = 272.0f;

    private const float Margin = 10.0f;
    private const float RowHeight = 17.0f;

    private const float LabelWidth = 62.0f;

    private const int Rows = 2;
    private const int Columns = 2;

    private const float Gauge = 5.0f;
    private const float GaugeGap = 8.0f;

    // Where the shield stops being comfortable. Below it the bar is ink like any other reading;
    // above it the one hue the interface keeps for trouble.
    private const float Warm = 0.72f;

    private static readonly float Cell = (PanelWidth - Margin * 2.0f) / Columns;

    public static readonly float Height = Margin * 2.0f + Rows * RowHeight + GaugeGap + Gauge + RowHeight;

    public static readonly Vector2 Extent = new Vector2(PanelWidth, Height);

    private Flight _flight;

    public override void _Ready() {

        CustomMinimumSize = Extent;
        Size = Extent;

        MouseFilter = MouseFilterEnum.Stop;

    }

    public void Sync(Flight flight) {

        _flight = flight;

        // The panel is the air's own instrument. Out of the air it is not dimmed, it is gone.
        Visible = flight.Vessel.Aero.InAir;

        if (Visible) {

            QueueRedraw();

        }

    }

    public override void _Draw() {

        if (_flight == null) {

            return;

        }

        Vessel vessel = _flight.Vessel;

        AeroForces air = vessel.Aero;

        DrawStyleBox(HudTheme.Panel(0.0f), new Rect2(Vector2.Zero, Extent));

        Cellule(0, 0, "MACH", $"{air.Mach:F2}");
        Cellule(1, 0, "DYN PR", $"{air.DynamicPressure / 1000.0:F1} kPa");

        Cellule(0, 1, "LOAD", $"{air.Force.Length / vessel.Mass / _flight.Body.SurfaceGravity:F1} g");
        Cellule(1, 1, "AoA", $"{Incidence(air) * 180.0 / Math.PI:F0}°");

        Heat(vessel);

    }

    /// <summary>How far off the flow the vehicle is flying, measured from whichever end is into it.
    /// A capsule at trim reads zero rather than a hundred and eighty.</summary>
    private static double Incidence(AeroForces air) {

        return air.AngleOfAttack > Math.PI * 0.5 ? Math.PI - air.AngleOfAttack : air.AngleOfAttack;

    }

    private void Cellule(int column, int row, string label, string value) {

        Rect2 box = new Rect2(Margin + Cell * column, Margin + RowHeight * row, Cell, RowHeight);

        HudTheme.WriteIn(this, HudTheme.Label, HudTheme.Small, box, label, HudTheme.Faint, HorizontalAlignment.Left);

        HudTheme.WriteIn(this, HudTheme.Numeral, HudTheme.Small, new Rect2(box.Position.X + LabelWidth, box.Position.Y, box.Size.X - LabelWidth - 12.0f, box.Size.Y), value, HudTheme.Ink, HorizontalAlignment.Right);

    }

    /// <summary>The skin, as a fraction of what it can survive. The one reading on the panel where
    /// the number alone is not enough - what matters is how near the end of the track it is.</summary>
    private void Heat(Vessel vessel) {

        float load = (float)Math.Clamp(vessel.HeatLoad, 0.0, 1.0);

        bool warm = load > Warm;

        Color ink = warm ? HudTheme.Caution : HudTheme.Ink;

        Rect2 caption = new Rect2(Margin, Margin + RowHeight * Rows + GaugeGap - 2.0f, Extent.X - Margin * 2.0f, RowHeight);

        HudTheme.WriteIn(this, HudTheme.Label, HudTheme.Small, caption, "SKIN", HudTheme.Faint, HorizontalAlignment.Left);

        HudTheme.WriteIn(this, HudTheme.Numeral, HudTheme.Small, caption, $"{vessel.SkinTemperature:N0} / {vessel.SkinLimit:N0} K", ink, HorizontalAlignment.Right);

        Rect2 track = new Rect2(Margin, caption.End.Y + 2.0f, Extent.X - Margin * 2.0f, Gauge);

        DrawStyleBox(HudTheme.Track(), track);

        DrawRect(new Rect2(track.Position, new Vector2(track.Size.X * load, Gauge)), ink);

        // The mark the bar must not reach, drawn once and not moving, so the eye has something to
        // measure the fill against rather than a bare colour change.
        float limit = track.Position.X + track.Size.X * Warm;

        DrawLine(new Vector2(limit, track.Position.Y - 2.0f), new Vector2(limit, track.End.Y + 2.0f), HudTheme.Caution * new Color(1.0f, 1.0f, 1.0f, 0.7f), 1.0f);

    }

}
