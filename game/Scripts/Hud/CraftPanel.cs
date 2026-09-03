using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The vehicle in section. Every outline is a run of stations - the mould line for the
/// structure, the part's own profile for anything bolted to it - so a hull nobody has seen before
/// draws itself without this panel knowing what it is looking at.</summary>
public sealed partial class CraftPanel : Control {

    private const float PanelHeight = 200.0f;
    private const float PanelWidth = 72.0f;

    private const float Pad = 10.0f;

    private const int ColumnStations = 28;

    // Dark enough to sit against the planet, light enough to read as hardware rather than a hole.
    private static readonly Color Skin = new Color(0.114f, 0.141f, 0.176f, 0.88f);

    private sealed class Piece {

        public Part Part;

        public Rect2 Bounds;

        /// <summary>One closed outline per copy: one on the axis, or a pair for a ring.</summary>
        public Vector2[][] Outlines;

    }

    private Vessel _vessel;
    private Popover _popover;

    private readonly List<Piece> _pieces = new List<Piece>();
    private readonly List<(float Depth, float Half)> _bands = new List<(float, float)>();

    private Vector2[] _shell;
    private Vector2[] _wall;

    private float _scale;
    private float _axis;
    private float _top;
    private double _tip;

    private Part _hovered;
    private Part _selected;

    public void Build(Vessel vessel, Popover popover) {

        _vessel = vessel;
        _popover = popover;

        CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);
        Size = new Vector2(PanelWidth, PanelHeight);

        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;

        Hull hull = vessel.Hull;

        double low = hull.Base;
        double high = hull.Tip;
        double wide = hull.MaxRadius;

        foreach (Part part in vessel.Parts) {

            low = Math.Min(low, part.Bottom);
            high = Math.Max(high, part.Top);

            wide = Math.Max(wide, part.RingRadius + part.Extent);

        }

        _scale = (float)Math.Min((PanelHeight - Pad * 2.0f) / (high - low), (PanelWidth * 0.5f - Pad) / wide);

        _axis = PanelWidth * 0.5f;
        _top = (PanelHeight - (float)(high - low) * _scale) * 0.5f;
        _tip = high;

        _shell = Revolve(Inset(hull, 0.0), 0.0f);
        _wall = Revolve(Inset(hull, hull.WallThickness), 0.0f);

        BuildBands(hull);

        // Hardware bolted to the hull is picked before the run of hull behind it, so a click on a
        // thruster port never lands on the tank it is cut into.
        foreach (Part part in vessel.Parts) {

            if (!part.IsMouldLine) {

                Register(part);

            }

        }

