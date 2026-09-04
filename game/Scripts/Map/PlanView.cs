using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The orbit seen down its own normal, where it is a true ellipse and nothing is hidden
/// behind the planet. The 3D view can be pointed anywhere; this one always reads, so it is also a
/// place to put the node down when the conic in the world is edge-on to the camera.</summary>
public sealed partial class PlanView : Control {

    public static readonly Vector2 Extent = new Vector2(212.0f, 170.0f);

    private const int Samples = 224;

    // Enough to find the extent of a conic without sampling it at drawing resolution twice.
    private const int Bounds = 48;

    private const float Pad = 16.0f;

    private const double Tau = Math.PI * 2.0;

    // Samples per dash, and per gap, for the planned conic.
    private const int Dash = 3;

    private const int Arc = 48;

    // Below this the sun is near enough the orbit normal that the orbit sits in the terminator
    // plane: there is no day side to draw across the disc and nothing on the conic is ever eclipsed.
    private const float Grazing = 0.08f;

    private readonly List<Vector2> _run = new List<Vector2>(Samples + 1);
    private readonly List<Color> _tint = new List<Color>(Samples + 1);

    private Flight _flight;

    private Vector3d _major;
    private Vector3d _minor;

    private Vector3d _sun;

    private Vector2 _origin;
    private float _scale;

    private bool _placing;

    public void CancelPlacement() => _placing = false;

    public void Build(Flight flight) {

        _flight = flight;

        CustomMinimumSize = Extent;
        Size = Extent;

        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;

        ClipContents = true;

    }

    public void Sync() {

        QueueRedraw();

    }

    public override void _GuiInput(InputEvent @event) {

        if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left) {

            _placing = button.Pressed;

            if (button.Pressed) {

                Aim(button.Position);

            }

            AcceptEvent();

        }

