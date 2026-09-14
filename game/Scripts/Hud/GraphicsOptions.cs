using Godot;

namespace FullThrust.Game;

public sealed partial class GraphicsOptions : CanvasLayer {

    private const string SettingsPath = "user://graphics.cfg";
    private const int SettingsVersion = 2;
    private static readonly float[] Scales = { 1.0f, 0.75f, 0.5f };

    private Control _scrim;
    private PanelContainer _panel;
    private OptionButton _resolution;
    private CheckButton _fullscreen;
    private int _mode = 1;

    public const bool LaunchShake = true;
    public const float CloudSteps = 96.0f;

    public override void _Ready() {

        Layer = 30;
        using ConfigFile config = new();

        if (config.Load(SettingsPath) == Error.Ok) {

            int stored = config.GetValue("graphics", "resolution", 1).AsInt32();
            int version = config.GetValue("graphics", "version", 0).AsInt32();
            _mode = version >= SettingsVersion
                ? Mathf.Clamp(stored, 0, Scales.Length - 1)
                : stored <= 1 ? 0 : stored >= 4 ? 2 : 1;

        }

        Apply();

        _scrim = new ColorRect {

            Color = new Color(0.005f, 0.01f, 0.02f, 0.78f),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,

        };
        AddChild(_scrim);
        _panel = new PanelContainer { CustomMinimumSize = new Vector2(280, 0) };
        _panel.AddThemeStyleboxOverride("panel", HudTheme.Panel(26));
        _scrim.AddChild(_panel);
        VBoxContainer column = new();
        column.AddThemeConstantOverride("separation", 14);
        _panel.AddChild(column);

        _resolution = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        _resolution.AddItem("Native");
        _resolution.AddItem("Quality");
        _resolution.AddItem("Performance");
        _resolution.Select(_mode);
        _resolution.ItemSelected += index => {

            _mode = (int)index;
            Apply();
            Save();

        };
        column.AddChild(_resolution);

        _fullscreen = new CheckButton { Text = "Fullscreen", FocusMode = Control.FocusModeEnum.None };
        _fullscreen.Toggled += enabled => DisplayServer.WindowSetMode(enabled ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        column.AddChild(_fullscreen);

    }

    private void Apply() {

        Viewport viewport = GetViewport();
        viewport.Msaa3D = Viewport.Msaa.Disabled;
        viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
        viewport.UseTaa = true;
        viewport.Scaling3DMode = _mode == 0 ? Viewport.Scaling3DModeEnum.Bilinear : Viewport.Scaling3DModeEnum.Fsr;
        viewport.Scaling3DScale = Scales[_mode];
        viewport.FsrSharpness = 0.2f;

    }

    private void Save() {

        using ConfigFile config = new();
        config.SetValue("graphics", "version", SettingsVersion);
        config.SetValue("graphics", "resolution", _mode);
        Error error = config.Save(SettingsPath);
        if (error != Error.Ok) { GD.PushWarning($"Could not save graphics settings: {error}"); }

    }

    private void Toggle() {

        _scrim.Visible = !_scrim.Visible;
        _fullscreen.SetPressedNoSignal(DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen);
        GetViewport().SetInputAsHandled();

    }

    public override void _Input(InputEvent @event) {

        if (@event is InputEventKey key && key.Pressed && !key.Echo && (key.Keycode == Key.F2 || (_scrim.Visible && key.Keycode == Key.Escape))) {

            Toggle();

        }

        if (_scrim.Visible && @event is InputEventKey) { GetViewport().SetInputAsHandled(); }

    }

    public override void _Process(double delta) {

        if (!_scrim.Visible) { return; }

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        _scrim.Size = viewport;
        _panel.Position = (viewport - _panel.Size) * 0.5f;

    }

}
