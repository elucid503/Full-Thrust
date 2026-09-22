using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>Autopilot master, attitude reference and thrusters, over the ball they act on.</summary>
public sealed partial class ModeBar : Control {

    // Tall enough that the text buttons' own line height never sets it and the glyph one falls short.
    private const float Height = 30.0f;
    private const float Gauge = 3.0f;
    private const float Gap = 6.0f;

    private const float Border = 1.0f;

    private const float Narrow = 38.0f;

    // The firing-mode chevron rides on the thrusters' switch, split off by a hairline so it reads as its option.
    private const float Chevron = 18.0f;
    private const float Seam = 2.0f;

    // The bar is the head of the ball's own panel, so it is set out to the same width rather than
    // to the width of its own labels.
    private static readonly float Wide = (Navball.Extent.X - Narrow - Gap * 2.0f) * 0.5f;

    public static readonly Vector2 Extent = new Vector2(Navball.Extent.X, Height);

    private Flight _flight;
    private ModeMenu _menu;

    private Button _sas;
    private Button _reference;
    private Button _rcs;
    private Button _pulse;

    private Control _glyph;
    private Control _gauge;
    private Control _chevron;

    public void Bind(Flight flight, ModeMenu menu) {

        _flight = flight;
        _menu = menu;

        CustomMinimumSize = Extent;
        Size = Extent;

        MouseFilter = MouseFilterEnum.Ignore;

        _sas = Add("SAS", Wide, 0.0f);
        _reference = Add(string.Empty, Narrow, Wide + Gap);
        _rcs = Add("RCS", Wide - Chevron - Seam, Wide + Narrow + Gap * 2.0f);
        _pulse = Add(string.Empty, Chevron, Wide * 2.0f + Narrow + Gap * 2.0f - Chevron);

        _glyph = Overlay(_reference, DrawReference);
        _gauge = Overlay(_rcs, DrawMonopropellant);
        _chevron = Overlay(_pulse, DrawChevron);

        _sas.Pressed += ToggleAutopilot;
        _reference.Pressed += RaiseMenu;
        _rcs.Pressed += ToggleThrusters;
        _pulse.Pressed += RaiseThrusters;

    }

    public void Sync() {

        bool armed = _flight.Autopilot.Hold != AttitudeHold.Off;

        HudTheme.Light(_sas, armed);
        HudTheme.Light(_reference, armed && _flight.Autopilot.Hold != AttitudeHold.Stability);
        HudTheme.Light(_rcs, _flight.Vessel.RcsEnabled);
        HudTheme.Light(_pulse, _flight.RcsPulse);

        _glyph.QueueRedraw();
        _gauge.QueueRedraw();
        _chevron.QueueRedraw();

    }

    // The thrusters are the only attitude authority aboard, so how much monopropellant is left
    // belongs on the switch that arms them, laid along its bottom edge rather than under it.
    private void DrawMonopropellant() {

        Vessel vessel = _flight.Vessel;

        if (vessel.RcsPropellantCapacity <= 0.0) {

            return;

        }

        float fill = (float)Math.Clamp(vessel.RcsPropellantMass / vessel.RcsPropellantCapacity, 0.0, 1.0);

        Rect2 track = new Rect2(Border, _gauge.Size.Y - Gauge - Border, _gauge.Size.X - Border * 2.0f, Gauge);

        _gauge.DrawRect(track, HudTheme.Well);
        _gauge.DrawRect(new Rect2(track.Position, new Vector2(track.Size.X * fill, Gauge)), vessel.RcsEnabled ? HudTheme.Ink : HudTheme.Faint);

    }

    private void DrawReference() {

        AttitudeHold hold = _flight.Autopilot.Hold;

        Color ink = hold == AttitudeHold.Off ? HudTheme.Faint : HudTheme.Ink;

        AttitudeMarker.Draw(_glyph, hold, _glyph.Size * 0.5f, 7.5f, ink, 1.3f);

    }

    private void DrawChevron() {

        Vector2 centre = _chevron.Size * 0.5f;

        Color ink = _flight.RcsPulse ? HudTheme.Ink : HudTheme.Faint;

        _chevron.DrawPolyline(new[] {

            centre + new Vector2(-4.5f, 2.0f),
            centre + new Vector2(0.0f, -2.5f),
            centre + new Vector2(4.5f, 2.0f),

        }, ink, 1.4f, true);

    }

    private void ToggleAutopilot() {

        _flight.Autopilot.Hold = _flight.Autopilot.Hold == AttitudeHold.Off ? AttitudeHold.Stability : AttitudeHold.Off;

    }

    private void ToggleThrusters() {

        _flight.Vessel.RcsEnabled = !_flight.Vessel.RcsEnabled;

    }

    private void RaiseThrusters() {

        if (_menu.Open) {

            _menu.Dismiss();

            return;

        }

        _menu.RaiseThrusters(_flight, new Vector2(GlobalPosition.X + Extent.X, GlobalPosition.Y - 6.0f));

    }

    private void RaiseMenu() {

        if (_menu.Open) {

            _menu.Dismiss();

            return;

        }

        _menu.RaiseAttitude(_flight, new Vector2(GlobalPosition.X, GlobalPosition.Y - 6.0f));

    }

    /// <summary>A pane over a switch, so the switch can carry a drawing its own label cannot.</summary>
    private static Control Overlay(Button host, Action draw) {

        Control pane = new Control { MouseFilter = MouseFilterEnum.Ignore };

        pane.SetAnchorsPreset(LayoutPreset.FullRect);
        pane.Draw += draw;

        host.AddChild(pane);

        return pane;

    }

    private Button Add(string text, float width, float x) {

        Button button = HudTheme.Button(text, new Vector2(width, Height));

        button.Position = new Vector2(x, 0.0f);
        button.Size = new Vector2(width, Height);

        AddChild(button);

        return button;

    }

}
