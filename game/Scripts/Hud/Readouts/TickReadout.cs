using System;
using System.Collections.Generic;

using Godot;

namespace FullThrust.Game;

/// <summary>A number that rolls to its new value one column at a time, like a mechanical counter.
/// Every readout in the interface that carries a changing figure is one of these.</summary>
public sealed partial class TickReadout : Control {

    private const float Duration = 0.19f;

    /// <summary>One column of the counter. Columns settle on their own, so a digit that never
    /// changes never moves while the one beside it is still rolling.</summary>
    private struct Column {

        public char From;
        public char To;

        public float Age;

    }

    // Indexed from the right, so a value gaining a digit grows leftwards without shifting the rest.
    private readonly List<Column> _columns = new List<Column>();

    private Font _font;
    private int _size;

    private Color _colour;
    private HorizontalAlignment _align;

    private double _value;
    private float _sense = 1.0f;

    public void Dress(Font font, int size, Color colour, HorizontalAlignment align) {

        _font = font;
        _size = size;

        _colour = colour;
        _align = align;

        // Columns leave through the top and bottom of the box, so the box has to cut them off.
        ClipContents = true;

        MouseFilter = MouseFilterEnum.Ignore;

        SetProcess(false);

    }

    /// <summary>Points the counter at a new reading. The value decides which way the columns roll.</summary>
    public void Set(double value, string text) {

        if (!double.IsNaN(value) && value != _value) {

            _sense = value > _value ? 1.0f : -1.0f;

            _value = value;

        }

        bool moved = false;

        for (int index = 0; index < Math.Max(text.Length, _columns.Count); index++) {

            char wanted = index < text.Length ? text[text.Length - 1 - index] : ' ';

            if (index >= _columns.Count) {

                _columns.Add(new Column { From = ' ', To = ' ', Age = Duration });

            }

            Column column = _columns[index];

            if (column.To == wanted) {

                continue;

            }

            column.From = column.To;
            column.To = wanted;
            column.Age = 0.0f;

            _columns[index] = column;

            moved = true;

        }

        // Blanks off the left end are dropped only once they have finished rolling away.
        while (_columns.Count > text.Length && _columns[_columns.Count - 1].To == ' ' && _columns[_columns.Count - 1].Age >= Duration) {

            _columns.RemoveAt(_columns.Count - 1);

        }

        if (moved) {

            SetProcess(true);

            QueueRedraw();

        }

    }

    public override void _Process(double delta) {

        bool rolling = false;

        for (int index = 0; index < _columns.Count; index++) {

            Column column = _columns[index];

            if (column.Age >= Duration) {

                continue;

            }

            column.Age += (float)delta;

            _columns[index] = column;

            rolling = true;

        }

        QueueRedraw();

        if (!rolling) {

            SetProcess(false);

        }

    }

    public override void _Draw() {

        if (_font == null || _columns.Count == 0) {

            return;

        }

        float cell = HudTheme.Width(_font, _size, "0");

        float baseline = (Size.Y + _size * HudTheme.NumeralCap) * 0.5f;

        float block = cell * _columns.Count;

        float left = _align switch {

            HorizontalAlignment.Right => Size.X - block,
            HorizontalAlignment.Center => (Size.X - block) * 0.5f,

            _ => 0.0f,

        };

        for (int index = 0; index < _columns.Count; index++) {

            Column column = _columns[index];

            float x = left + cell * (_columns.Count - 1 - index);

            if (column.Age >= Duration) {

                Glyph(column.To, x, baseline, 1.0f);

                continue;

            }

            float progress = Ease(column.Age / Duration);

            float travel = Size.Y * _sense;

            Glyph(column.From, x, baseline - travel * progress, 1.0f - progress);
            Glyph(column.To, x, baseline + travel * (1.0f - progress), progress);

        }

    }

    private void Glyph(char character, float x, float y, float alpha) {

        if (character == ' ' || alpha <= 0.0f) {

            return;

        }

        DrawChar(_font, new Vector2(x, y), character.ToString(), _size, _colour * new Color(1.0f, 1.0f, 1.0f, alpha));

    }

    // Slow at both ends, so a column that only moves a few pixels still reads as a roll.
    private static float Ease(float t) => t * t * (3.0f - 2.0f * t);

}
