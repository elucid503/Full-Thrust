using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The list of attitude references, raised over the bar. It covers the screen so that a
/// click anywhere else dismisses it rather than falling through to the ship.</summary>
public sealed partial class AttitudeMenu : Control {

    private const int Columns = 2;

    private const float RowHeight = 26.0f;
    private const float CellWidth = 102.0f;

    private const float Inset = 5.0f;
    private const float Glyph = 22.0f;

    private readonly List<AttitudeHold> _rows = new List<AttitudeHold>();

    private Flight _flight;

    private Rect2 _panel;

    private int _height;
    private int _hovered = -1;

    public bool Open => Visible;

    public override void _Ready() {

        MouseFilter = MouseFilterEnum.Stop;

        Hide();

    }

    /// <summary>Raises the menu with its lower left corner at a point, listing what can be flown now.</summary>
    public void Raise(Flight flight, Vector2 corner) {

        _flight = flight;

        // Anchors resolve against a parent Control, and this one hangs off the canvas layer itself.
        // Without a rect of its own it still draws, but hit testing finds nothing and every pick
        // falls through to the ship behind it.
        Position = Vector2.Zero;
        Size = GetViewportRect().Size;

        _rows.Clear();

        foreach (AttitudeHold hold in AttitudeMarker.Selectable) {

            if (AttitudeMarker.Available(hold, flight)) {

                _rows.Add(hold);

            }

        }

        // Filled down one column and then the next, so the pairs that belong together - prograde
        // against retrograde, out against in - stay side by side however many rows there are.
        _height = (_rows.Count + Columns - 1) / Columns;

        float height = _height * RowHeight + Inset * 2.0f;

        _panel = new Rect2(corner.X, corner.Y - height, CellWidth * Columns + Inset * 2.0f, height);

        _hovered = -1;

        Show();

        QueueRedraw();

    }

    public void Dismiss() {

        Hide();

        _hovered = -1;

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

            _flight.Autopilot.Hold = _rows[row];

        }

        AcceptEvent();

        Dismiss();

    }

    public override void _Draw() {

        DrawStyleBox(HudTheme.Panel(0.0f), _panel);

        for (int index = 0; index < _rows.Count; index++) {

            AttitudeHold hold = _rows[index];

            Rect2 cell = Cell(index);

            bool current = _flight.Autopilot.Hold == hold;

            if (index == _hovered || current) {

                DrawStyleBox(HudTheme.Track(), cell);

            }

            Color ink = current || index == _hovered ? HudTheme.Ink : HudTheme.Dim;

            AttitudeMarker.Draw(this, hold, cell.Position + new Vector2(Glyph * 0.5f + 1.0f, RowHeight * 0.5f), 7.5f, ink, 1.3f);

            Rect2 text = new Rect2(cell.Position.X + Glyph + 4.0f, cell.Position.Y, cell.Size.X - Glyph - 4.0f, RowHeight);

            HudTheme.WriteIn(this, HudTheme.Label, HudTheme.Body, text, AttitudeMarker.Name(hold), ink, HorizontalAlignment.Left);

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
