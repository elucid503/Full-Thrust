using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The engines, laid out the way they are actually mounted. Shading is what is burning,
/// and each one can be shut on its own.</summary>
public sealed partial class EnginePanel : Control {

    private const float Bell = 16.0f;
    private const float Margin = 11.0f;
    private const float Clearance = 1.16f;

    private readonly List<Vector2> _mounts = new List<Vector2>();

    private Vessel _vessel;

    private int _hovered = -1;
    private int _selected = -1;
    private PopupPanel _popup;
    private Label _stats;
    private Label _limitLabel;
    private HSlider _limit;
    private Button _enabled;


    /// <summary>Builds the cluster from the switches fitted, so a restaged vessel redraws itself.</summary>
    public void Build(Vessel vessel) {

        _popup?.Hide();
        _selected = -1;
        _vessel = vessel;
        _hovered = -1;

        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;

        Arrange(vessel.EngineCount);

        float reach = 0.0f;

        foreach (Vector2 mount in _mounts) {

            reach = Math.Max(reach, mount.Length());

        }

        float extent = (reach + Bell) * 2.0f + Margin * 2.0f;

        CustomMinimumSize = new Vector2(extent, extent);
        Size = new Vector2(extent, extent);

    }

    // Match VesselView: a single axial engine, otherwise equally spaced engines on one ring.
    private void Arrange(int count) {

        _mounts.Clear();

        if (count <= 0) {

            return;

        }

        if (count == 1) {

            _mounts.Add(Vector2.Zero);

            return;

        }

        int ring = count;

        float radius = Math.Max(Bell / Mathf.Sin(Mathf.Pi / ring), Bell * 2.0f) * Clearance;

        for (int index = 0; index < ring; index++) {

            float angle = Mathf.Tau * index / ring - Mathf.Pi * 0.5f;

            _mounts.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);

        }

    }

    public void Sync() {

        // A stage with no engines under it has no cluster to draw, and an empty box is not a
        // reading. The panel is up only while there is something on it.
        Visible = _vessel.EngineCount > 0;

        if (Visible) {

            QueueRedraw();
            if (_popup != null && _popup.Visible) { UpdateDetails(); }

        }

    }

    public override void _GuiInput(InputEvent @event) {

        if (@event is InputEventMouseMotion motion) {

            int hovered = At(motion.Position);

            if (hovered != _hovered) {

                _hovered = hovered;

                QueueRedraw();

            }

            return;

        }

        if (@event is not InputEventMouseButton button || !button.Pressed || (button.ButtonIndex != MouseButton.Left && button.ButtonIndex != MouseButton.Right)) {

            return;

        }

        int engine = At(button.Position);

        if (engine >= 0) {

            OpenDetails(engine);

        }

        AcceptEvent();

    }

    private Label TextLabel(string text) {

        Label label = new Label { Text = text };
        label.AddThemeFontOverride("font", HudTheme.Label);
        label.AddThemeFontSizeOverride("font_size", HudTheme.Body);
        label.AddThemeColorOverride("font_color", HudTheme.Ink);
        return label;

    }

    private void OpenDetails(int engine) {

        if (_popup == null) {

            _popup = new PopupPanel { Name = "EngineDetails", Size = new Vector2I(300, 280) };
            _popup.AddThemeStyleboxOverride("panel", HudTheme.Panel(14.0f));
            AddChild(_popup);
            VBoxContainer content = new VBoxContainer();
            content.AddThemeConstantOverride("separation", 12);
            _popup.AddChild(content);
            _stats = TextLabel("");
            content.AddChild(_stats);
            _limitLabel = TextLabel("");
            content.AddChild(_limitLabel);
            _limit = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, CustomMinimumSize = new Vector2(270, 28) };
            _limit.TooltipText = "Fraction of the mechanical gimbal range. Zero locks this engine straight.";
            _limit.ValueChanged += value => {

                if (_selected >= 0) { _vessel.Active.SetGimbalLimit(_selected, value / 100.0); }
                UpdateDetails();

            };
            content.AddChild(_limit);
            content.AddChild(TextLabel("0% locks the mount · 100% full range"));
            _enabled = HudTheme.Button("", new Vector2(270, 34));
            _enabled.Pressed += () => {

                if (_selected >= 0) { _vessel.SetEngine(_selected, !_vessel.IsEngineLit(_selected)); }
                UpdateDetails();

            };
            content.AddChild(_enabled);
            Button close = HudTheme.Button("Close", new Vector2(270, 30));
            close.Pressed += () => _popup.Hide();
            content.AddChild(close);

        }
        _selected = engine;
        _limit.SetValueNoSignal(_vessel.Active.GimbalLimit(engine) * 100.0);
        UpdateDetails();
        Vector2 screen = GetViewportRect().Size;
        Vector2 at = GlobalPosition - new Vector2(320, 170);
        at = at.Clamp(Vector2.One * 8, (screen - new Vector2(320, 330)).Max(Vector2.One * 8));
        _popup.Popup(new Rect2I((Vector2I)at, new Vector2I(310, 310)));

    }

    private void UpdateDetails() {

        if (_selected < 0 || _stats == null || _selected >= _vessel.EngineCount) { return; }
        Stage stage = _vessel.Active;
        bool enabled = stage.IsEngineLit(_selected);
        double thrust = enabled && stage.PropellantMass > 0.0
            ? stage.ThrustNewtons / stage.EngineCount * Math.Clamp(_vessel.Throttle, 0.0, 1.0) : 0.0;
        double flow = stage.SpecificImpulse > 0.0 ? thrust / (stage.SpecificImpulse * Vessel.StandardGravity) : 0.0;
        Vector3 direction = VesselView.Active?.EngineDirection(_selected) ?? Vector3.Down;
        float angle = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(-direction.Y, -1.0f, 1.0f)));
        _stats.Text = $"ENGINE {_selected + 1}  ·  {(enabled ? (thrust > 0 ? "FIRING" : "READY") : "DISABLED")}\nThrust  {thrust / 1000.0:F1} kN\nPropellant  {flow:F1} kg/s\nSpecific impulse  {stage.SpecificImpulse:F0} s\nGimbal deflection  {angle:F1}°";
        _limitLabel.Text = $"Gimbal limit  {stage.GimbalLimit(_selected) * 100:F0}%  /  {stage.GimbalRange * stage.GimbalLimit(_selected) * 180.0 / Math.PI:F1}°";
        _enabled.Text = enabled ? "Disable engine" : "Enable engine";

    }

    public override void _Notification(int what) {

        if (what == NotificationMouseExit && _hovered >= 0) {

            _hovered = -1;

            QueueRedraw();

        }

    }

    private int At(Vector2 point) {

        for (int index = 0; index < _mounts.Count; index++) {

            if (point.DistanceTo(Size * 0.5f + _mounts[index]) <= Bell) {

                return index;

            }

        }

        return -1;

    }

    public override void _Draw() {

        if (_vessel == null || _mounts.Count == 0) {

            return;

        }

        DrawStyleBox(HudTheme.Panel(0.0f), new Rect2(Vector2.Zero, Size));

        float throttle = (float)Math.Clamp(_vessel.Throttle, 0.0, 1.0);

        bool dry = _vessel.PropellantMass <= 0.0;
        bool burning = _vessel.CurrentThrust > 0.0;

        Vector2 centre = Size * 0.5f;

        for (int index = 0; index < _mounts.Count; index++) {

            Vector3 direction = VesselView.Active?.EngineDirection(index) ?? Vector3.Down;
            Vector2 movement = new Vector2(direction.Z, -direction.X) * 22.0f;
            Vector2 mount = centre + _mounts[index];
            Vector2 at = mount + movement;
            DrawCircle(mount, 1.4f, HudTheme.Faint);


            bool lit = _vessel.IsEngineLit(index);

            // Shut is the quiet state, armed is legible, burning is the loud one. A dry stage warns
            // whatever its switches say, because the switches are no longer what is stopping it.
            Color ink = index == _hovered ? HudTheme.Ink
                : !lit ? HudTheme.Faint
                : dry ? HudTheme.Caution
                : burning ? HudTheme.Ink
                : HudTheme.Dim;

            if (lit && burning) {

                DrawCircle(at, Bell - 2.0f, HudTheme.Ink * new Color(1.0f, 1.0f, 1.0f, 0.12f + 0.42f * throttle));

            }

            DrawArc(at, Bell - 2.0f, 0.0f, Mathf.Tau, 40, ink, lit && burning ? 2.0f : 1.3f, true);

            // A shut engine is struck through, so its state reads without a legend.
            if (!lit) {

                float reach = (Bell - 2.0f) * 0.62f;

                DrawLine(at - new Vector2(reach, 0.0f), at + new Vector2(reach, 0.0f), ink, 1.3f, true);

            }

        }

    }

}
