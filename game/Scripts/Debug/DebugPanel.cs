using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The development console: a camera cut loose from the vehicle, cheats for the vehicle,
/// and the figures the flight interface has no business showing. F1 raises it.</summary>
public sealed partial class DebugPanel : CanvasLayer {

    private const float Width = 274.0f;
    private const float Margin = 22.0f;

    private const float Inset = 11.0f;

    private const float ChipHeight = 22.0f;
    private const float RowHeight = 15.0f;

    // Over the flight interface and over the planning one, both of which sit on the layers below.
    private const int Raised = 10;

    private static readonly double[] Timescales = { 0.25, 1.0, 4.0, 10.0, 20.0 };

    private Main _main;
    private Flight _flight;
    private FreeCamera _free;
    private OrbitCamera _chase;
    private Hud _hud;

    private PanelContainer _frame;
    private ScrollContainer _scroll;
    private VBoxContainer _column;
    private HFlowContainer _row;
    private Control _readout;

    private readonly List<(Button Chip, Func<bool> Lit)> _lamps = new List<(Button, Func<bool>)>();
    private readonly List<(string Label, Func<string> Value)> _rows = new List<(string, Func<string>)>();

    public void Build(Main main, Flight flight, FreeCamera free, OrbitCamera chase, Hud hud) {

        _main = main;
        _flight = flight;
        _free = free;
        _chase = chase;
        _hud = hud;

        Layer = Raised;

        // The wireframe pass needs its meshes rebuilt with edge adjacency, and asking for that only
        // when the switch is thrown leaves every patch already paged in drawn as a solid.
        RenderingServer.SetDebugGenerateWireframes(true);

        _frame = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(Width, 0.0f) };

        _frame.AddThemeStyleboxOverride("panel", HudTheme.Panel(Inset));

