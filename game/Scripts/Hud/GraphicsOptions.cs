using Godot;

namespace FullThrust.Game;

public sealed partial class GraphicsOptions : CanvasLayer {

    private const string SettingsPath = "user://graphics.cfg";
    private static readonly float[] Scales = { 1.0f, 0.85f, 0.75f, 2.0f / 3.0f, 0.5f };
    private Control _scrim;
    private PanelContainer _panel;
    private Button _open;
    private Label _status;
    private OptionButton _resolution;
    private OptionButton _clouds;
    private CheckButton _shake;
    private CheckButton _fullscreen;
    private int _mode = 2;
    private int _cloudMode = 0;
    private int _atmosphereMode = 2;
    private double _statusClock;

    public static bool LaunchShake { get; private set; } = true;
    public static float CloudSteps { get; private set; } = 128.0f;
    public static bool Haze { get; private set; } = true;
    public static bool SunShafts { get; private set; } = true;
    public static bool ContactShading { get; private set; } = true;

    public override void _Ready() {

        Layer = 30;
        using ConfigFile config = new();

        if (config.Load(SettingsPath) == Error.Ok) {

            _mode = Mathf.Clamp(config.GetValue("graphics", "resolution", 2).AsInt32(), 0, Scales.Length - 1);
            LaunchShake = config.GetValue("graphics", "launch_shake", true).AsBool();
            ContactShading = config.GetValue("graphics", "contact_shading", true).AsBool();
            _cloudMode = Mathf.Clamp(config.GetValue("graphics", "cloud_quality", 0).AsInt32(), 0, 2);
            _atmosphereMode = Mathf.Clamp(config.GetValue("graphics", "atmosphere", 2).AsInt32(), 0, 2);

        }

        CloudSteps = 128.0f + _cloudMode * 64.0f;
        Haze = _atmosphereMode > 0;
        SunShafts = _atmosphereMode > 1;
        Apply();
        _open = HudTheme.Button("GRAPHICS  /  F8", new Vector2(148, 30));
        _open.Pressed += Toggle;
        AddChild(_open);

        _scrim = new ColorRect {

            Color = new Color(0.005f, 0.01f, 0.02f, 0.78f),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,

        };
        AddChild(_scrim);
        _panel = new PanelContainer { CustomMinimumSize = new Vector2(440, 0) };
        _panel.AddThemeStyleboxOverride("panel", HudTheme.Panel(26));
        _scrim.AddChild(_panel);
        VBoxContainer column = new();
        column.AddThemeConstantOverride("separation", 18);
        _panel.AddChild(column);
        column.AddChild(Text("DISPLAY & GRAPHICS", 22, HudTheme.Ink));
        column.AddChild(Text("Balance image detail and smooth flight.", 16, HudTheme.Dim));

        _resolution = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        _resolution.AddItem("Native resolution  /  TAA");
        _resolution.AddItem("Ultra quality  /  85% + FSR 2");
        _resolution.AddItem("Quality  /  75% + FSR 2");
        _resolution.AddItem("Balanced  /  67% + FSR 2");
        _resolution.AddItem("Performance  /  50% + FSR 2");
        _resolution.Select(_mode);
        _resolution.ItemSelected += index => {

            _mode = (int)index;
            Apply();
            Save();

        };
        column.AddChild(_resolution);

        _clouds = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        _clouds.AddItem("Cloud detail  /  Standard");
        _clouds.AddItem("Cloud detail  /  High");
        _clouds.AddItem("Cloud detail  /  Ultra");
        _clouds.Select(_cloudMode);
        _clouds.ItemSelected += index => {

            _cloudMode = (int)index;
            CloudSteps = 128.0f + _cloudMode * 64.0f;
            Save();

        };
        column.AddChild(_clouds);

        OptionButton atmosphere = new() { FocusMode = Control.FocusModeEnum.None };
        atmosphere.AddItem("Atmosphere  /  Clear");
        atmosphere.AddItem("Atmosphere  /  Coastal haze");
        atmosphere.AddItem("Atmosphere  /  Haze & sun shafts");
        atmosphere.Select(_atmosphereMode);
        atmosphere.ItemSelected += index => {

            _atmosphereMode = (int)index;
            Haze = _atmosphereMode > 0;
            SunShafts = _atmosphereMode > 1;
            Save();

        };
        column.AddChild(atmosphere);
        CheckButton contacts = new() { Text = "Terrain contact shading", ButtonPressed = ContactShading, FocusMode = Control.FocusModeEnum.None };
        contacts.Toggled += enabled => {

            ContactShading = enabled;
            Apply();
            Save();

        };
        column.AddChild(contacts);

        _shake = new CheckButton { Text = "Subtle launch camera shake", ButtonPressed = LaunchShake, FocusMode = Control.FocusModeEnum.None };
        _shake.Toggled += enabled => {

            LaunchShake = enabled;
            Save();

        };
        column.AddChild(_shake);
        _fullscreen = new CheckButton { Text = "Fullscreen", FocusMode = Control.FocusModeEnum.None };
        _fullscreen.Toggled += enabled => DisplayServer.WindowSetMode(enabled ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        column.AddChild(_fullscreen);
        _status = Text(string.Empty, 15, HudTheme.Dim);
        column.AddChild(_status);
        column.AddChild(Text("The interface always renders at display resolution.", 14, HudTheme.Dim));
        Button close = HudTheme.Button("RETURN TO FLIGHT", new Vector2(0, 38));
        close.Pressed += Toggle;
        column.AddChild(close);

    }

    private static Label Text(string text, int size, Color color) {

        Label label = new() { Text = text };
        label.AddThemeFontOverride("font", HudTheme.Label);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;

    }

    private void Apply() {

        WorldEnvironment world = GetParent().GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        if (world != null) {

            world.Environment.SsaoEnabled = ContactShading;
            if (world.Compositor != null) {

                foreach (CompositorEffect effect in world.Compositor.CompositorEffects) {

                    if (effect is GeometryHistory) { effect.Enabled = _mode != 0; }

                }

            }

        }

        Viewport viewport = GetViewport();
        viewport.Msaa3D = Viewport.Msaa.Disabled;
        viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
        viewport.UseTaa = _mode == 0;
        viewport.Scaling3DMode = _mode == 0 ? Viewport.Scaling3DModeEnum.Bilinear : Viewport.Scaling3DModeEnum.Fsr2;
        viewport.Scaling3DScale = Scales[_mode];
        viewport.FsrSharpness = 1.0f;

    }

    private void Save() {

        using ConfigFile config = new();
        config.SetValue("graphics", "resolution", _mode);
        config.SetValue("graphics", "launch_shake", LaunchShake);
        config.SetValue("graphics", "contact_shading", ContactShading);
        config.SetValue("graphics", "cloud_quality", _cloudMode);
        config.SetValue("graphics", "atmosphere", _atmosphereMode);
        Error error = config.Save(SettingsPath);
        if (error != Error.Ok) { GD.PushWarning($"Could not save graphics settings: {error}"); }

    }

    private void Toggle() {

        _scrim.Visible = !_scrim.Visible;
        _open.Visible = !_scrim.Visible;
        _fullscreen.SetPressedNoSignal(DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen);
        // Consume flight commands while the graphics panel has the user's attention.
        GetViewport().SetInputAsHandled();

    }

    public override void _Input(InputEvent @event) {

        if (@event is InputEventKey key && key.Pressed && !key.Echo && (key.Keycode == Key.F8 || (_scrim.Visible && key.Keycode == Key.Escape))) {

            Toggle();

        }

        if (_scrim.Visible && @event is InputEventKey) { GetViewport().SetInputAsHandled(); }

    }

    public override void _Process(double delta) {

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        _open.Position = new Vector2((viewport.X - _open.Size.X) * 0.5f, viewport.Y - 52);
        _scrim.Size = viewport;

        if (!_scrim.Visible) { return; }

        _panel.Position = (viewport - _panel.Size) * 0.5f;
        _statusClock += delta;

        if (_statusClock >= 0.5) {

            _statusClock = 0.0;
            _status.Text = $"{viewport.X:0} × {viewport.Y:0}    ·    {Engine.GetFramesPerSecond()} FPS";

        }

    }

}
