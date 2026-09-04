using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The vehicle in section, a stage at a time. Every outline is a run of stations - the
/// mould line for the structure, the part's own profile for anything bolted to it - so a stack
/// nobody has seen before draws itself without this panel knowing what it is looking at.</summary>
public sealed partial class CraftPanel : Control {

    // The tallest the diagram is ever drawn. What it actually takes is what the stack needs,
    // so a capsule on its own does not sit in a column of empty box.
    private const float DiagramCeiling = 200.0f;

    // Wide enough that a squat vehicle is limited by the box's height rather than by its width.
    // A capsule is twice as wide as it is tall, and in a narrow cell it draws as a smear.
    private const float PanelWidth = 104.0f;

    private const float ControlHeight = 24.0f;
    private const float ControlGap = 8.0f;

    private const float Pad = 8.0f;

    private const int ColumnStations = 28;

    // Dark enough to sit against the planet, light enough to read as hardware rather than a hole.
    private static readonly Color Skin = new Color(0.114f, 0.141f, 0.176f, 0.88f);

    private sealed class Piece {

        public Stage Stage;
        public Part Part;

        public Rect2 Bounds;

        /// <summary>One closed outline per copy: one on the axis, or a pair for a ring.</summary>
        public Vector2[][] Outlines;

    }

    /// <summary>One stage's own drawing: its mould line, the wall inside it, and the proud rings
    /// the profile itself declares.</summary>
    private sealed class Section {

        public Stage Stage;

        public Vector2[] Shell;
        public Vector2[] Wall;

        /// <summary>The run of the mould line a shield covers, as its own closed outline.</summary>
        public Vector2[] Shield;

        public readonly List<(float Depth, float Half)> Bands = new List<(float, float)>();

    }

    private Flight _flight;
    private Popover _popover;

    private Button _stage;

    private readonly List<Piece> _pieces = new List<Piece>();
    private readonly List<Section> _sections = new List<Section>();

    private float _scale;
    private float _axis;
    private float _top;
    private double _tip;

    private float _diagram;

    private Part _hovered;
    private Part _selected;

    public void Build(Flight flight, Popover popover) {

        _flight = flight;
        _popover = popover;

        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;

        _stage = HudTheme.Button("STAGE", new Vector2(PanelWidth, ControlHeight));

        _stage.Size = new Vector2(PanelWidth, ControlHeight);

        _stage.Pressed += () => _flight.Separate();

        AddChild(_stage);

        flight.Staged += _ => Lay();
        flight.VesselChanged += (_, _) => Lay();

        Lay();

    }

    /// <summary>Fits the stack to the cell and rebuilds every outline in it. Run again whenever the
    /// stack changes shape, which is the whole of what staging does to this panel.</summary>
    private void Lay() {

        _pieces.Clear();
        _sections.Clear();

        _hovered = null;
        _selected = null;

        Vessel vessel = _flight.Vessel;

        double low = vessel.Base;
        double high = vessel.Tip;
        double wide = 0.0;

        foreach (Stage stage in vessel.Stages) {

            wide = Math.Max(wide, stage.Hull.MaxRadius);

            foreach (Part part in stage.Parts) {

                low = Math.Min(low, part.Bottom);
                high = Math.Max(high, part.Top);

                wide = Math.Max(wide, part.RingRadius + part.Extent);

            }

        }

        _scale = (float)Math.Min((DiagramCeiling - Pad * 2.0f) / (high - low), (PanelWidth * 0.5f - Pad) / wide);

        // The box is cut to the vehicle rather than the vehicle floated in a fixed box, so what the
        // panel takes off the screen says something about what is left of the stack.
        _diagram = (float)(high - low) * _scale + Pad * 2.0f;

        _axis = PanelWidth * 0.5f;
        _top = Pad;
        _tip = high;

        CustomMinimumSize = new Vector2(PanelWidth, _diagram + ControlGap + ControlHeight);
        Size = CustomMinimumSize;

        _stage.Position = new Vector2(0.0f, _diagram + ControlGap);

        foreach (Stage stage in vessel.Stages) {

            Section section = new Section {

                Stage = stage,

                Shell = Revolve(Inset(stage.Hull, 0.0), 0.0f),
                Wall = Revolve(Inset(stage.Hull, stage.Hull.WallThickness), 0.0f),

                Shield = Ablator(stage),

            };

            BuildBands(section);

            _sections.Add(section);

            // Hardware bolted to the hull is picked before the run of hull behind it, so a click on a
            // thruster port never lands on the tank it is cut into.
            foreach (Part part in stage.Parts) {

                if (!part.IsMouldLine) {

                    Register(stage, part);

                }

            }

        }

        foreach (Stage stage in vessel.Stages) {

            foreach (Part part in stage.Parts) {

                if (part.IsMouldLine) {

                    Register(stage, part);

                }

            }

        }

    }

