using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The development console: a camera cut loose from the vehicle, cheats for the vehicle,
/// and the figures the flight interface has no business showing. F1 raises it.</summary>
public sealed partial class DebugPanel : CanvasLayer {

    private const float Width = 260.0f;
    private const float Margin = 22.0f;
    private const float Inset = 11.0f;
    private const float ChipHeight = 24.0f;
    private const float RowHeight = 16.0f;
    private const int Raised = 10;

    private static readonly float[] CameraSpeeds = { 8.0f, 80.0f, 2_000.0f };
    private static readonly double[] Timescales = { 1.0, 4.0, 10.0 };

    private Main _main;
    private Flight _flight;
    private FreeCamera _free;
    private OrbitCamera _chase;
    private Hud _hud;

    private PanelContainer _frame;
    private VBoxContainer _column;
    private HBoxContainer _row;
    private Control _readout;
    private OptionButton _cameraSpeed;
    private OptionButton _clock;

    private readonly List<(Button Chip, Func<bool> Lit)> _lamps = new List<(Button, Func<bool>)>();
    private readonly List<(string Label, Func<string> Value)> _rows = new List<(string, Func<string>)>();

    public void Build(Main main, Flight flight, FreeCamera free, OrbitCamera chase, Hud hud) {

        _main = main;
        _flight = flight;
        _free = free;
        _chase = chase;
        _hud = hud;

        Layer = Raised;
        RenderingServer.SetDebugGenerateWireframes(true);

        _frame = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(Width, 0.0f) };
        _frame.AddThemeStyleboxOverride("panel", HudTheme.Panel(Inset));
        AddChild(_frame);

        _column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _column.AddThemeConstantOverride("separation", 8);
        _frame.AddChild(_column);

        Write("DEBUG", HudTheme.Strong, HudTheme.Head, HudTheme.Ink);

        Section("CAMERA");
        Row();
        Chip("FREE CAM", ToggleFreeCamera, () => FreeCamera.Flying);
        Chip("SHIP", () => { if (FreeCamera.Flying) { _free.Release(); } });
        _cameraSpeed = Menu("Walk", "Fly", "Fast");
        _cameraSpeed.Select(1);
        _cameraSpeed.ItemSelected += index => _free.Speed = CameraSpeeds[(int)index];

        Section("VESSEL");
        Row();
        Chip("INF FUEL", () => _flight.InfiniteFuel = !_flight.InfiniteFuel, () => _flight.InfiniteFuel);
        Chip("INVULN", () => _flight.Invulnerable = !_flight.Invulnerable, () => _flight.Invulnerable);
        Row();
        Chip("REFUEL", () => Flight.Fill(_flight.Vessel));
        Chip("HALT", _flight.Halt);
        Chip("RESTART", _flight.Restart);

        Section("PLACE");
        Row();
        Chip("PAD", ToPad);
        Chip("ENTRY", () => Orbit(90_000.0, 0.99));
        Chip("ORBIT", () => Orbit(150_000.0, 1.0));

        Section("TIME");
        Row();
        Chip("PAUSE", () => _flight.DebugPaused = !_flight.DebugPaused, () => _flight.DebugPaused);
        _clock = Menu("1×", "4×", "10×");
        _clock.Select(0);
        _clock.ItemSelected += index => Engine.TimeScale = Timescales[(int)index];

        Section("DISPLAY");
        Row();
        Chip("HUD", () => _hud.Hidden = !_hud.Hidden, () => !_hud.Hidden);
        Chip("WIRE", () => Draw(Viewport.DebugDrawEnum.Wireframe), () => Drawing(Viewport.DebugDrawEnum.Wireframe));

        Read("FPS", () => $"{Engine.GetFramesPerSecond():0}");
        Read("GPU", () => $"{RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()):F1} ms");

        _readout = new Control { CustomMinimumSize = new Vector2(0.0f, _rows.Count * RowHeight + 4.0f) };
        _readout.Draw += DrawReadout;
        _column.AddChild(_readout);

    }

    public void Sync() {

        if (!_frame.Visible) {

            return;

        }

        Vector2 screen = GetViewport().GetVisibleRect().Size;
        _frame.Position = new Vector2(screen.X - Margin - Width, Margin);

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

        if (MapView.Active != null && MapView.Active.Open) {

            MapView.Active.Toggle();

        }

        _free.Take(_chase);

    }

    private void ToPad() {

        _flight.PlaceAt(_flight.Site.Latitude * 180.0 / Math.PI, _flight.Site.Longitude * 180.0 / Math.PI, 400.0, 0.0);

    }

    private void Orbit(double altitude, double share) {

        _flight.Place(altitude, _flight.Body.CircularVelocityAt(altitude) * share, true);

    }

    private bool Drawing(Viewport.DebugDrawEnum mode) => GetViewport().DebugDraw == mode;

    private void Draw(Viewport.DebugDrawEnum mode) {

        GetViewport().DebugDraw = Drawing(mode) ? Viewport.DebugDrawEnum.Disabled : mode;

    }

    private void DrawReadout() {

        for (int index = 0; index < _rows.Count; index++) {

            Rect2 box = new Rect2(0.0f, index * RowHeight + 4.0f, _readout.Size.X, RowHeight);
            HudTheme.WriteIn(_readout, HudTheme.Label, HudTheme.Small, box, _rows[index].Label, HudTheme.Faint, HorizontalAlignment.Left);
            HudTheme.WriteIn(_readout, HudTheme.Numeral, HudTheme.Small, box, _rows[index].Value(), HudTheme.Dim, HorizontalAlignment.Right);

        }

    }

    private void Section(string heading) {

        Control rule = new Control { CustomMinimumSize = new Vector2(0.0f, 8.0f) };
        rule.Draw += () => rule.DrawRect(new Rect2(0.0f, 7.0f, rule.Size.X, 1.0f), HudTheme.Edge);
        _column.AddChild(rule);
        Write(heading, HudTheme.Strong, HudTheme.Tiny, HudTheme.Faint);

    }

    private OptionButton Menu(params string[] items) {

        OptionButton menu = new OptionButton {

            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0.0f, ChipHeight),

        };
        menu.AddThemeFontOverride("font", HudTheme.Strong);
        menu.AddThemeFontSizeOverride("font_size", HudTheme.Small);
        HudTheme.Light(menu, false);

        foreach (string item in items) {

            menu.AddItem(item);

        }

        _column.AddChild(menu);
        return menu;

    }

    private void Read(string label, Func<string> value) {

        _rows.Add((label, value));

    }

    private void Row() {

        _row = new HBoxContainer();
        _row.AddThemeConstantOverride("separation", 4);
        _column.AddChild(_row);

    }

    private void Chip(string text, Action pressed, Func<bool> lit = null) {

        Button chip = HudTheme.Button(text, new Vector2(0.0f, ChipHeight));
        chip.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
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
