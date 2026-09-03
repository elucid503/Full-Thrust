using System;
using System.Collections.Generic;

using Godot;

namespace FullThrust.Game;

/// <summary>A readout panel raised against whatever was clicked. It knows how to lay out a title,
/// a column of figures and a row of actions; what any of them say is the caller's business.</summary>
public sealed partial class Popover : Control {

    private const float Width = 208.0f;
    private const float Margin = 11.0f;

    private const float RowHeight = 17.0f;
    private const float HeaderHeight = 28.0f;
    private const float ActionHeight = 24.0f;

    private const float ActionWidth = 88.0f;

    private const float RowTop = 4.0f;
    private const float Foot = 9.0f;

    /// <summary>Fills the panel for whatever it is showing. Called every frame, so the figures are live.</summary>
    public delegate void Reader(List<(string Label, string Value)> rows, List<(string Label, Action Run)> actions);

    private readonly List<(string Label, string Value)> _rows = new List<(string, string)>();
    private readonly List<(string Label, Action Run)> _actions = new List<(string, Action)>();

    private object _subject;
    private string _title;

    private Reader _read;

    private Button _left;
    private Button _right;

    /// <summary>Whether the panel is up and showing this particular thing.</summary>
    public bool Shows(object subject) => Visible && ReferenceEquals(_subject, subject);

    public override void _Ready() {

        MouseFilter = MouseFilterEnum.Stop;

        _left = HudTheme.Button(string.Empty, new Vector2(ActionWidth, ActionHeight));
        _right = HudTheme.Button(string.Empty, new Vector2(ActionWidth, ActionHeight));

        AddChild(_left);
        AddChild(_right);

        _left.Pressed += () => Act(0);
        _right.Pressed += () => Act(1);

        Hide();

    }

    /// <summary>Opens against a point, on whichever side of it there is room for the panel.</summary>
    public void Raise(object subject, string title, Reader read, Vector2 anchor) {

        _subject = subject;
        _title = title;
        _read = read;

        Fill();

        float height = Floor() + (_actions.Count > 0 ? ActionHeight + 8.0f : 0.0f) + Foot;

        Size = new Vector2(Width, height);

        Vector2 viewport = GetViewportRect().Size;

        float x = anchor.X < viewport.X * 0.5f ? anchor.X : anchor.X - Width;
        float y = anchor.Y - height * 0.5f;

        Position = new Vector2(

            Mathf.Clamp(x, 12.0f, viewport.X - Width - 12.0f),
            Mathf.Clamp(y, 12.0f, viewport.Y - height - 12.0f)

        );

        Layout();

        Show();

    }

    public void Dismiss() {

        _subject = null;
        _read = null;

        Hide();

    }

    public void Sync() {

        if (!Visible) {

            return;

        }

        Fill();
        Layout();

        QueueRedraw();

    }

    public override void _Draw() {

        if (_read == null) {

            return;

        }

        DrawStyleBox(HudTheme.Panel(0.0f), new Rect2(Vector2.Zero, Size));

        HudTheme.WriteIn(this, HudTheme.Strong, HudTheme.Head, new Rect2(Margin, 4.0f, Width - Margin * 2.0f, 20.0f), _title, HudTheme.Ink, HorizontalAlignment.Left);

        DrawLine(new Vector2(Margin, HeaderHeight), new Vector2(Width - Margin, HeaderHeight), HudTheme.Edge, 1.0f);

        for (int index = 0; index < _rows.Count; index++) {

            Rect2 row = new Rect2(Margin, HeaderHeight + RowTop + RowHeight * index, Width - Margin * 2.0f, RowHeight);

            HudTheme.WriteIn(this, HudTheme.Label, HudTheme.Small, row, _rows[index].Label, HudTheme.Faint, HorizontalAlignment.Left);
            HudTheme.WriteIn(this, HudTheme.Numeral, HudTheme.Small, row, _rows[index].Value, HudTheme.Ink, HorizontalAlignment.Right);

        }

    }

    /// <summary>Where the rows stop. Everything below is measured from here, so the box never
    /// carries a margin sized for content it does not have.</summary>
    private float Floor() => HeaderHeight + RowTop + _rows.Count * RowHeight;

    private void Fill() {

        _rows.Clear();
        _actions.Clear();

        _read?.Invoke(_rows, _actions);

    }

    private void Layout() {

        float y = Floor() + 8.0f;

        Dress(_left, 0, new Vector2(Margin, y));
        Dress(_right, 1, new Vector2(Width - Margin - ActionWidth, y));

    }

    private void Dress(Button button, int index, Vector2 at) {

        button.Visible = index < _actions.Count;

        if (!button.Visible) {

            return;

        }

        button.Text = _actions[index].Label;
        button.Position = at;

    }

    private void Act(int index) {

        if (index < _actions.Count) {

            _actions[index].Run();

        }

    }

}
