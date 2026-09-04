using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>Everything the map draws over the world: the conic the vessel is on, the one the plan
/// would leave it on, and the handful of points on either that are worth a name. All of it is
/// projected through the map camera and stroked in 2D, so a line is one weight at any range.</summary>
public sealed partial class MapPath : Control {

    private const int Samples = 320;

    private const double Tau = Math.PI * 2.0;

    // Below these the crossing and the apsides are numerical artefacts rather than points worth
    // marking: a circular orbit has no periapsis, and at this eccentricity the two apsides are
    // within a few hundred metres of each other and land on the same pixel with the same figure.
    private const double Equatorial = 0.002;
    private const double Circular = 1e-4;

    private const float MarkReach = 12.0f;
    private const float ConicReach = 9.0f;

    // How near the pointer has to bring the node before it takes the marker's anomaly exactly. A
    // burn meant for apoapsis wants to be at apoapsis, not four seconds short of it.
    private const double SnapAngle = 0.05;

    private const float Glyph = 4.5f;

    // Samples per dash, and per gap. The planned conic is drawn dotted so that it reads as a
    // proposal rather than as a second orbit the vessel is somehow already on.
    private const int Dash = 3;

    public enum Kind { Apoapsis, Periapsis, Ascending, Descending, Entry, Impact, Vessel }

    /// <summary>A named point on a conic. Instances live as long as the map does, so a popover keyed
    /// on one stays keyed on it while its figures go on changing underneath.</summary>
    public sealed class Mark {

        public Kind Kind { get; init; }

        public double Anomaly { get; set; }
        public double Radius { get; set; }
        public double Seconds { get; set; }

        public Vector2 Screen { get; set; }

        public bool Live { get; set; }
        public bool Hidden { get; set; }

    }

    // Indexed by Kind, so the order of these is the order of the enum and not a matter of taste.
    private readonly Mark[] _marks = {

        new Mark { Kind = Kind.Apoapsis },
        new Mark { Kind = Kind.Periapsis },
        new Mark { Kind = Kind.Ascending },
        new Mark { Kind = Kind.Descending },
        new Mark { Kind = Kind.Entry },
        new Mark { Kind = Kind.Impact },
        new Mark { Kind = Kind.Vessel },

    };

    private readonly Vector2[] _screen = new Vector2[Samples + 1];
    private readonly double[] _anomaly = new double[Samples + 1];
    private readonly bool[] _front = new bool[Samples + 1];

    private readonly List<Vector2> _run = new List<Vector2>(Samples + 1);
    private readonly List<Color> _tint = new List<Color>(Samples + 1);

    private Flight _flight;
    private MapView _map;

    private Vector2 _node;
    private bool _nodeLive;

    private Vector3 _eye;
    private Vector3 _centre;

    private Mark _hovered;

    public void Bind(Flight flight, MapView map) {

        _flight = flight;
        _map = map;

        SetAnchorsPreset(LayoutPreset.FullRect);

        MouseFilter = MouseFilterEnum.Ignore;

    }

    public override void _Process(double delta) {

        Visible = _map.Open;

        if (!Visible) {

            return;

        }

        Vector2 pointer = GetViewport().GetMousePosition();

        _hovered = _map.Dragging ? null : Nearest(pointer, MarkReach);

        QueueRedraw();

    }

    /// <summary>Where the node's handle is on screen, and whether it is on screen at all.</summary>
    public Vector2 Node => _node;
    public bool NodeLive => _nodeLive;

    public bool PickNode(Vector2 at) => _nodeLive && at.DistanceTo(_node) <= MarkReach;

    public bool PickVessel(Vector2 at) {

        Mark vessel = _marks[(int)Kind.Vessel];

        return vessel.Live && at.DistanceTo(vessel.Screen) <= MarkReach;

    }

    public Mark PickMark(Vector2 at) => Nearest(at, MarkReach);

    /// <summary>The anomaly under the pointer, if the pointer is on the line at all.</summary>
    public bool PickConic(Vector2 at, out double anomaly) => Closest(at, ConicReach, out anomaly);

    /// <summary>The anomaly nearest the pointer at any distance, snapped to a marker it passes.</summary>
    public bool PickSlip(Vector2 at, out double anomaly) => Closest(at, float.MaxValue, out anomaly);

