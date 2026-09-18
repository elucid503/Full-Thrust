using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The conic as four figures. A planned burn opens a second column beside the first, so
/// what the plan costs is read across a row rather than held in the head between two panels.</summary>
public sealed partial class ElementsPanel : Control {

    private const float Margin = 13.0f;

    private const float LabelWidth = 86.0f;
    private const float Column = 104.0f;

    private const float RowHeight = 21.0f;

    private const int Rows = 4;

    private const float Narrow = Margin * 2.0f + LabelWidth + Column;
    private const float Wide = Narrow + Column;

    // Fast enough that the column is there by the time the eye follows the node, slow enough that
    // the wipe reads as the panel opening rather than as a layout jump.
    private const float Sweep = 620.0f;

    public static readonly float Height = Margin * 2.0f + Rows * RowHeight;

    private static readonly string[] Names = { "APOAPSIS", "PERIAPSIS", "INCLINATION", "PERIOD" };

    private readonly TickReadout[] _live = new TickReadout[Rows];
    private readonly TickReadout[] _plan = new TickReadout[Rows];

    private float _width = Narrow;

    public override void _Ready() {

        Size = new Vector2(Narrow, Height);

        MouseFilter = MouseFilterEnum.Stop;

        // The planned column is drawn where the panel is not yet wide enough to hold it, so the
        // panel's own edge is what wipes it in.
        ClipContents = true;

        for (int row = 0; row < Rows; row++) {

            float y = Margin + RowHeight * row;

            _live[row] = Counter(HudTheme.Ink, new Rect2(Margin + LabelWidth, y, Column, RowHeight));
            _plan[row] = Counter(HudTheme.Dim, new Rect2(Margin + LabelWidth + Column, y, Column, RowHeight));

            _plan[row].Visible = false;

        }

    }

    public void Sync(Flight flight) {

        Orbit planned = flight.PlannedOrbit;

        _width = Mathf.MoveToward(_width, planned != null ? Wide : Narrow, Sweep * (float)GetProcessDeltaTime());

        Size = new Vector2(_width, Height);

        double radius = flight.Body.Radius;

        for (int row = 0; row < Rows; row++) {

            (double value, string text) = Element(row, flight.Orbit, radius);

            _live[row].Set(value, text);

            // Left showing what it last said while the column closes, so the wipe carries figures
            // out rather than a row of blanks.
            _plan[row].Visible = _width > Narrow + 1.0f;

            if (planned != null) {

                (double plannedValue, string plannedText) = Element(row, planned, radius);

                _plan[row].Set(plannedValue, plannedText);

            }

        }

        QueueRedraw();

    }

    public override void _Draw() {

        DrawStyleBox(HudTheme.Panel(0.0f), new Rect2(Vector2.Zero, Size));

        for (int row = 0; row < Rows; row++) {

            Rect2 box = new Rect2(Margin, Margin + RowHeight * row, LabelWidth, RowHeight);

            HudTheme.WriteIn(this, HudTheme.Label, HudTheme.Body, box, Names[row], HudTheme.Faint, HorizontalAlignment.Left);

        }

    }

    private TickReadout Counter(Color colour, Rect2 box) {

        TickReadout counter = new TickReadout { Position = box.Position, Size = box.Size };

        AddChild(counter);

        counter.Dress(HudTheme.Numeral, HudTheme.Body, colour, HorizontalAlignment.Right);

        return counter;

    }

    private static (double Value, string Text) Element(int row, Orbit orbit, double radius) {

        return row switch {

            0 => Apsis(orbit.ApoapsisRadius, radius),
            1 => Apsis(orbit.PeriapsisRadius, radius),

            2 => (orbit.Inclination, $"{orbit.Inclination * 180.0 / Math.PI:N1}°"),

            _ => (orbit.IsClosed ? orbit.Period : 0.0, Hud.Clock(orbit.Period)),

        };

    }

    private static (double Value, string Text) Apsis(double apsis, double radius) {

        if (double.IsInfinity(apsis) || double.IsNaN(apsis)) {

            return (0.0, "—");

        }

        return (apsis - radius, Hud.Distance(apsis - radius));

    }

}
