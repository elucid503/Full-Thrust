using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>Altitude and speed, over the shape of the orbit they came from.</summary>
public sealed partial class TrajectoryPanel : Control {

    private const int PanelWidth = 336;
    private const int PanelHeight = 74;

    // The panel's own hairline. The graph runs to it on every side, so the cell is the box.
    private const int Border = 1;

    private const int ChipStrip = 26;

    // What the column reserves: what an altitude in the thousands of kilometres needs. Shorter
    // readings are centred in it rather than left against an edge with the slack all on one side.
    private const int ReadoutWidth = 103;

    // Each line's box is its own digit height plus the same lead as the other, so the two carry
    // equal margins and centring the pair of boxes centres the ink the pair is actually made of.
    private const float Lead = 10.0f;

    private const int Samples = 112;

    // A quarter of the orbit is kept behind the vessel: a curve that only runs forward gives the dot
    // nothing to have come from, and the climb or fall it is on stops reading.
    private const double Behind = 0.25;

    // What the window covers when the orbit has no period to size it from.
    private const double OpenWindow = 2400.0;

    // The shortest a window is ever sized to. On the last seconds of a descent the graph would
    // otherwise be redrawn to a span too short to read anything off.
    private const double Shortest = 20.0;

    public static readonly Vector2 Extent = new Vector2(PanelWidth, PanelHeight + ChipStrip);

    /// <summary>Where the panel actually ends. The chip strip under the box is reserved space, not
    /// drawn space, so a panel below this one sits under the box until there is a chip to clear.</summary>
    public float Foot => PanelHeight + (Chipped ? ChipStrip : 0.0f);

    private bool Chipped => _flight != null && (_flight.WarpStep > 0 || (_flight.Node != null && !_flight.Node.IsEmpty));

    private readonly Vector2[] _curve = new Vector2[Samples + 1];

    private Flight _flight;

    private TickReadout _altitude;
    private TickReadout _speed;

    private Rect2 _graph;

    private double _low;
    private double _high;

    private int _now;

    public override void _Ready() {

        CustomMinimumSize = Extent;
        Size = Extent;

        MouseFilter = MouseFilterEnum.Stop;

        _graph = new Rect2(ReadoutWidth + Border, Border, PanelWidth - ReadoutWidth - Border * 2, PanelHeight - Border * 2);

        float altitude = HudTheme.Large * HudTheme.NumeralCap + Lead;
        float speed = HudTheme.Body * HudTheme.NumeralCap + Lead;

        float top = (PanelHeight - altitude - speed) * 0.5f;
        float width = ReadoutWidth - Border;

        _altitude = Counter(HudTheme.NumeralStrong, HudTheme.Large, HudTheme.Ink, new Rect2(Border, top, width, altitude));
        _speed = Counter(HudTheme.Numeral, HudTheme.Body, HudTheme.Dim, new Rect2(Border, top + altitude, width, speed));

    }

    public void Sync(Flight flight) {

        _flight = flight;

        double speed = flight.Vessel.Velocity.Length;

        _altitude.Set(flight.Altitude, Hud.Distance(flight.Altitude));
        _speed.Set(speed, Hud.Speed(speed));

        Sample();

        QueueRedraw();

    }

    private void Sample() {

        Orbit orbit = _flight.Orbit;

        double radius = _flight.Body.Radius;

        // A conic that ends in the ground ends the graph with it. Run out to the full period
        // instead and the whole descent is a spike in the corner of a box of flat ground.
        double window = orbit.IsClosed ? orbit.Period : OpenWindow;

        if (MapPath.Crossing(orbit, radius, out double fall)) {

            double ahead = orbit.TimeToTrueAnomaly(_flight.Time, fall);

            if (!double.IsNaN(ahead)) {

                window = Math.Min(window, Math.Max(ahead, Shortest) / (1.0 - Behind));

            }

        }

        double start = _flight.Time - window * Behind;

        _now = (int)Math.Round(Samples * Behind);

        double low = double.MaxValue;
        double high = double.MinValue;

        for (int index = 0; index <= Samples; index++) {

            double when = start + window * index / Samples;

            double altitude = orbit.StateAt(when).Position.Length - radius;

            _curve[index] = new Vector2(index, (float)altitude);

            low = Math.Min(low, altitude);
            high = Math.Max(high, altitude);

        }

        // Measured from the ground rather than from the lowest point on the conic. A circular orbit
        // has no spread to scale to, and against its own range it would draw as an empty box with a
        // line through it; against the surface it draws as the standoff it actually is.
        _low = 0.0;
        _high = Math.Max(high, low) * 1.20;

    }