    public override void _Draw() {

        Orbit orbit = _flight.Orbit;

        _eye = _map.Camera.GlobalPosition;
        _centre = Frames.Point(Vector3d.Zero);

        Maneuver node = _flight.Node;
        Orbit planned = _flight.PlannedOrbit;

        double cut = node != null ? Cut(orbit, _flight.Time, node.Time) : 1.0;

        Span(orbit, out double start, out double span);

        Trace(orbit, start, span, cut, Ink(orbit), true, false);

        if (planned != null) {

            double from = planned.TrueAnomalyAt(node.Time);

            Trace(planned, from, planned.IsClosed ? Tau : planned.TrueAnomalyLimit * 0.985 - from, 1.0, Ink(planned), false, true);

            Ghost(planned, node.Time);

        }

        Traffic();

        Update(orbit);

        foreach (Mark mark in _marks) {

            Show(mark);

        }

        DrawNode(orbit, node);

    }

    // A conic that ends in the ground is the one thing on the map that is not a matter of taste,
    // so it is the one thing on the map allowed a hue.
    private Color Ink(Orbit orbit) => orbit.PeriapsisRadius < _flight.Body.Radius ? HudTheme.Caution : HudTheme.Ink;

    /// <summary>Everything else still up there. A spent stage is a body with a conic of its own, so
    /// it gets one - drawn thin, unlabelled and unpickable, because the plan is not about it.</summary>
    private void Traffic() {

        foreach (Flight.Tracked tracked in _flight.Debris) {

            Span(tracked.Rails, out double start, out double span);

            Trace(tracked.Rails, start, span, 1.0, HudTheme.Dim * Alpha(0.34f), false, false);

            Vector3 world = Frames.Point(tracked.Vessel.Position);

            if (_map.Camera.IsPositionBehind(world)) {

                continue;

            }

            DrawCircle(_map.Camera.UnprojectPosition(world), 2.0f, HudTheme.Dim * Alpha(Behind(world) ? 0.4f : 0.85f));

        }

    }

    private void Span(Orbit orbit, out double start, out double span) {

        if (orbit.IsClosed) {

            start = orbit.TrueAnomalyAt(_flight.Time);
            span = Tau;

            return;

        }

        double limit = orbit.TrueAnomalyLimit * 0.985;

        start = -limit;
        span = limit * 2.0;

    }

    /// <summary>Where along the sampled run the plan takes over, as a fraction of it.</summary>
    public static double Cut(Orbit orbit, double now, double when) {

        double node = orbit.TrueAnomalyAt(when);

        if (orbit.IsClosed) {

            return Wrap(node - orbit.TrueAnomalyAt(now)) / Tau;

        }

        double limit = orbit.TrueAnomalyLimit * 0.985;

        return Mathf.Clamp((float)((node + limit) / (limit * 2.0)), 0.0f, 1.0f);

    }

    private void Trace(Orbit orbit, double start, double span, double cut, Color ink, bool record, bool dotted) {

        for (int sample = 0; sample <= Samples; sample++) {

            double fraction = (double)sample / Samples;
            double anomaly = start + span * fraction;

            Vector3 world = Frames.Point(orbit.PositionAtTrueAnomaly(anomaly));

            // A conic carries on under the ground, and the stretch of it that does is not a path
            // anything flies: drawn, it runs to the centre of the planet. It is cut, not faded.
            bool flown = orbit.RadiusAtTrueAnomaly(anomaly) >= _flight.Body.Radius;

            // Unprojecting a point at or behind the camera plane is a divide by zero in Godot, so
            // the frustum test comes first and the projection only happens for points that pass it.
            bool front = flown && !_map.Camera.IsPositionBehind(world);

            Vector2 screen = front ? _map.Camera.UnprojectPosition(world) : Vector2.Zero;

            if (record) {

                _anomaly[sample] = anomaly;
                _screen[sample] = screen;
                _front[sample] = front;

            }

            if (!front || (dotted && sample / Dash % 2 == 1)) {

                Stroke();

                continue;

            }

            _run.Add(screen);
            _tint.Add(ink * Alpha(Shade(fraction, cut) * (Behind(world) ? 0.30f : 1.0f)));

        }

        Stroke();

    }

    // Past the node the live conic is not what the vessel will fly, so it drops to a hairline: the
    // loop still closes and stays clickable, but the eye runs straight through the burn onto the plan.
    //
    // Nothing here goes below Floor. A thin line under about a tenth alpha does not render as a dim
    // line on a dark ground, it renders as nothing, and a gradient that fades to nothing reads as a
    // line that breaks rather than one that recedes.
    private const float Floor = 0.15f;