        foreach (Part part in vessel.Parts) {

            if (part.IsMouldLine) {

                Register(part);

            }

        }

    }

    /// <summary>The mould line as stations, optionally taken in by a wall thickness.</summary>
    private static Hull.Station[] Inset(Hull hull, double wall) {

        Hull.Station[] stations = new Hull.Station[hull.Stations.Count];

        for (int index = 0; index < stations.Length; index++) {

            Hull.Station station = hull.Stations[index];

            stations[index] = new Hull.Station(station.Z, Math.Max(station.Radius - wall, 0.0));

        }

        return stations;

    }

    /// <summary>Turns a run of stations into a closed outline about an axis. This is the one
    /// operation the whole diagram is built from, which is why nothing in it is shape-specific.</summary>
    private Vector2[] Revolve(IReadOnlyList<Hull.Station> stations, float offset) {

        int count = stations.Count;

        Vector2[] outline = new Vector2[count * 2 + 1];

        for (int index = 0; index < count; index++) {

            Hull.Station station = stations[index];

            float y = Depth(station.Z);
            float half = (float)station.Radius * _scale;

            outline[index] = new Vector2(_axis + offset + half, y);
            outline[count * 2 - 1 - index] = new Vector2(_axis + offset - half, y);

        }

        outline[count * 2] = outline[0];

        return outline;

    }

    // Every proud ring on the mould line is a run of stations standing out from the wall on both
    // sides of it. Read off the profile rather than listed, so any hull draws its own hardware.
    private void BuildBands(Hull hull) {

        _bands.Clear();

        for (int index = 1; index < hull.Stations.Count - 1; index++) {

            double radius = hull.Stations[index].Radius;

            if (radius <= hull.Stations[index - 1].Radius) {

                continue;

            }

            int last = index;

            while (last + 1 < hull.Stations.Count && hull.Stations[last + 1].Radius >= radius) {

                last++;

            }

            if (last + 1 >= hull.Stations.Count) {

                break;

            }

            double centre = (hull.Stations[index].Z + hull.Stations[last].Z) * 0.5;

            _bands.Add((Depth(centre), (float)radius * _scale));

            index = last;

        }

    }

    private void Register(Part part) {

        Piece piece = new Piece { Part = part };

        float half;

        if (part.IsMouldLine) {

            half = (float)Widest(part) * _scale;

        }
        else {

            // A ring shows two of itself side on whatever the count is; a part on the axis shows one.
            float offset = (float)part.RingRadius * _scale;

            piece.Outlines = offset > 0.0f
                ? new[] { Revolve(part.Profile, -offset), Revolve(part.Profile, offset) }
                : new[] { Revolve(part.Profile, 0.0f) };

            half = offset + (float)part.Extent * _scale;

        }

        float top = Depth(part.Top);
        float bottom = Depth(part.Bottom);

        piece.Bounds = new Rect2(_axis - half, top, half * 2.0f, bottom - top);

        _pieces.Add(piece);

    }

    /// <summary>Widest the mould line gets over a part's run, so its hit box matches what is drawn.</summary>
    private double Widest(Part part) {

        double widest = 0.0;

        for (int step = 0; step <= 12; step++) {

            widest = Math.Max(widest, _vessel.Hull.RadiusAt(part.Bottom + part.Length * step / 12.0));

        }

        return widest;

    }

    public void Sync() {

        QueueRedraw();

    }

    public override void _GuiInput(InputEvent @event) {

        if (@event is InputEventMouseMotion motion) {

            Part hovered = PartAt(motion.Position);

            if (hovered != _hovered) {

                _hovered = hovered;

                QueueRedraw();

            }

            return;

        }

        if (@event is not InputEventMouseButton button || !button.Pressed || button.ButtonIndex != MouseButton.Left) {

            return;

        }

        Select(PartAt(button.Position));

        AcceptEvent();

    }

    public override void _Notification(int what) {

        if (what == NotificationMouseExit && _hovered != null) {

            _hovered = null;

            QueueRedraw();

        }

    }

    private void Select(Part part) {

        _selected = part != null && _popover.Shows(part) ? null : part;

        if (_selected == null) {

            _popover.Dismiss();

            return;

        }

        Part shown = _selected;

        Rect2 box = Find(shown).Bounds;

        _popover.Raise(shown, shown.Name, (rows, actions) => Read(shown, rows, actions), new Vector2(GlobalPosition.X - Pad, GlobalPosition.Y + box.GetCenter().Y));

    }

    public override void _Draw() {

        if (_vessel == null) {

            return;

        }

        DrawColoredPolygon(_shell, Skin);

        foreach (Piece piece in _pieces) {

            Wash(piece);

        }

        Column();

        DrawPolyline(_wall, HudTheme.Dim * new Color(1.0f, 1.0f, 1.0f, 0.30f), 1.0f, true);

        Seams();
        Bands();

        DrawPolyline(_shell, HudTheme.Ink * new Color(1.0f, 1.0f, 1.0f, 0.72f), 1.4f, true);

        foreach (Piece piece in _pieces) {

            Hardware(piece);

        }

    }

    /// <summary>What is in the tank, drawn where it sits: oxidiser standing on the floor, fuel on
    /// the bulkhead above it, both to the surface the fill height puts them at.</summary>
    private void Column() {

        Hull hull = _vessel.Hull;

        if (_vessel.PropellantCapacity <= 0.0 || _vessel.PropellantMass <= 0.0 || hull.TankVolume <= 0.0) {

            return;

        }

        double surface = hull.FillHeight(_vessel.PropellantMass / _vessel.PropellantCapacity);
        double bulkhead = hull.FillHeight(_vessel.OxidiserVolume / hull.TankVolume);

        Band(hull, hull.TankBottom, bulkhead, HudTheme.Oxidiser);
        Band(hull, bulkhead, surface, HudTheme.Fuel);

        float y = Depth(bulkhead);
        float half = Inner(hull, bulkhead);

        DrawLine(new Vector2(_axis - half, y), new Vector2(_axis + half, y), HudTheme.Edge, 1.0f);

    }

    private void Band(Hull hull, double low, double high, Color ink) {

        if (high <= low) {

            return;

        }

        Vector2[] column = new Vector2[ColumnStations * 2];

        for (int index = 0; index < ColumnStations; index++) {

            double z = low + (high - low) * index / (ColumnStations - 1.0);

            float y = Depth(z);
            float half = Inner(hull, z);

            column[index] = new Vector2(_axis + half, y);
            column[ColumnStations * 2 - 1 - index] = new Vector2(_axis - half, y);

        }

        DrawColoredPolygon(column, ink * new Color(1.0f, 1.0f, 1.0f, 0.30f));

    }

    private float Inner(Hull hull, double z) => (float)Math.Max(hull.RadiusAt(z) - hull.WallThickness, 0.0) * _scale;

    /// <summary>Washes the run a part occupies, so pointing at it says which piece it is.</summary>
    private void Wash(Piece piece) {

        if (piece.Part != _hovered && piece.Part != _selected) {

            return;

        }

        Color ink = piece.Part == _selected ? HudTheme.Ink : HudTheme.Dim;

        DrawRect(piece.Bounds, ink * new Color(1.0f, 1.0f, 1.0f, piece.Part == _selected ? 0.16f : 0.09f));

        DrawLine(piece.Bounds.Position, new Vector2(piece.Bounds.End.X, piece.Bounds.Position.Y), ink * new Color(1.0f, 1.0f, 1.0f, 0.5f), 1.0f);
        DrawLine(new Vector2(piece.Bounds.Position.X, piece.Bounds.End.Y), piece.Bounds.End, ink * new Color(1.0f, 1.0f, 1.0f, 0.5f), 1.0f);

    }

    private void Bands() {

        foreach ((float depth, float half) in _bands) {

            DrawLine(new Vector2(_axis - half, depth), new Vector2(_axis + half, depth), HudTheme.Ink * new Color(1.0f, 1.0f, 1.0f, 0.34f), 1.6f);

        }

    }

    /// <summary>Where one part hands over to the next. Without them the stage is a single outline.</summary>
    private void Seams() {

        foreach (Piece piece in _pieces) {

            if (!piece.Part.IsMouldLine) {

                continue;

            }

            float y = Depth(piece.Part.Top);

            if (y < _top) {

                continue;

            }

            float half = (float)_vessel.Hull.RadiusAt(piece.Part.Top) * _scale;

            DrawLine(new Vector2(_axis - half, y), new Vector2(_axis + half, y), HudTheme.Dim * new Color(1.0f, 1.0f, 1.0f, 0.45f), 1.0f, true);

        }

    }

    private void Hardware(Piece piece) {

        if (piece.Outlines == null) {

            return;

        }

        bool live = piece.Part.Kind switch {

            PartKind.Engine => _vessel.CurrentThrust > 0.0,
            PartKind.Thruster => _vessel.HasRcs,

            _ => false,

        };

        Color ink = piece.Part == _selected || piece.Part == _hovered ? HudTheme.Ink : live ? HudTheme.Dim : HudTheme.Faint;

        foreach (Vector2[] outline in piece.Outlines) {

            DrawColoredPolygon(outline, Skin);
            DrawPolyline(outline, ink, 1.3f, true);

        }

    }

    private Piece Find(Part part) {

        foreach (Piece piece in _pieces) {

            if (piece.Part == part) {

                return piece;

            }

        }

        return null;

    }

    private Part PartAt(Vector2 point) {

        foreach (Piece piece in _pieces) {

            if (piece.Bounds.Grow(2.0f).HasPoint(point)) {

                return piece.Part;

            }

        }

        return null;

    }

    /// <summary>What one part is doing. Only figures the sim actually carries, and only actions the
    /// part genuinely has authority over.</summary>
    private void Read(Part part, List<(string Label, string Value)> rows, List<(string Label, Action Run)> actions) {

        Vessel vessel = _vessel;

        switch (part.Kind) {

            case PartKind.Engine:

                rows.Add(("THRUST", $"{vessel.CurrentThrust / 1000.0:F1} / {vessel.ThrustNewtons / 1000.0:F0} kN"));
                rows.Add(("IMPULSE", $"{vessel.SpecificImpulse:F0} s"));
                rows.Add(("LIT", $"{vessel.EnginesLit} / {vessel.EngineCount}"));
                rows.Add(("FLOW", $"{vessel.CurrentMassFlow:F2} kg/s"));
                rows.Add(("BELL", $"{part.Extent * 2.0:F2} m"));

                actions.Add(("CUT", () => vessel.Throttle = 0.0));
                actions.Add(("FULL", () => vessel.Throttle = 1.0));

                break;

            case PartKind.Tank:

                rows.Add(("LOAD", $"{vessel.PropellantMass / 1000.0:F2} / {vessel.PropellantCapacity / 1000.0:F2} t"));
                rows.Add(("VOLUME", $"{vessel.Hull.TankVolume:F2} m³"));
                rows.Add(("MIXTURE", $"{vessel.MixtureRatio:F2} : 1"));
                rows.Add(("ULLAGE", $"{Ullage(vessel) * 100.0:F0} %"));
                rows.Add(("DELTA-V", $"{vessel.DeltaV:N0} m/s"));

                break;

            case PartKind.Thruster:

                rows.Add(("STATUS", vessel.HasRcs ? "ARMED" : vessel.RcsEnabled ? "DRY" : "SAFE"));
                rows.Add(("MONOPROP", $"{vessel.RcsPropellantMass:F0} / {vessel.RcsPropellantCapacity:F0} kg"));
                rows.Add(("CLUSTER", $"{vessel.RcsThrustNewtons:N0} N"));
                rows.Add(("TORQUE", $"{vessel.ControlTorqueLimit / 1000.0:F1} kN·m"));
                rows.Add(("DUTY", $"{vessel.RcsDuty * 100.0:F0} %"));

                actions.Add((vessel.RcsEnabled ? "SAFE" : "ARM", () => vessel.RcsEnabled = !vessel.RcsEnabled));

                break;

            default:

                rows.Add(("MASS", $"{Share(part):N0} kg"));
                rows.Add(("SPAN", $"{part.Length:F2} m"));
                rows.Add(("DIAMETER", $"{vessel.Hull.RadiusAt(part.Centre) * 2.0:F2} m"));

                break;

        }

    }

    /// <summary>Share of the tank the propellant has left behind, which is what the diagram draws
    /// above the surface line.</summary>
    private static double Ullage(Vessel vessel) {

        return vessel.PropellantCapacity > 0.0 ? 1.0 - vessel.PropellantMass / vessel.PropellantCapacity : 1.0;

    }

    // Dry mass is modelled as a shell of one areal density, so a run of the mould line carries
    // exactly its share of the swept area. Nothing here is invented; it falls out of that model.
    private double Share(Part part) {

        Hull hull = _vessel.Hull;

        double total = hull.ShellArea(hull.Base, hull.Tip);

        return total > 0.0 ? _vessel.DryMass * hull.ShellArea(part.Bottom, part.Top) / total : 0.0;

    }

    private float Depth(double z) => _top + (float)(_tip - z) * _scale;

}
