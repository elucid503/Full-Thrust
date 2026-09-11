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
    private readonly List<object> _subjects = new List<object>();

    private Vessel _vessel;
    private Popover _popover;

    private int _hovered = -1;

    /// <summary>Builds the cluster from the switches fitted, so a restaged vessel redraws itself.</summary>
    public void Build(Vessel vessel, Popover popover) {

        _vessel = vessel;
        _popover = popover;
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
        _subjects.Clear();

        if (count <= 0) {

            return;

        }

        if (count == 1) {

            _mounts.Add(Vector2.Zero);
            _subjects.Add(new object());

            return;

        }

        int ring = count;

        float radius = Math.Max(Bell / Mathf.Sin(Mathf.Pi / ring), Bell * 2.0f) * Clearance;

        for (int index = 0; index < ring; index++) {

            float angle = Mathf.Tau * index / ring - Mathf.Pi * 0.5f;

            _mounts.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            _subjects.Add(new object());

        }

    }

    public void Sync() {

        // A stage with no engines under it has no cluster to draw, and an empty box is not a
        // reading. The panel is up only while there is something on it.
        Visible = _vessel.EngineCount > 0;

        if (Visible) {

            QueueRedraw();

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

        if (@event is not InputEventMouseButton button || !button.Pressed || button.ButtonIndex != MouseButton.Left) {

            return;

        }

        Select(At(button.Position));

        AcceptEvent();

    }

    public override void _Notification(int what) {

        if (what == NotificationMouseExit && _hovered >= 0) {

            _hovered = -1;

            QueueRedraw();

        }

    }

    private void Select(int engine) {

        if (engine < 0 || engine >= _subjects.Count || _popover.Shows(_subjects[engine])) {

            _popover.Dismiss();

            return;

        }

        Vector2 mount = Size * 0.5f + _mounts[engine];

        _popover.Raise(_subjects[engine], $"ENGINE {engine + 1}", (rows, actions) => Read(engine, rows, actions), GlobalPosition + mount);

    }

    /// <summary>What one bell is doing. Only figures this mount carries, and only the two actions
    /// it can take on its own: shut or arm, lock or free the gimbal.</summary>
    private void Read(int engine, List<(string Label, string Value)> rows, List<(string Label, Action Run)> actions) {

        if (engine < 0 || engine >= _vessel.EngineCount) {

            return;

        }

        Stage stage = _vessel.Active;
        bool lit = stage.IsEngineLit(engine);

        double rating = stage.EngineCount > 0 ? stage.ThrustNewtons / stage.EngineCount : 0.0;
        double thrust = lit && stage.PropellantMass > 0.0 ? rating * Math.Clamp(_vessel.Throttle, 0.0, 1.0) : 0.0;
        double flow = stage.SpecificImpulse > 0.0 ? thrust / (stage.SpecificImpulse * Vessel.StandardGravity) : 0.0;

        Vector3 direction = VesselView.Active?.EngineDirection(engine) ?? Vector3.Down;
        float deflection = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(-direction.Y, -1.0f, 1.0f)));
        double limit = stage.GimbalRange * stage.GimbalLimit(engine) * 180.0 / Math.PI;

        string status = !lit ? "SHUT" : thrust > 0.0 ? "FIRING" : "READY";

        rows.Add(("STATUS", status));
        rows.Add(("THRUST", $"{thrust / 1000.0:F1} / {rating / 1000.0:F1} kN"));
        rows.Add(("FLOW", $"{flow:F2} kg/s"));
        rows.Add(("IMPULSE", $"{stage.SpecificImpulse:F0} s"));
        rows.Add(("GIMBAL", limit <= 0.0 ? "LOCKED" : $"{deflection:F1}° / {limit:F1}°"));

        actions.Add((lit ? "SHUT" : "ARM", () => _vessel.SetEngine(engine, !lit)));

        if (stage.GimbalRange > 0.0) {

            bool locked = stage.GimbalLimit(engine) <= 0.0;

            actions.Add((locked ? "FREE" : "LOCK", () => stage.SetGimbalLimit(engine, locked ? 1.0 : 0.0)));

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
            bool picked = index == _hovered || (index < _subjects.Count && _popover.Shows(_subjects[index]));

            // Shut is the quiet state, armed is legible, burning is the loud one. A dry stage warns
            // whatever its switches say, because the switches are no longer what is stopping it.
            Color ink = picked ? HudTheme.Ink
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