    public override void _Draw() {

        if (_flight == null) {

            return;

        }

        DrawStyleBox(HudTheme.Panel(0.0f), new Rect2(Vector2.Zero, new Vector2(PanelWidth, PanelHeight)));

        DrawLine(new Vector2(ReadoutWidth + 0.5f, Border), new Vector2(ReadoutWidth + 0.5f, PanelHeight - Border), HudTheme.Edge, 1.0f);

        DrawAir();
        DrawCurve();
        DrawChips();

    }

    private TickReadout Counter(Font font, int size, Color colour, Rect2 box) {

        TickReadout counter = new TickReadout { Position = box.Position, Size = box.Size };

        AddChild(counter);

        counter.Dress(font, size, colour, HorizontalAlignment.Center);

        return counter;

    }

    /// <summary>The air, as the band of the graph it actually occupies. A trace that dips into it
    /// is a trace that is about to stop being a conic, and that is worth seeing before it happens.</summary>
    private void DrawAir() {

        double top = _flight.Body.AtmosphereTop;

        if (top <= 0.0 || top >= _high) {

            return;

        }

        float y = Height(top);

        DrawRect(new Rect2(_graph.Position.X, y, _graph.Size.X, _graph.End.Y - y), HudTheme.Well);

        DrawLine(new Vector2(_graph.Position.X, y), new Vector2(_graph.End.X, y), HudTheme.Edge, 1.0f);

    }

    private void DrawCurve() {

        Vector2[] flown = new Vector2[_now + 1];
        Vector2[] ahead = new Vector2[Samples - _now + 1];

        Vector2[] under = new Vector2[Samples + 3];

        for (int index = 0; index <= Samples; index++) {

            Vector2 point = new Vector2(_graph.Position.X + _graph.Size.X * index / Samples, Height(_curve[index].Y));

            if (index <= _now) {

                flown[index] = point;

            }

            if (index >= _now) {

                ahead[index - _now] = point;

            }

            under[index] = point;

        }

        under[Samples + 1] = new Vector2(_graph.End.X, _graph.End.Y);
        under[Samples + 2] = new Vector2(_graph.Position.X, _graph.End.Y);

        // The floor of the graph is the ground. A conic that reaches it colours the whole trace,
        // which says the same thing as a rule along the bottom without drawing over the border.
        Color ink = _flight.Orbit.PeriapsisRadius < _flight.Body.Radius ? HudTheme.Caution : HudTheme.Ink;

        DrawColoredPolygon(under, ink * new Color(1.0f, 1.0f, 1.0f, 0.17f));

        DrawLine(ahead[0], new Vector2(ahead[0].X, _graph.End.Y), HudTheme.Ink * new Color(1.0f, 1.0f, 1.0f, 0.40f), 1.0f);

        DrawPolyline(flown, ink * new Color(1.0f, 1.0f, 1.0f, 0.30f), 1.4f, true);
        DrawPolyline(ahead, ink * new Color(1.0f, 1.0f, 1.0f, 0.88f), 1.4f, true);

        DrawCircle(ahead[0], 3.0f, ink);

    }

    private void DrawChips() {

        float x = 0.0f;

        if (_flight.WarpStep > 0) {

            x += Chip(x, $"WARP {_flight.Warp:N0}×", HudTheme.Ink);

        }

        Maneuver node = _flight.Node;

        if (node != null && !node.IsEmpty) {

            double ignition = _flight.TimeToIgnition;

            string countdown = ignition > 0.0 ? $"T–{Hud.Clock(ignition)}" : "BURN";

            x += Chip(x, $"NODE {node.DeltaV:N0} m/s", HudTheme.Dim);

            Chip(x, countdown, ignition > 0.0 ? HudTheme.Dim : HudTheme.Ink);

        }

    }

    private float Chip(float x, string text, Color ink) {

        float width = HudTheme.Width(HudTheme.Strong, HudTheme.Tiny, text) + 16.0f;

        Rect2 box = new Rect2(x, PanelHeight + 6.0f, width, 18.0f);

        DrawStyleBox(HudTheme.Panel(0.0f), box);

        HudTheme.WriteIn(this, HudTheme.Strong, HudTheme.Tiny, box.Grow(-8.0f), text, ink, HorizontalAlignment.Left);

        return width + 6.0f;

    }

    private float Height(double altitude) {

        double span = _high - _low;

        float fraction = span > 0.0 ? (float)((altitude - _low) / span) : 0.5f;

        return _graph.End.Y - _graph.Size.Y * Mathf.Clamp(fraction, 0.0f, 1.0f);

    }

}
