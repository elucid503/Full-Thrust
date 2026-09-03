using System;

using Godot;

namespace FullThrust.Game;

/// <summary>The flight interface. It reads the sim and lays itself out; it integrates nothing.</summary>
public sealed partial class Hud : CanvasLayer {

    private const float Margin = 22.0f;
    private const float Gap = 8.0f;

    private Flight _flight;

    private TrajectoryPanel _trajectory;
    private CraftPanel _craft;
    private Navball _navball;
    private PropellantGauge _gauge;
    private ModeBar _modes;
    private EnginePanel _engines;

    private Popover _popover;
    private AttitudeMenu _menu;

    public void Build(Flight flight) {

        _flight = flight;

        _trajectory = Attach(new TrajectoryPanel());
        _craft = Attach(new CraftPanel());
        _navball = Attach(new Navball());
        _gauge = Attach(new PropellantGauge());
        _modes = Attach(new ModeBar());
        _engines = Attach(new EnginePanel());

        // Both of these are raised over everything else, so they are the last things in the layer.
        _popover = Attach(new Popover());
        _menu = Attach(new AttitudeMenu());

        _craft.Build(flight.Vessel, _popover);
        _gauge.Build(flight.Vessel, _popover);
        _engines.Build(flight.Vessel);

        _modes.Bind(flight, _menu);

    }

    public void Sync() {

        // The map is its own mode with its own instruments still to come. Nothing here reads on it,
        // and the ball alone would cost a raster a frame for a panel nobody is looking at.
        if (MapView.Active != null && MapView.Active.Open) {

            if (Visible) {

                _menu.Dismiss();
                _popover.Dismiss();

                Visible = false;

            }

            return;

        }

        Visible = true;

        Place();

        _trajectory.Sync(_flight);
        _craft.Sync();
        _navball.Sync(_flight);
        _gauge.Sync();
        _modes.Sync();
        _engines.Sync();
        _popover.Sync();

    }

    // Recomputed every frame rather than anchored, so a resized window needs no notification path.
    private void Place() {

        Vector2 screen = GetViewport().GetVisibleRect().Size;

        _trajectory.Position = new Vector2(Margin, Margin);

        _craft.Position = new Vector2(screen.X - Margin - _craft.Size.X, Margin);

        float floor = screen.Y - Margin;

        _navball.Position = new Vector2(Margin, floor - Navball.Extent.Y);

        _gauge.Position = _navball.Position + new Vector2(Navball.Extent.X + Gap, 0.0f);

        _modes.Position = _navball.Position - new Vector2(0.0f, ModeBar.Extent.Y + Gap);

        _engines.Position = new Vector2(screen.X - Margin - _engines.Size.X, floor - _engines.Size.Y);

    }

    private T Attach<T>(T control) where T : Control {

        AddChild(control);

        return control;

    }

    public static string Distance(double metres) {

        if (double.IsInfinity(metres) || double.IsNaN(metres)) {

            return "—";

        }

        double magnitude = Math.Abs(metres);

        if (magnitude >= 100_000.0) {

            return $"{metres / 1000.0:N0} km";

        }

        if (magnitude >= 1000.0) {

            return $"{metres / 1000.0:N1} km";

        }

        return $"{metres:N0} m";

    }

    public static string Speed(double metresPerSecond) => $"{metresPerSecond:N0} m/s";

    public static string Clock(double seconds) {

        if (double.IsInfinity(seconds) || double.IsNaN(seconds)) {

            return "—";

        }

        TimeSpan span = TimeSpan.FromSeconds(Math.Max(seconds, 0.0));

        if (span.TotalHours >= 1.0) {

            return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";

        }

        return $"{span.Minutes:00}:{span.Seconds:00}";

    }

}