    public static float Shade(double fraction, double cut) {

        if (cut >= 1.0) {

            return Mathf.Lerp(0.95f, 0.24f, (float)fraction);

        }

        if (fraction > cut) {

            return Floor;

        }

        return Mathf.Lerp(0.95f, 0.28f, (float)(fraction / Math.Max(cut, 1e-6)));

    }

    private void Stroke() {

        if (_run.Count >= 2) {

            DrawPolylineColors(_run.ToArray(), _tint.ToArray(), 2.0f, true);

        }

        _run.Clear();
        _tint.Clear();

    }

    /// <summary>The plan's own apsides and impact, drawn but not named; the elements panel carries
    /// their figures, and a second set of labels over the first is unreadable at any zoom.</summary>
    private void Ghost(Orbit planned, double when) {

        Color ink = Ink(planned) * Alpha(0.55f);

        if (planned.IsClosed) {

            Pip(planned, Math.PI, Kind.Apoapsis, ink);

        }

        Pip(planned, 0.0, Kind.Periapsis, ink);

        if (Crossing(planned, _flight.Body.Radius, out double anomaly)) {

            Pip(planned, anomaly, Kind.Impact, HudTheme.Caution * Alpha(0.75f));

        }

    }

    private void Pip(Orbit orbit, double anomaly, Kind kind, Color ink) {

        Vector3 world = Frames.Point(orbit.PositionAtTrueAnomaly(anomaly));

        // Same rule as the live conic's own marks: behind the body it goes, it does not dim.
        if (_map.Camera.IsPositionBehind(world) || Behind(world)) {

            return;

        }

        Emblem(kind, _map.Camera.UnprojectPosition(world), ink);

    }

    private void Update(Orbit orbit) {

        double radius = _flight.Body.Radius;

        bool apsides = orbit.Eccentricity > Circular;

        Seat(Kind.Apoapsis, orbit, Math.PI, orbit.IsClosed && apsides);
        Seat(Kind.Periapsis, orbit, 0.0, apsides);

        Seat(Kind.Ascending, orbit, orbit.AscendingNodeTrueAnomaly, orbit.Inclination > Equatorial);
        Seat(Kind.Descending, orbit, orbit.DescendingNodeTrueAnomaly, orbit.Inclination > Equatorial);

        // Suppressed once the vessel is already in the air: the next crossing is then a pass it
        // is very unlikely to live to make, and marking it says the opposite.
        bool entering = Crossing(orbit, radius + _flight.Body.AtmosphereTop, out double entry) && !_flight.InAtmosphere;

        Seat(Kind.Entry, orbit, entry, entering);

        bool falls = Crossing(orbit, radius, out double fall);

        Seat(Kind.Impact, orbit, fall, falls);

        Seat(Kind.Vessel, orbit, orbit.TrueAnomalyAt(_flight.Time), true);

    }

    private void Seat(Kind kind, Orbit orbit, double anomaly, bool shown) {

        Mark mark = _marks[(int)kind];

        if (!shown) {

            mark.Live = false;

            return;

        }

        Vector3 world = Frames.Point(orbit.PositionAtTrueAnomaly(anomaly));

        mark.Live = !_map.Camera.IsPositionBehind(world);

        if (!mark.Live) {

            return;

        }

        mark.Anomaly = anomaly;
        mark.Radius = orbit.RadiusAtTrueAnomaly(anomaly);
        mark.Seconds = orbit.TimeToTrueAnomaly(_flight.Time, anomaly);

        mark.Screen = _map.Camera.UnprojectPosition(world);

        mark.Hidden = Behind(world);

        // An apsis and a crossing are points on the conic, not places on the planet. One the body is
        // standing in front of puts a label over a hemisphere the vessel is nowhere near, so it goes
        // rather than dimming. The vessel, the entry and an impact stay: a pilot must never lose
        // track of where the vehicle is or of where it stops being able to choose.
        if (mark.Hidden && kind != Kind.Vessel && kind != Kind.Impact && kind != Kind.Entry) {

            mark.Live = false;

        }

    }

    private void Show(Mark mark) {

        if (!mark.Live) {

            return;

        }

        bool lit = ReferenceEquals(mark, _hovered);

        Color ink = mark.Kind == Kind.Impact ? HudTheme.Caution : lit ? HudTheme.Ink : HudTheme.Dim;

        if (mark.Hidden && !lit) {

            ink *= Alpha(0.42f);

        }

        Emblem(mark.Kind, mark.Screen, ink);

        string label = Label(mark);

        if (label.Length == 0) {

            return;

        }

        // The label carries its own ground, the way every chip in the flight interface does. Ink
        // this small over a lit day side is unreadable without one.
        Rect2 chip = new Rect2(mark.Screen.X + 10.0f, mark.Screen.Y - 9.0f, HudTheme.Width(HudTheme.Strong, HudTheme.Small, label) + 14.0f, 18.0f);

        DrawStyleBox(HudTheme.Panel(0.0f), chip);

        HudTheme.WriteIn(this, HudTheme.Strong, HudTheme.Small, chip.Grow(-7.0f), label, ink, HorizontalAlignment.Left);

    }