    /// <summary>The shield's own run of the mould line, closed off at the top. Drawn solid, it is
    /// what makes a capsule read as a capsule in section rather than as a cone.</summary>
    private Vector2[] Ablator(Stage stage) {

        Part shield = null;

        foreach (Part part in stage.Parts) {

            if (part.Kind == PartKind.Shield) {

                shield = part;

            }

        }

        if (shield == null) {

            return null;

        }

        List<Hull.Station> run = new List<Hull.Station>();

        foreach (Hull.Station station in stage.Hull.Stations) {

            if (station.Z >= shield.Bottom && station.Z <= shield.Top) {

                run.Add(station);

            }

        }

        if (run.Count < 2) {

            return null;

        }

        run.Add(new Hull.Station(shield.Top, stage.Hull.RadiusAt(shield.Top)));

        return Revolve(run, 0.0f);

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
    private void BuildBands(Section section) {

        Hull hull = section.Stage.Hull;

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

            section.Bands.Add((Depth(centre), (float)radius * _scale));

            index = last;

        }

    }

    private void Register(Stage stage, Part part) {

        Piece piece = new Piece { Stage = stage, Part = part };

        float half;

        if (part.IsMouldLine) {

            half = (float)Widest(stage, part) * _scale;

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
    private static double Widest(Stage stage, Part part) {

        double widest = 0.0;

        for (int step = 0; step <= 12; step++) {

            widest = Math.Max(widest, stage.Hull.RadiusAt(part.Bottom + part.Length * step / 12.0));

        }

        return widest;

    }

    public void Sync() {

        HudTheme.Light(_stage, false);

        _stage.Disabled = !_flight.Vessel.CanSeparate;

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

        Piece shown = Find(_selected);

        _popover.Raise(shown.Part, shown.Part.Name, (rows, actions) => Read(shown, rows, actions), new Vector2(GlobalPosition.X - Pad, GlobalPosition.Y + shown.Bounds.GetCenter().Y));

    }

    public override void _Draw() {

        if (_flight == null) {

            return;

        }

        foreach (Section section in _sections) {

            DrawColoredPolygon(section.Shell, Skin);

        }

        foreach (Piece piece in _pieces) {

            Wash(piece);

        }

        foreach (Section section in _sections) {

            Column(section);

            DrawPolyline(section.Wall, HudTheme.Dim * new Color(1.0f, 1.0f, 1.0f, 0.30f), 1.0f, true);

            // The ablator is the one run of a hull that is solid rather than a shell, and drawing
            // it as one is what makes a capsule read as a capsule instead of as a cone.
            if (section.Shield != null) {

                DrawColoredPolygon(section.Shield, new Color(0.043f, 0.051f, 0.063f, 0.96f));
                DrawPolyline(section.Shield, HudTheme.Dim * new Color(1.0f, 1.0f, 1.0f, 0.55f), 1.0f, true);

            }

        }

        Seams();

        foreach (Section section in _sections) {

            Bands(section);

        }

        foreach (Section section in _sections) {

            // The live stage is the bright one; a payload riding above it is available, not active.
            bool live = section.Stage == _flight.Vessel.Active;

            DrawPolyline(section.Shell, HudTheme.Ink * new Color(1.0f, 1.0f, 1.0f, live ? 0.72f : 0.44f), live ? 1.4f : 1.2f, true);

        }

        Joints();

        foreach (Piece piece in _pieces) {

            Hardware(piece);

        }

    }

    /// <summary>What is in a stage's tank, drawn where it sits: oxidiser standing on the floor, fuel
    /// on the bulkhead above it, both to the surface the fill height puts them at.</summary>
    private void Column(Section section) {

        Stage stage = section.Stage;
        Hull hull = stage.Hull;

        if (stage.PropellantCapacity <= 0.0 || stage.PropellantMass <= 0.0 || hull.TankVolume <= 0.0) {

            return;

        }

        double surface = hull.FillHeight(stage.FillFraction);
        double bulkhead = hull.FillHeight(stage.OxidiserVolume / hull.TankVolume);

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

    private void Bands(Section section) {

        foreach ((float depth, float half) in section.Bands) {

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

            float half = (float)piece.Stage.Hull.RadiusAt(piece.Part.Top) * _scale;

            DrawLine(new Vector2(_axis - half, y), new Vector2(_axis + half, y), HudTheme.Dim * new Color(1.0f, 1.0f, 1.0f, 0.45f), 1.0f, true);

        }

    }

    /// <summary>Where one stage lets go of the next. A heavier rule than a seam, with the tick that
    /// says which way the stack comes apart.</summary>
    private void Joints() {

        for (int index = 1; index < _sections.Count; index++) {

            Hull hull = _sections[index].Stage.Hull;

            float y = Depth(hull.Base);
            float half = (float)_sections[index - 1].Stage.Hull.MaxRadius * _scale + 3.0f;

            Color ink = HudTheme.Ink * new Color(1.0f, 1.0f, 1.0f, 0.55f);

            DrawLine(new Vector2(_axis - half, y), new Vector2(_axis + half, y), ink, 1.0f);

            DrawLine(new Vector2(_axis - half, y), new Vector2(_axis - half, y + 4.0f), ink, 1.0f);
            DrawLine(new Vector2(_axis + half, y), new Vector2(_axis + half, y + 4.0f), ink, 1.0f);

        }

    }

    private void Hardware(Piece piece) {

        if (piece.Outlines == null) {

            return;

        }

        bool live = piece.Part.Kind switch {

            PartKind.Engine => piece.Stage == _flight.Vessel.Active && _flight.Vessel.CurrentThrust > 0.0,
            PartKind.Thruster => _flight.Vessel.RcsEnabled && piece.Stage.HasReactionControl,

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
    private void Read(Piece piece, List<(string Label, string Value)> rows, List<(string Label, Action Run)> actions) {

        Vessel vessel = _flight.Vessel;

        Stage stage = piece.Stage;
        Part part = piece.Part;

        bool live = stage == vessel.Active;

        switch (part.Kind) {

            case PartKind.Engine:

                rows.Add(("THRUST", $"{(live ? vessel.CurrentThrust : 0.0) / 1000.0:F1} / {stage.ThrustNewtons / 1000.0:F0} kN"));
                rows.Add(("IMPULSE", $"{stage.SpecificImpulse:F0} s"));
                rows.Add(("LIT", $"{stage.EnginesLit} / {stage.EngineCount}"));
                rows.Add(("FLOW", $"{(live ? vessel.CurrentMassFlow : 0.0):F2} kg/s"));
                rows.Add(("BELL", $"{part.Extent * 2.0:F2} m"));

                if (live) {

                    actions.Add(("CUT", () => vessel.Throttle = 0.0));
                    actions.Add(("FULL", () => vessel.Throttle = 1.0));

                }

                break;

            case PartKind.Tank:

                rows.Add(("LOAD", $"{stage.PropellantMass / 1000.0:F2} / {stage.PropellantCapacity / 1000.0:F2} t"));
                rows.Add(("VOLUME", $"{stage.Hull.TankVolume:F2} m³"));
                rows.Add(("MIXTURE", $"{stage.MixtureRatio:F2} : 1"));
                rows.Add(("ULLAGE", $"{(1.0 - stage.FillFraction) * 100.0:F0} %"));
                rows.Add(("DELTA-V", $"{(live ? vessel.DeltaV : 0.0):N0} m/s"));

                break;

            case PartKind.Thruster:

                rows.Add(("STATUS", stage.HasReactionControl ? vessel.RcsEnabled ? "ARMED" : "SAFE" : "DRY"));
                rows.Add(("MONOPROP", $"{stage.RcsPropellantMass:F0} / {stage.RcsPropellantCapacity:F0} kg"));
                rows.Add(("CLUSTER", $"{stage.RcsThrustNewtons:N0} N"));
                rows.Add(("TORQUE", $"{stage.ControlTorque / 1000.0:F1} kN·m"));
                rows.Add(("DUTY", $"{vessel.RcsDuty * 100.0:F0} %"));

                actions.Add((vessel.RcsEnabled ? "SAFE" : "ARM", () => vessel.RcsEnabled = !vessel.RcsEnabled));

                break;

            case PartKind.Shield:

                rows.Add(("SKIN", $"{vessel.SkinTemperature:N0} K"));
                rows.Add(("LIMIT", $"{stage.HeatLimit:N0} K"));
                rows.Add(("FLUX", $"{vessel.Aero.HeatFlux / 1000.0:N0} kW/m²"));
                rows.Add(("BLUNTNESS", $"{vessel.Profile.BaseCurvature:F2} m"));
                rows.Add(("MASS", $"{stage.Ballast.Mass:N0} kg"));

                break;

            default:

                rows.Add(("MASS", $"{Share(stage, part):N0} kg"));
                rows.Add(("SPAN", $"{part.Length:F2} m"));
                rows.Add(("DIAMETER", $"{stage.Hull.RadiusAt(part.Centre) * 2.0:F2} m"));

                break;

        }

    }

    // Dry mass is modelled as a shell of one areal density, so a run of the mould line carries
    // exactly its share of the swept area. Nothing here is invented; it falls out of that model.
    private static double Share(Stage stage, Part part) {

        Hull hull = stage.Hull;

        double total = hull.ShellArea(hull.Base, hull.Tip);

        return total > 0.0 ? stage.ShellMass * hull.ShellArea(part.Bottom, part.Top) / total : 0.0;

    }

    private float Depth(double z) => _top + (float)(_tip - z) * _scale;

}
