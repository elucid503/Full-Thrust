using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>A list of modes raised over the bar: the attitude references, or how the thrusters
/// fire. It covers the screen so that a click anywhere else dismisses it rather than falling
/// through to the ship.</summary>
public sealed partial class ModeMenu : Control {

    private const float RowHeight = 26.0f;
    private const float CellWidth = 102.0f;

    private const float Inset = 5.0f;
    private const float Glyph = 22.0f;

    private readonly record struct Row(string Name, Action<Control, Vector2, Color> Draw, Func<bool> Current, Action Pick);

    private readonly List<Row> _rows = new List<Row>();

    private Rect2 _panel;

    private int _height;
    private int _hovered = -1;

    public bool Open => Visible;

    public override void _Ready() {

        MouseFilter = MouseFilterEnum.Stop;

        Hide();

    }

    /// <summary>Raises the attitude references that can be flown now, lower left corner at a point.</summary>
    public void RaiseAttitude(Flight flight, Vector2 corner) {

        _rows.Clear();

        foreach (AttitudeHold hold in AttitudeMarker.Selectable) {

            if (AttitudeMarker.Available(hold, flight)) {

                _rows.Add(new Row(

                    AttitudeMarker.Name(hold),
                    (canvas, at, ink) => AttitudeMarker.Draw(canvas, hold, at, 7.5f, ink, 1.3f),

                    () => flight.Autopilot.Hold == hold,
                    () => flight.Autopilot.Hold = hold

                ));

            }

        }

        // Filled down one column and then the next, so the pairs that belong together - prograde
        // against retrograde, out against in - stay side by side however many rows there are.
        Raise(corner, 2);

    }

    /// <summary>Raises the choice of steady or pulsed thrusters, lower right corner at a point.</summary>
    public void RaiseThrusters(Flight flight, Vector2 corner) {

        _rows.Clear();

        _rows.Add(new Row("Steady", DrawSteady, () => !flight.RcsPulse, () => flight.RcsPulse = false));
        _rows.Add(new Row("Pulse", DrawPulse, () => flight.RcsPulse, () => flight.RcsPulse = true));

        Raise(corner - new Vector2(CellWidth + Inset * 2.0f, 0.0f), 1);

    }

    public void Dismiss() {

        Hide();

        _hovered = -1;

    }

    private void Raise(Vector2 corner, int columns) {

        // Anchors resolve against a parent Control, and this one hangs off the canvas layer itself.
        // Without a rect of its own it still draws, but hit testing finds nothing and every pick
        // falls through to the ship behind it.
        Position = Vector2.Zero;
        Size = GetViewportRect().Size;

        _height = (_rows.Count + columns - 1) / columns;

        float height = _height * RowHeight + Inset * 2.0f;

        _panel = new Rect2(corner.X, corner.Y - height, CellWidth * columns + Inset * 2.0f, height);

        _hovered = -1;

        Show();

        QueueRedraw();

    }

    public override void _GuiInput(InputEvent @event) {

        if (@event is InputEventMouseMotion motion) {

            int hovered = RowAt(motion.Position);

            if (hovered != _hovered) {

                _hovered = hovered;

                QueueRedraw();

            }

            return;

        }

        if (@event is not InputEventMouseButton button || !button.Pressed) {

            return;

        }

        int row = RowAt(button.Position);

        if (row >= 0) {

            _rows[row].Pick();

        }

        AcceptEvent();

        Dismiss();

    }

    public override void _Draw() {

        DrawStyleBox(HudTheme.Panel(0.0f), _panel);

        for (int index = 0; index < _rows.Count; index++) {

            Row row = _rows[index];

            Rect2 cell = Cell(index);

            bool current = row.Current();

            if (index == _hovered || current) {

                DrawStyleBox(HudTheme.Track(), cell);

            }

            Color ink = current || index == _hovered ? HudTheme.Ink : HudTheme.Dim;

            row.Draw(this, cell.Position + new Vector2(Glyph * 0.5f + 1.0f, RowHeight * 0.5f), ink);

            Rect2 text = new Rect2(cell.Position.X + Glyph + 4.0f, cell.Position.Y, cell.Size.X - Glyph - 4.0f, RowHeight);

            HudTheme.WriteIn(this, HudTheme.Label, HudTheme.Body, text, row.Name, ink, HorizontalAlignment.Left);

        }

    }

    private static void DrawSteady(Control canvas, Vector2 at, Color ink) {

        canvas.DrawLine(at + new Vector2(-7.0f, 0.0f), at + new Vector2(7.0f, 0.0f), ink, 2.0f, true);

    }

    private static void DrawPulse(Control canvas, Vector2 at, Color ink) {

        for (int dash = -1; dash <= 1; dash++) {

            Vector2 centre = at + new Vector2(dash * 5.5f, 0.0f);

            canvas.DrawLine(centre - new Vector2(1.5f, 0.0f), centre + new Vector2(1.5f, 0.0f), ink, 2.0f, true);

        }

    }

    private Rect2 Cell(int index) {

        return new Rect2(

            _panel.Position.X + Inset + CellWidth * (index / _height),
            _panel.Position.Y + Inset + RowHeight * (index % _height),

            CellWidth,
            RowHeight

        );

    }

    private int RowAt(Vector2 point) {

        for (int index = 0; index < _rows.Count; index++) {

            if (Cell(index).HasPoint(point)) {

                return index;

            }

        }

        return -1;

    }

}