    private string Label(Mark mark) {

        double altitude = mark.Radius - _flight.Body.Radius;

        return mark.Kind switch {

            Kind.Apoapsis => $"AP {Hud.Distance(altitude)}",
            Kind.Periapsis => $"PE {Hud.Distance(altitude)}",

            Kind.Ascending => "AN",
            Kind.Descending => "DN",

            Kind.Entry => $"ENTRY {Hud.Clock(mark.Seconds)}",

            Kind.Impact => $"IMPACT {Hud.Clock(mark.Seconds)}",

            _ => string.Empty,

        };

    }

    // One primitive each, and no two of them the same shape. Anything more elaborate is a
    // five-pixel drawing nobody can read against a lit planet.
    private void Emblem(Kind kind, Vector2 at, Color ink) {

        switch (kind) {

            case Kind.Apoapsis:

                DrawCircle(at, Glyph, ink);

                break;

            case Kind.Periapsis:

                DrawArc(at, Glyph, 0.0f, Mathf.Tau, 20, ink, 1.6f, true);

                break;

            case Kind.Ascending:

                Triangle(at, -1.0f, ink);

                break;

            case Kind.Descending:

                Triangle(at, 1.0f, ink);

                break;

            case Kind.Entry:

                // A threshold, drawn as one: the conic crosses a line rather than arriving anywhere.
                DrawLine(at + new Vector2(-Glyph * 1.3f, 0.0f), at + new Vector2(Glyph * 1.3f, 0.0f), ink, 2.0f, true);
                DrawLine(at + new Vector2(0.0f, 0.0f), at + new Vector2(0.0f, Glyph), ink, 1.4f, true);

                break;

            case Kind.Impact:

                DrawLine(at + new Vector2(-Glyph, -Glyph), at + new Vector2(Glyph, Glyph), ink, 1.6f, true);
                DrawLine(at + new Vector2(-Glyph, Glyph), at + new Vector2(Glyph, -Glyph), ink, 1.6f, true);

                break;

            case Kind.Vessel:

                DrawCircle(at, 2.5f, ink);
                DrawArc(at, 6.5f, 0.0f, Mathf.Tau, 24, ink * Alpha(0.5f), 1.0f, true);

                break;

        }

    }

    private void Triangle(Vector2 at, float sense, Color ink) {

        DrawColoredPolygon(new[] {

            at + new Vector2(0.0f, Glyph * 1.15f * sense),
            at + new Vector2(-Glyph, -Glyph * 0.8f * sense),
            at + new Vector2(Glyph, -Glyph * 0.8f * sense),

        }, ink);

    }

    private void DrawNode(Orbit orbit, Maneuver node) {

        _nodeLive = false;

        if (node == null) {

            return;

        }

        Vector3 world = Frames.Point(orbit.PositionAtTrueAnomaly(orbit.TrueAnomalyAt(node.Time)));

        if (_map.Camera.IsPositionBehind(world)) {

            return;

        }

        Vector2 seat = _map.Camera.UnprojectPosition(world);

        _node = _map.Dragging ? _map.DragHandle : seat;
        _nodeLive = true;

        if (_map.Dragging) {

            Leash(seat);

        }

        Color ink = _map.Dragging || PickNode(GetViewport().GetMousePosition()) ? HudTheme.Ink : HudTheme.Dim;

        DrawColoredPolygon(new[] {

            _node + new Vector2(0.0f, -6.0f),
            _node + new Vector2(6.0f, 0.0f),
            _node + new Vector2(0.0f, 6.0f),
            _node + new Vector2(-6.0f, 0.0f),

        }, ink);

    }

