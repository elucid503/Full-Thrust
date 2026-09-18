using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>What is left when the vehicle is not there any more: what ended it, where, and the one
/// action still available. It is its own scrim, so nothing behind it can be clicked by mistake.</summary>
public sealed partial class LossPanel : Control {

    private const float PanelWidth = 288.0f;
    private const float PanelHeight = 132.0f;

    private const float Margin = 16.0f;

    private const float RowHeight = 18.0f;

    private const float ActionWidth = 108.0f;
    private const float ActionHeight = 26.0f;

    private Flight _flight;

    private Button _restart;

    public void Build(Flight flight) {

        _flight = flight;

        MouseFilter = MouseFilterEnum.Stop;

        _restart = HudTheme.Button("RESTART", new Vector2(ActionWidth, ActionHeight));

        _restart.Pressed += () => _flight.Restart();

        AddChild(_restart);

        Hide();

    }

    public void Sync() {

        Visible = _flight.Ended;

        if (!Visible) {

            return;

        }

        Vector2 screen = GetViewportRect().Size;

        Position = Vector2.Zero;
        Size = screen;

        _restart.Position = Box(screen).Position + new Vector2((PanelWidth - ActionWidth) * 0.5f, PanelHeight - Margin - ActionHeight);

        HudTheme.Light(_restart, true);

        QueueRedraw();

    }

    private static Rect2 Box(Vector2 screen) {

        return new Rect2((screen.X - PanelWidth) * 0.5f, (screen.Y - PanelHeight) * 0.5f, PanelWidth, PanelHeight);

    }

    public override void _GuiInput(InputEvent @event) {

        // Everything under the panel is gone; a click anywhere is only ever meant for the panel.
        if (@event is InputEventMouseButton) {

            AcceptEvent();

        }

    }

    public override void _Draw() {

        Vector2 screen = GetViewportRect().Size;

        DrawRect(new Rect2(Vector2.Zero, screen), new Color(0.008f, 0.011f, 0.016f, 0.55f));

        Rect2 box = Box(screen);

        DrawStyleBox(HudTheme.Panel(0.0f), box);

        Rect2 title = new Rect2(box.Position.X + Margin, box.Position.Y + Margin, box.Size.X - Margin * 2.0f, 22.0f);

        HudTheme.WriteIn(this, HudTheme.Strong, HudTheme.Head, title, "VEHICLE LOST", HudTheme.Caution, HorizontalAlignment.Left);

        DrawLine(new Vector2(box.Position.X + Margin, title.End.Y), new Vector2(box.End.X - Margin, title.End.Y), HudTheme.Edge, 1.0f);

        Row(box, 0, "CAUSE", Cause());
        Row(box, 1, "MISSION TIME", Hud.Clock(_flight.Time));

    }

    private void Row(Rect2 box, int index, string label, string value) {

        Rect2 row = new Rect2(box.Position.X + Margin, box.Position.Y + Margin + 26.0f + RowHeight * index, box.Size.X - Margin * 2.0f, RowHeight);

        HudTheme.WriteIn(this, HudTheme.Label, HudTheme.Small, row, label, HudTheme.Faint, HorizontalAlignment.Left);
        HudTheme.WriteIn(this, HudTheme.Numeral, HudTheme.Small, row, value, HudTheme.Ink, HorizontalAlignment.Right);

    }

    private string Cause() {

        return _flight.Fate switch {

            VesselFate.BurnedUp => "BURNED UP ON ENTRY",
            VesselFate.Impacted => "IMPACT WITH SURFACE",

            _ => "—",

        };

    }

}