        AddChild(_frame);

        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };

        _scroll.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

        _frame.AddChild(_scroll);

        Bar(_scroll.GetVScrollBar());

        _column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

        _column.AddThemeConstantOverride("separation", 6);

        _scroll.AddChild(_column);

        Write("DEBUG", HudTheme.Strong, HudTheme.Head, HudTheme.Ink);

        Section("CAMERA");

        Row();
        Chip("FREE", ToggleFreeCamera, () => FreeCamera.Flying);
        Chip("TO SHIP", () => _free.Take(_chase));

        Row();
        Chip("SLOW", () => _free.Speed = 8.0f);
        Chip("CRUISE", () => _free.Speed = 80.0f);
        Chip("FAST", () => _free.Speed = 2_000.0f);

        Section("VESSEL");

        Row();
        Chip("REFUEL", () => Flight.Fill(_flight.Vessel));
        Chip("INF FUEL", () => _flight.InfiniteFuel = !_flight.InfiniteFuel, () => _flight.InfiniteFuel);
        Chip("INVULN", () => _flight.Invulnerable = !_flight.Invulnerable, () => _flight.Invulnerable);

        Row();
        Chip("HALT", _flight.Halt);
        Chip("KILL ROT", KillRotation);
        Chip("STAGE", () => _flight.Separate());
        Chip("RESTART", _flight.Restart);

        Section("PLACE");

        Row();
        Chip("PAD", ToPad);
        Chip("DROP 5 km", () => _flight.Place(5_000.0, 0.0));
        Chip("ENTRY", () => Orbit(90_000.0, 0.99));

        Row();
        Chip("ORBIT 150 km", () => Orbit(150_000.0, 1.0));
        Chip("ORBIT 500 km", () => Orbit(500_000.0, 1.0));

        Section("TIME");

        Row();
        Chip("PAUSE", () => _flight.DebugPaused = !_flight.DebugPaused, () => _flight.DebugPaused);
        Chip("STEP", _flight.StepFrame);

        Row();

        foreach (double scale in Timescales) {

            Chip($"{scale:0.##}x", () => Engine.TimeScale = scale, () => Math.Abs(Engine.TimeScale - scale) < 1.0e-6);

        }

        Section("WORLD");

        Row();
        Chip("WIRE", () => Draw(Viewport.DebugDrawEnum.Wireframe), () => Drawing(Viewport.DebugDrawEnum.Wireframe));
        Chip("FLAT", () => Draw(Viewport.DebugDrawEnum.Unshaded), () => Drawing(Viewport.DebugDrawEnum.Unshaded));
        Chip("OVERDRAW", () => Draw(Viewport.DebugDrawEnum.Overdraw), () => Drawing(Viewport.DebugDrawEnum.Overdraw));

        Row();
        Chip("HUD", () => _hud.Hidden = !_hud.Hidden, () => !_hud.Hidden);
        Chip("MAP", () => MapView.Active?.Toggle(), () => MapView.Active != null && MapView.Active.Open);

        Section("READOUT");

        Read("FPS", () => $"{Engine.GetFramesPerSecond()}");
        Read("GPU", () => $"{RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()):F1} ms");
        Read("FLIGHT", () => $"{_main.FlightMilliseconds:F2} ms");
        Read("GROUND", () => $"{Planet.Active?.GroundMilliseconds ?? 0.0:F2} ms");
        Read("PATCHES", () => $"{Planet.Active?.PatchCount ?? 0} / L{Planet.Active?.DeepestLevel ?? 0}");
        Read("TREES", () => $"{Planet.Active?.TreeCount ?? 0:N0}");

        Read("ALTITUDE", () => Hud.Distance(_flight.Altitude));
        Read("OVER GROUND", () => Hud.Distance(_flight.Body.HeightAboveGround(_flight.Vessel.Position, _flight.Time)));
        Read("SPEED", () => Hud.Speed(_flight.Vessel.Velocity.Length));
        Read("MACH", () => _flight.Vessel.Aero.InAir ? $"{_flight.Vessel.Aero.Mach:F2}" : "—");
        Read("DYNAMIC Q", () => $"{_flight.Vessel.Aero.DynamicPressure / 1000.0:F1} kPa");
        Read("SKIN", () => $"{_flight.Vessel.SkinTemperature:N0} / {_flight.Vessel.SkinLimit:N0} K");

        Read("MASS", () => $"{_flight.Vessel.Mass / 1000.0:N1} t");
        Read("STAGE dV", () => Hud.Speed(_flight.Vessel.DeltaV));
        Read("WARP", () => $"{_flight.Warp:N0}x at {Engine.TimeScale:0.##}x");
        Read("CAMERA", () => FreeCamera.Flying ? $"free {_free.Speed:N0} m/s" : $"chase {_chase.Distance:N0} m");

        _readout = new Control { CustomMinimumSize = new Vector2(0.0f, _rows.Count * RowHeight) };

        _readout.Draw += DrawReadout;

        _column.AddChild(_readout);

    }

    public void Sync() {

        if (!_frame.Visible) {

            return;

        }

        Vector2 screen = GetViewport().GetVisibleRect().Size;

        _frame.Position = new Vector2(screen.X - Margin - Width, Margin);

        // The console is taller than a small window, so it is cut to whatever room there is and
        // scrolls the rest rather than running off the bottom edge where nothing can reach it.
        _scroll.CustomMinimumSize = new Vector2(0.0f, Mathf.Min(_column.GetCombinedMinimumSize().Y, screen.Y - Margin * 2.0f - Inset * 2.0f));

        foreach ((Button chip, Func<bool> lit) in _lamps) {

            HudTheme.Light(chip, lit());

        }

        _readout.QueueRedraw();

    }

    public override void _UnhandledKeyInput(InputEvent @event) {

        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.F1) {

            return;

        }

        _frame.Visible = !_frame.Visible;

        GetViewport().SetInputAsHandled();

    }

    private void ToggleFreeCamera() {

        if (FreeCamera.Flying) {

            _free.Release();

            return;

        }

        // The map is a mode with its own camera and its own furniture; both at once reads as a
        // broken view rather than as two views.
        if (MapView.Active != null && MapView.Active.Open) {

            MapView.Active.Toggle();

        }

        _free.Take(_chase);

    }

    private void KillRotation() {

        _flight.Vessel.AngularVelocity = Vector3d.Zero;
        _flight.Vessel.ControlTorque = Vector3d.Zero;

    }

    private void ToPad() {

        _flight.PlaceAt(_flight.Site.Latitude * 180.0 / Math.PI, _flight.Site.Longitude * 180.0 / Math.PI, 400.0, 0.0);

    }

    // A share of the circular speed at that height: one leaves a circular orbit, and a little under
    // one puts the periapsis down in the air, which is an entry.
    private void Orbit(double altitude, double share) {

        _flight.Place(altitude, _flight.Body.CircularVelocityAt(altitude) * share, true);

    }

    private bool Drawing(Viewport.DebugDrawEnum mode) => GetViewport().DebugDraw == mode;

    private void Draw(Viewport.DebugDrawEnum mode) {

        GetViewport().DebugDraw = Drawing(mode) ? Viewport.DebugDrawEnum.Disabled : mode;

    }

    private void DrawReadout() {

        for (int index = 0; index < _rows.Count; index++) {

            Rect2 box = new Rect2(0.0f, index * RowHeight, _readout.Size.X, RowHeight);

            HudTheme.WriteIn(_readout, HudTheme.Label, HudTheme.Small, box, _rows[index].Label, HudTheme.Faint, HorizontalAlignment.Left);
            HudTheme.WriteIn(_readout, HudTheme.Numeral, HudTheme.Small, box, _rows[index].Value(), HudTheme.Dim, HorizontalAlignment.Right);

        }

    }

    /// <summary>The scroll bar cut down to a hairline in the interface's own greys; Godot's own is
    /// a wide blue slab that belongs to no other panel here.</summary>
    private static void Bar(VScrollBar bar) {

        bar.CustomMinimumSize = new Vector2(4.0f, 0.0f);

        bar.AddThemeStyleboxOverride("scroll", HudTheme.Track());

        foreach (string state in new[] { "grabber", "grabber_highlight", "grabber_pressed" }) {

            bar.AddThemeStyleboxOverride(state, new StyleBoxFlat { BgColor = state == "grabber" ? HudTheme.Faint : HudTheme.Dim });

        }

    }

    private void Section(string heading) {

        Control rule = new Control { CustomMinimumSize = new Vector2(0.0f, 1.0f) };

        rule.Draw += () => rule.DrawRect(new Rect2(Vector2.Zero, rule.Size), HudTheme.Edge);

        _column.AddChild(rule);

        Write(heading, HudTheme.Strong, HudTheme.Tiny, HudTheme.Faint);

    }

    private void Read(string label, Func<string> value) {

        _rows.Add((label, value));

    }

    private void Row() {

        _row = new HFlowContainer();

        _row.AddThemeConstantOverride("h_separation", 4);
        _row.AddThemeConstantOverride("v_separation", 4);

        _column.AddChild(_row);

    }

    private void Chip(string text, Action pressed, Func<bool> lit = null) {

        Button chip = HudTheme.Button(text, new Vector2(0.0f, ChipHeight));

        chip.Pressed += pressed;

        _row.AddChild(chip);

        if (lit != null) {

            _lamps.Add((chip, lit));

        }

    }

    private void Write(string text, Font font, int size, Color colour) {

        Label label = new Label { Text = text };

        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);

        _column.AddChild(label);

    }

}
