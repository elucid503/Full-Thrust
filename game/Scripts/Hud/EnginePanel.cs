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

    /// <summary>Builds the cluster from the switches fitted, so a restaged vessel redraws itself.</summary>
    public void Build(Vessel vessel) {

        _vessel = vessel;

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

    // One on the axis, a ring of anything up to four, and beyond that a ring around a centre engine.
    // Every real cluster this project will fly is one of those three.
    private void Arrange(int count) {

        _mounts.Clear();

        if (count <= 0) {

            return;

        }

        if (count == 1) {

            _mounts.Add(Vector2.Zero);

            return;

        }

        int ring = count <= 4 ? count : count - 1;

        float radius = Math.Max(Bell / Mathf.Sin(Mathf.Pi / ring), Bell * 2.0f) * Clearance;

        if (count > 4) {

            _mounts.Add(Vector2.Zero);

        }

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

        int engine = At(button.Position);

        if (engine >= 0) {

            _vessel.SetEngine(engine, !_vessel.IsEngineLit(engine));

        }

        AcceptEvent();

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

            Vector2 at = centre + _mounts[index];

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
