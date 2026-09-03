using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>Fuel over oxidiser. Each bar is as tall as its share of the load, colour is the only
/// label, and either one opens on what it is carrying.</summary>
public sealed partial class PropellantGauge : Control {

    private const int BarWidth = 22;
    private const int Split = 8;

    // Below this the bar turns; a tenth of a tank is the point where the burn plan stops being free.
    private const double Reserve = 0.10;

    public static readonly Vector2 Extent = new Vector2(BarWidth, Navball.Extent.Y);

    private Vessel _vessel;
    private Popover _popover;

    private Rect2 _fuel;
    private Rect2 _oxidiser;

    private Propellant _hovered;

    public void Build(Vessel vessel, Popover popover) {

        _vessel = vessel;
        _popover = popover;

        CustomMinimumSize = Extent;
        Size = Extent;

        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;

        float usable = Extent.Y - Split;

        // The two bars are drawn to scale against each other, so the mixture ratio is visible in the
        // gauge itself rather than being something the pilot has to remember.
        float fuel = vessel.PropellantCapacity > 0.0 ? (float)(usable * vessel.FuelCapacity / vessel.PropellantCapacity) : usable * 0.5f;

        _fuel = new Rect2(0.0f, 0.0f, BarWidth, fuel);
        _oxidiser = new Rect2(0.0f, fuel + Split, BarWidth, usable - fuel);

    }

    public void Sync() {

        QueueRedraw();

    }

    public override void _GuiInput(InputEvent @event) {

        if (@event is InputEventMouseMotion motion) {

            Propellant hovered = At(motion.Position);

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

        if (what == NotificationMouseExit && _hovered != null) {

            _hovered = null;

            QueueRedraw();

        }

    }

    private Propellant At(Vector2 point) {

        return _fuel.HasPoint(point) ? _vessel.Fuel : _oxidiser.HasPoint(point) ? _vessel.Oxidiser : null;

    }

    private void Select(Propellant propellant) {

        if (propellant == null || _popover.Shows(propellant)) {

            _popover.Dismiss();

            return;

        }

        bool oxidiser = propellant == _vessel.Oxidiser;

        Rect2 box = oxidiser ? _oxidiser : _fuel;

        _popover.Raise(propellant, propellant.Name, (rows, actions) => Read(oxidiser, rows, actions), GlobalPosition + new Vector2(Extent.X + 8.0f, box.GetCenter().Y));

    }

    public override void _Draw() {

        if (_vessel == null || _vessel.PropellantCapacity <= 0.0) {

            return;

        }

        Bar(_fuel, _vessel.FuelMass / _vessel.FuelCapacity, HudTheme.Fuel, _hovered == _vessel.Fuel || _popover.Shows(_vessel.Fuel));
        Bar(_oxidiser, _vessel.OxidiserMass / _vessel.OxidiserCapacity, HudTheme.Oxidiser, _hovered == _vessel.Oxidiser || _popover.Shows(_vessel.Oxidiser));

    }

    private void Bar(Rect2 box, double fill, Color ink, bool picked) {

        DrawRect(box, HudTheme.Backing);

        float fraction = (float)Math.Clamp(fill, 0.0, 1.0);

        Color colour = fill < Reserve ? HudTheme.Caution : ink;

        float top = box.End.Y - box.Size.Y * fraction;

        DrawRect(new Rect2(box.Position.X, top, box.Size.X, box.End.Y - top), colour * new Color(1.0f, 1.0f, 1.0f, picked ? 0.82f : 0.62f));

        if (fraction > 0.0f) {

            DrawLine(new Vector2(box.Position.X, top), new Vector2(box.End.X, top), colour, 2.0f);

        }

        for (int step = 1; step < 4; step++) {

            float y = Mathf.Round(box.End.Y - box.Size.Y * step / 4.0f) + 0.5f;

            DrawLine(new Vector2(box.Position.X, y), new Vector2(box.Position.X + box.Size.X * 0.35f, y), HudTheme.Backing * new Color(1.0f, 1.0f, 1.0f, 0.9f), 1.0f);

        }

        DrawRect(box, picked ? HudTheme.Ink : HudTheme.Edge, false, 1.0f);

    }

    /// <summary>What one species is doing. Every figure comes off the load and the flow the engines
    /// are actually drawing, split by the ratio the tank is mixed at.</summary>
    private void Read(bool oxidiser, List<(string Label, string Value)> rows, List<(string Label, Action Run)> actions) {

        Vessel vessel = _vessel;

        Propellant propellant = oxidiser ? vessel.Oxidiser : vessel.Fuel;

        double mass = oxidiser ? vessel.OxidiserMass : vessel.FuelMass;
        double capacity = oxidiser ? vessel.OxidiserCapacity : vessel.FuelCapacity;
        double volume = oxidiser ? vessel.OxidiserVolume : vessel.FuelVolume;

        // Each species leaves the tank at its share of the mixture, so the draw splits the same way.
        double share = oxidiser ? vessel.MixtureRatio / (1.0 + vessel.MixtureRatio) : 1.0 / (1.0 + vessel.MixtureRatio);

        rows.Add(("MASS", $"{mass:N0} / {capacity:N0} kg"));
        rows.Add(("VOLUME", $"{volume:F2} m³"));
        rows.Add(("DENSITY", $"{propellant.Density:N0} kg/m³"));
        rows.Add(("TEMP", $"{propellant.Temperature:F0} K{(propellant.IsCryogenic ? " cryo" : string.Empty)}"));
        rows.Add(("DRAW", $"{vessel.CurrentMassFlow * share:F2} kg/s"));
        rows.Add(("ENDURANCE", Hud.Clock(Endurance(vessel))));
        rows.Add(("DELTA-V", $"{vessel.DeltaV:N0} m/s"));

    }

    /// <summary>Seconds of burn left at the rating the lit engines are running. Both species run out
    /// together, so this is the same number on either bar.</summary>
    private static double Endurance(Vessel vessel) {

        double flow = vessel.MassFlowRate * vessel.ThrustFraction;

        return flow > 0.0 ? vessel.PropellantMass / flow : double.PositiveInfinity;

    }

}