        if (@event is InputEventMouseMotion && _placing) {

            Aim(GetLocalMousePosition());

            AcceptEvent();

        }

    }

    // The projection is the orbit's own perifocal frame, so the angle from the centre to the
    // pointer is the true anomaly outright. No search, no sampling, no nearest-point fudge.
    private void Aim(Vector2 at) {

        Vector2 local = at - _origin;

        double anomaly = Math.Atan2(-local.Y, local.X);

        _flight.PlaceNode(MapPath.Snap(_flight.Orbit, anomaly));

    }

    public override void _Draw() {

        Orbit orbit = _flight.Orbit;

        QuaternionD frame = orbit.PerifocalToInertial;

        _major = frame.Rotate(Vector3d.UnitX);
        _minor = frame.Rotate(Vector3d.UnitY);

        _sun = Frames.Sim(Main.SunDirection);

        Fit(orbit, _flight.PlannedOrbit);

        DrawStyleBox(HudTheme.Panel(0.0f), new Rect2(Vector2.Zero, Extent));

        Body();

        Maneuver node = _flight.Node;
        Orbit planned = _flight.PlannedOrbit;

        double cut = node != null ? MapPath.Cut(orbit, _flight.Time, node.Time) : 1.0;

        Trace(orbit, orbit.TrueAnomalyAt(_flight.Time), Tau, cut, Ink(orbit), false);

        if (planned != null) {

            Trace(planned, planned.TrueAnomalyAt(node.Time), Tau, 1.0, Ink(planned), true);

            Apsides(planned, HudTheme.Faint);

        }

        Apsides(orbit, HudTheme.Dim);

        if (node != null) {

            Diamond(Project(orbit.PositionAtTrueAnomaly(orbit.TrueAnomalyAt(node.Time))), HudTheme.Ink);

        }

        Heading();

    }

    /// <summary>The vessel, as a chevron along its own velocity. A dot says where it is; this says
    /// which way round the drawn ellipse it is going, which is what the node is ahead or behind of.</summary>
    private void Heading() {

        Vector2 at = Project(_flight.Vessel.Position);

        Vector2 along = Along(_flight.Vessel.Velocity);
        Vector2 side = new Vector2(-along.Y, along.X);

        DrawColoredPolygon(new[] {

            at + along * 5.5f,
            at - along * 3.0f + side * 3.8f,
            at - along * 3.0f - side * 3.8f,

        }, HudTheme.Ink);

    }

    /// <summary>The apsides, filled for high and hollow for low, the same pair of shapes the map
    /// itself uses. Nothing joins them: a chord across the planet is a line the vessel never flies.
    /// A periapsis under the ground is not a place, so it is not marked as one.</summary>
    private void Apsides(Orbit orbit, Color ink) {

        if (orbit.PeriapsisRadius >= _flight.Body.Radius) {

            DrawArc(Project(orbit.PositionAtTrueAnomaly(0.0)), 2.6f, 0.0f, Mathf.Tau, 16, ink, 1.2f, true);

        }

        if (orbit.IsClosed) {

            DrawCircle(Project(orbit.PositionAtTrueAnomaly(Math.PI)), 2.6f, ink);

        }

    }

    private Color Ink(Orbit orbit) => orbit.PeriapsisRadius < _flight.Body.Radius ? HudTheme.Caution : HudTheme.Ink;

    // Fitted to the bounds of everything drawn rather than to the live conic alone: a plan that
    // triples the apoapsis has to stay in the cell, and the conic is centred on its own centre so
    // it uses the whole of it rather than running off one side.
    private void Fit(Orbit orbit, Orbit planned) {

        double radius = _flight.Body.Radius;

        double lowAlong = -radius;
        double highAlong = radius;
        double lowAcross = -radius;
        double highAcross = radius;

        Extend(orbit, ref lowAlong, ref highAlong, ref lowAcross, ref highAcross);

        if (planned != null) {

            Extend(planned, ref lowAlong, ref highAlong, ref lowAcross, ref highAcross);

        }

        Vector2 box = Extent - new Vector2(Pad, Pad) * 2.0f;

        _scale = (float)Math.Min(box.X / (highAlong - lowAlong), box.Y / (highAcross - lowAcross));

        double centreAlong = (lowAlong + highAlong) * 0.5;
        double centreAcross = (lowAcross + highAcross) * 0.5;

        _origin = Extent * 0.5f - new Vector2((float)(centreAlong * _scale), (float)(-centreAcross * _scale));

    }

    private void Extend(Orbit orbit, ref double lowAlong, ref double highAlong, ref double lowAcross, ref double highAcross) {

        double limit = orbit.IsClosed ? Math.PI : orbit.TrueAnomalyLimit * 0.9;

        for (int sample = 0; sample <= Bounds; sample++) {

            Vector3d point = orbit.PositionAtTrueAnomaly(-limit + 2.0 * limit * sample / Bounds);

            double along = Vector3d.Dot(point, _major);
            double across = Vector3d.Dot(point, _minor);

            lowAlong = Math.Min(lowAlong, along);
            highAlong = Math.Max(highAlong, along);

            lowAcross = Math.Min(lowAcross, across);
            highAcross = Math.Max(highAcross, across);

        }

    }

    /// <summary>A direction carried into the cell. Project maps places; this maps the way something
    /// points, so it carries no origin.</summary>
    private Vector2 Face(Vector3d direction) => new Vector2((float)Vector3d.Dot(direction, _major), (float)-Vector3d.Dot(direction, _minor));

    private Vector2 Along(Vector3d direction) {

        Vector2 face = Face(direction);

        return face.Length() > 0.0f ? face.Normalized() : Vector2.Right;

    }

    private Vector2 Project(Vector3d world) {

        double along = Vector3d.Dot(world, _major);
        double across = Vector3d.Dot(world, _minor);

        return _origin + new Vector2((float)(along * _scale), (float)(-across * _scale));

    }

    private void Body() {

        Vector2 centre = Project(Vector3d.Zero);

        float radius = (float)(_flight.Body.Radius * _scale);

        Vector2 sun = Face(_sun);

        if (sun.Length() < Grazing) {

            DrawCircle(centre, radius, HudTheme.Well);

        }
        else {

            DrawCircle(centre, radius, HudTheme.Well * new Color(1.0f, 1.0f, 1.0f, 0.40f));

            Half(centre, radius, sun.Angle(), HudTheme.Well);

        }

        DrawArc(centre, radius, 0.0f, Mathf.Tau, 96, HudTheme.Edge, 1.0f, true);

    }

    private void Half(Vector2 centre, float radius, float facing, Color ink) {

        Vector2[] wedge = new Vector2[Arc + 1];

        for (int step = 0; step <= Arc; step++) {

            float angle = facing - Mathf.Pi * 0.5f + Mathf.Pi * step / Arc;

            wedge[step] = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

        }

        DrawColoredPolygon(wedge, ink);

    }

    /// <summary>Whether the planet's shadow falls on a point. Done in three dimensions rather than
    /// on the projection, because the cylinder is a real one and the cell is only a drawing of it.</summary>
    private bool Eclipsed(Vector3d point) {

        double along = Vector3d.Dot(point, _sun);

        if (along >= 0.0) {

            return false;

        }

        return (point - _sun * along).Length < _flight.Body.Radius;

    }

    private void Trace(Orbit orbit, double start, double span, double cut, Color ink, bool dotted) {

        _run.Clear();
        _tint.Clear();

        for (int sample = 0; sample <= Samples; sample++) {

            double fraction = (double)sample / Samples;

            double anomaly = start + span * fraction;

            // The run of a conic that lies under the ground is not a path anything flies; drawn, it
            // is a chord through the body. It ends where the surface is and picks up on the far side.
            if (orbit.RadiusAtTrueAnomaly(anomaly) < _flight.Body.Radius) {

                Stroke(dotted);

                continue;

            }

            Vector3d point = orbit.PositionAtTrueAnomaly(anomaly);

            float shade = MapPath.Shade(fraction, cut);

            _run.Add(Project(point));

            // Capped rather than scaled. Multiplying compounds with the fade, so the far end of the
            // shadow vanishes into it; a flat weight gives the stretch a clean step at both ends,
            // which is the whole point of drawing it.
            _tint.Add(ink * new Color(1.0f, 1.0f, 1.0f, Eclipsed(point) ? Math.Min(shade, 0.22f) : shade));

        }

        Stroke(dotted);

    }

    private void Stroke(bool dotted) {

        if (_run.Count >= 2) {

            if (dotted) {

                for (int sample = 0; sample + 1 < _run.Count; sample++) {

                    if (sample / Dash % 2 == 0) {

                        DrawLine(_run[sample], _run[sample + 1], _tint[sample], 1.8f, true);

                    }

                }

            }
            else {

                DrawPolylineColors(_run.ToArray(), _tint.ToArray(), 1.8f, true);

            }

        }

        _run.Clear();
        _tint.Clear();

    }

    private void Diamond(Vector2 at, Color ink) {

        DrawColoredPolygon(new[] {

            at + new Vector2(0.0f, -4.5f),
            at + new Vector2(4.5f, 0.0f),
            at + new Vector2(0.0f, 4.5f),
            at + new Vector2(-4.5f, 0.0f),

        }, ink);

    }

}