    /// <summary>The leash from where the node sits on the conic to where the hand has taken it,
    /// and what that pull is worth. The scale is logarithmic, so the figure is the only honest
    /// way to read it - there is nothing to measure the pull against.</summary>
    private void Leash(Vector2 seat) {

        DrawLine(seat, _map.DragHandle, HudTheme.Ink * Alpha(0.45f), 1.0f, true);

        // The plane only shows when it is not the obvious one, so the chip stays a figure rather
        // than a legend the whole time the hand is on the node.
        string caption = _map.Deep ? $"{_flight.Node.DeltaV:N0} m/s · DEPTH" : $"{_flight.Node.DeltaV:N0} m/s";

        Rect2 chip = new Rect2(_map.DragHandle.X + 12.0f, _map.DragHandle.Y - 26.0f, HudTheme.Width(HudTheme.NumeralStrong, HudTheme.Small, caption) + 14.0f, 18.0f);

        DrawStyleBox(HudTheme.Panel(0.0f), chip);

        HudTheme.WriteIn(this, HudTheme.NumeralStrong, HudTheme.Small, chip.Grow(-7.0f), caption, HudTheme.Ink, HorizontalAlignment.Left);

    }

    private Mark Nearest(Vector2 at, float reach) {

        Mark found = null;

        float best = reach;

        foreach (Mark mark in _marks) {

            if (!mark.Live) {

                continue;

            }

            float distance = at.DistanceTo(mark.Screen);

            if (distance < best) {

                best = distance;
                found = mark;

            }

        }

        return found;

    }

    private bool Closest(Vector2 at, float reach, out double anomaly) {

        anomaly = 0.0;

        float best = reach;

        bool found = false;

        for (int sample = 0; sample <= Samples; sample++) {

            if (!_front[sample]) {

                continue;

            }

            float distance = at.DistanceTo(_screen[sample]);

            if (distance < best) {

                best = distance;
                anomaly = _anomaly[sample];

                found = true;

            }

        }

        if (found) {

            anomaly = Snap(_flight.Orbit, anomaly);

        }

        return found;

    }

    /// <summary>Takes the anomaly to a marker it has come within a hair of. A burn meant for
    /// apoapsis wants to be at apoapsis, not four seconds short of it.</summary>
    public static double Snap(Orbit orbit, double anomaly) {

        double[] rungs = orbit.Inclination > Equatorial

            ? new[] { 0.0, Math.PI, orbit.AscendingNodeTrueAnomaly, orbit.DescendingNodeTrueAnomaly }
            : new[] { 0.0, Math.PI };

        foreach (double rung in rungs) {

            if (Math.Abs(Signed(anomaly - rung)) < SnapAngle) {

                return rung;

            }

        }

        return anomaly;

    }

    /// <summary>Whether the planet stands between the camera and a point. Done in doubles: at a
    /// million metres the discriminant of this in singles is worth about a kilometre.</summary>
    private bool Behind(Vector3 world) {

        double rayX = world.X - _eye.X;
        double rayY = world.Y - _eye.Y;
        double rayZ = world.Z - _eye.Z;

        double offsetX = _eye.X - _centre.X;
        double offsetY = _eye.Y - _centre.Y;
        double offsetZ = _eye.Z - _centre.Z;

        double a = rayX * rayX + rayY * rayY + rayZ * rayZ;

        if (a <= 0.0) {

            return false;

        }

        double b = 2.0 * (offsetX * rayX + offsetY * rayY + offsetZ * rayZ);
        double c = offsetX * offsetX + offsetY * offsetY + offsetZ * offsetZ - _flight.Body.Radius * _flight.Body.Radius;

        double discriminant = b * b - 4.0 * a * c;

        if (discriminant <= 0.0) {

            return false;

        }

        double hit = (-b - Math.Sqrt(discriminant)) / (2.0 * a);

        return hit > 0.0 && hit < 1.0;

    }

    /// <summary>Where the conic falls through a given radius on its way down, if it does. The
    /// ground and the top of the air are the same question asked of two different radii.</summary>
    public static bool Crossing(Orbit orbit, double radius, out double anomaly) {

        anomaly = 0.0;

        if (orbit.PeriapsisRadius >= radius || orbit.Eccentricity <= 0.0) {

            return false;

        }

        double cosine = (orbit.SemiLatusRectum / radius - 1.0) / orbit.Eccentricity;

        if (Math.Abs(cosine) > 1.0) {

            return false;

        }

        anomaly = Tau - Math.Acos(cosine);

        return true;

    }

    public static double Wrap(double radians) {

        double wrapped = radians % Tau;

        return wrapped < 0.0 ? wrapped + Tau : wrapped;

    }

    /// <summary>An angle difference folded into plus or minus half a turn.</summary>
    public static double Signed(double radians) {

        double wrapped = Wrap(radians);

        return wrapped > Math.PI ? wrapped - Tau : wrapped;

    }

    private static Color Alpha(float alpha) => new Color(1.0f, 1.0f, 1.0f, alpha);

}
