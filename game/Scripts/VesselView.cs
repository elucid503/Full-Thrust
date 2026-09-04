using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>A flying body, drawn a stage at a time. Every stage is lathed off its own mould line
/// and hung in the stack's own coordinates, so when one comes off its geometry is handed to a view
/// of its own without a single vertex being rebuilt.</summary>
public sealed partial class VesselView : Node3D {

    private const int RadialSegments = 96;
    private const int NozzleSegments = 32;

    // Facets meeting at less than this weld into one smooth surface. The proud rings and the tail
    // rim all turn harder than it, so they keep their edge while the ogive stays smooth.
    private const float SmoothAngle = 20.0f;

    // A whole number of tiles has to wrap the body or the texture shows a seam down one side.
    private const int TilesAround = 6;

    // Conical thrust structure: it carries the engine and is also what closes the tail, so nothing
    // behind it is ever drawn.
    private const float MountRadius = 0.40f;

    // Clearance between the deck the engine hangs off and the top of the engine itself.
    private const float DeckClearance = 0.06f;

    // How far a bulkhead sits inside an open end that has no engine behind it to place one.
    private const float BulkheadInset = 0.30f;

    private const int RcsHalfSteps = 3;
    private const float RcsCant = 0.50f;

    // A canted bell reaches further up the pocket than its own length, and further in than its own
    // depth: the pocket is cut to clear both, and the pair sits close enough that neither mouth
    // cuts back out through the sill or the lintel.
    private const float RcsOffset = 0.085f;

    // The port radius the nozzle mesh below is drawn at; anything else scales off it.
    private const float NozzleGauge = 0.18f;

    // Where the mounting plate and the bell mouth sit on the nozzle's own axis. The plate lands flat
    // on the pocket floor; standing the nozzle on its throat instead buries half of it in the plate.
    private const float NozzleBase = 0.126f;
    private const float NozzleReach = 0.135f;

    // Where the skin starts to glow and where it is running white. Between them the emission and
    // its colour both ramp, which is the whole of the heat readout the eye gets.
    private const float GlowFloor = 700.0f;
    private const float GlowCeiling = 1700.0f;

    public static VesselView Active { get; private set; }

    /// <summary>One stage's geometry, and the handful of things about it that have to be driven
    /// every frame. It travels intact from the stack's view to a view of its own.</summary>
    private sealed class Piece {

        public Stage Stage { get; init; }
        public Node3D Node { get; init; }

        public MeshInstance3D Plume { get; set; }
        public ShaderMaterial PlumeMaterial { get; set; }

        public float BellRadius { get; set; }

        public readonly List<Jet> Jets = new List<Jet>();
        public readonly List<OmniLight3D> Lights = new List<OmniLight3D>();
        public readonly List<StandardMaterial3D> Skins = new List<StandardMaterial3D>();

    }

    private readonly List<Piece> _pieces = new List<Piece>();

    private Vessel _vessel;

    private Node3D _body;

    private MeshInstance3D _sheath;
    private ShaderMaterial _sheathMaterial;

    private float _thrust;
    private float _sheathHeat;

    public Vessel Vessel => _vessel;

    /// <summary>Builds the whole stack. The primary view is the one the debug bridge tunes.</summary>
    public void Build(Vessel vessel, bool primary = true) {

        if (primary) {

            Active = this;

        }

        _vessel = vessel;

        _body = new Node3D { Name = "Body" };

        AddChild(_body);

        foreach (Stage stage in vessel.Stages) {

            Piece piece = BuildStage(stage);

            _body.AddChild(piece.Node);

            _pieces.Add(piece);

        }

        AttachSheath();

    }

    /// <summary>Hands one stage's geometry over to a view of its own, for the body that has just
    /// separated. Nothing is rebuilt, so the piece that flies away is the piece that was there.</summary>
    public VesselView Hand(Vessel debris) {

        Piece piece = Find(debris.Active);

        if (piece == null) {

            return null;

        }

        _pieces.Remove(piece);

        _body.RemoveChild(piece.Node);

        // What is left is a shorter stack, and the plasma has to wrap that one.
        BakeProfile();

        VesselView view = new VesselView { Name = debris.Name };

        view.Take(debris, piece);

        return view;

    }

    private void Take(Vessel vessel, Piece piece) {

        _vessel = vessel;

        _body = new Node3D { Name = "Body" };

        AddChild(_body);

        _body.AddChild(piece.Node);

        _pieces.Add(piece);

        AttachSheath();

    }

    private Piece Find(Stage stage) {

        foreach (Piece piece in _pieces) {

            if (piece.Stage == stage) {

                return piece;

            }

        }

        return null;

    }

    public void Sync(Vector3 point, Quaternion orientation) {

        Position = point;
        Basis = new Basis(orientation);

        // The mesh is built on the stack's own datum, so the offset that puts the centre of mass at
        // the origin is a figure that changes as the tank empties rather than one baked in at build.
        _body.Position = new Vector3(0.0f, -(float)_vessel.CentreOfMassZ, 0.0f);

        SyncPlume();
        SyncHeat();
        SyncSheath();

    }

    // Only the stage taking the flow gets hot, and only its skin lights up. A stack's upper stage
    // is in the wake of the one below it and stays the colour it was painted.
    private void SyncHeat() {

        Stage leading = _vessel.Leading;

        float temperature = (float)_vessel.SkinTemperature;

        float glow = Mathf.Clamp((temperature - GlowFloor) / (GlowCeiling - GlowFloor), 0.0f, 1.0f);

        // Emission climbs faster than the temperature does, the way a hot surface actually brightens.
        float energy = glow * glow * 7.0f;

        Color ink = new Color(0.75f, 0.12f, 0.02f).Lerp(new Color(1.0f, 0.72f, 0.42f), glow);

        foreach (Piece piece in _pieces) {

            bool hot = piece.Stage == leading && energy > 0.001f;

            foreach (StandardMaterial3D skin in piece.Skins) {

                skin.EmissionEnabled = hot;

                if (hot) {

                    skin.Emission = ink;
                    skin.EmissionEnergyMultiplier = energy;

                }

            }

        }

    }

    /// <summary>Live shader tuning from the debug bridge; scalars, or comma-separated colours.</summary>
    public bool Tune(string parameter, string value) {

        return Tune(_sheathMaterial, parameter, value) | Tune(PlumeMaterial(), parameter, value);

    }

    private ShaderMaterial PlumeMaterial() {

        foreach (Piece piece in _pieces) {

            if (piece.PlumeMaterial != null) {

                return piece.PlumeMaterial;

            }

        }

        return null;

    }

    private static bool Tune(ShaderMaterial material, string parameter, string value) {

        if (material == null) {

            return false;

        }

        string[] parts = value.Split(',');

        if (parts.Length == 3) {

            material.SetShaderParameter(parameter, new Color(parts[0].ToFloat(), parts[1].ToFloat(), parts[2].ToFloat()));

        }
        else {

            material.SetShaderParameter(parameter, value.ToFloat());

        }

        return true;

    }

    private static float TileMetres(Stage stage) => Mathf.Tau * (float)stage.Hull.MaxRadius / TilesAround;

    // Ports sit between the quadrant axes so none of them ever fires straight along a control axis
    // on its own; the window is a whole number of segments wide, so its edges land on the grid.
    private static int PortCentre(int count, int index) => RadialSegments / (count * 2) + RadialSegments / count * index;

    private Piece BuildStage(Stage stage) {

        Node3D node = new Node3D { Name = stage.Name };

        Piece piece = new Piece { Stage = stage, Node = node };

        // A stage with a model of its own is not lathed: the model is its outside, and the
        // hardware on it came with it rather than being bolted on afterwards.
        if (stage.Model != null) {

            Clad(node, piece, stage);
            using TriangleMesh surface = JetSurface(node);

            // The model brought its own thruster pods; only their exhaust is added here.
            foreach (Part part in stage.Parts) {

                if (part.Kind != PartKind.Thruster) {

                    continue;

                }

                foreach ((Vector3 position, Vector3 axis, Vector3 side, float scale) in Mounts(stage, part)) {

                    AttachJet(node, piece, position, axis, side, scale, surface);

                }

            }

        }
        else {

            node.AddChild(new MeshInstance3D {

                Name = "Hull",
                Mesh = BuildHullMesh(stage, piece),

                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,

            });

            foreach (Part part in stage.Parts) {

                if (part.Kind == PartKind.Thruster) {

                    AttachThrusters(node, piece, stage, part);

                }

            }

        }

        foreach (Part part in stage.Parts) {

            if (part.Kind == PartKind.Engine) {

                AttachEngine(node, piece, part);

            }

        }

        return piece;

    }

    /// <summary>Seats an imported model on the run its stage occupies and paints it. An import's
    /// own materials are whatever its author last had in a viewport, so none of them are kept: the
    /// shield and the backshell arrive as two surfaces and both are restated here.</summary>
    private static void Clad(Node3D node, Piece piece, Stage stage) {

        Node3D model = Fit(stage.Model, stage.Hull.Base, stage.Hull.Length);

        if (model == null) {

            return;

        }

        StandardMaterial3D[] coats = {

            // A cured ablator: brown-grey and matte. Dark enough to read as a shield against the
            // backshell, light enough that the whole base is not a hole in the picture - a
            // near-black surface under one sun and a star sky renders as a silhouette.
            Paint(new Color(0.285f, 0.245f, 0.212f), 0.0f, 0.88f),

            // The backshell is thermal panel over structure: brighter than the shield and cooler
            // than the tank's paint, so the capsule reads as its own vehicle either side of the
            // joint. Kept low-metallic for the same reason the tank is.
            Paint(new Color(0.74f, 0.755f, 0.78f), 0.14f, 0.42f),

        };

        // Ordered by where each surface actually sits on the model rather than by its index or its
        // name: an importer is free to reorder primitives, to split them across nodes - and then
        // every one of them is surface zero - and to drop the names they arrived with.
        List<(MeshInstance3D Mesh, int Index, float Height)> surfaces = new List<(MeshInstance3D, int, float)>();

        foreach (MeshInstance3D instance in Meshes(model)) {

            instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

            for (int index = 0; index < instance.Mesh.GetSurfaceCount(); index++) {

                surfaces.Add((instance, index, Seat(instance.Mesh, index)));

            }

        }

        surfaces.Sort((left, right) => left.Height.CompareTo(right.Height));

        for (int order = 0; order < surfaces.Count; order++) {

            StandardMaterial3D coat = coats[Math.Min(order, coats.Length - 1)];

            surfaces[order].Mesh.SetSurfaceOverrideMaterial(surfaces[order].Index, coat);

            piece.Skins.Add(coat);

        }

        node.AddChild(model);

    }

    /// <summary>Where a surface sits along the nose axis, as the mean of its own vertices.</summary>
    private static float Seat(Mesh mesh, int index) {

        Vector3[] points = mesh.SurfaceGetArrays(index)[(int)Mesh.ArrayType.Vertex].AsVector3Array();

        if (points.Length == 0) {

            return 0.0f;

        }

        float total = 0.0f;

        foreach (Vector3 point in points) {

            total += point.Y;

        }

        return total / points.Length;

    }

    private static IEnumerable<MeshInstance3D> Meshes(Node node) {

        if (node is MeshInstance3D mesh) {

            yield return mesh;

        }

        foreach (Node child in node.GetChildren()) {

            foreach (MeshInstance3D nested in Meshes(child)) {

                yield return nested;

            }

        }

    }

    /// <summary>Whether a station falls inside one of the windows cut for a recessed thruster.</summary>
    private static bool InsidePort(Stage stage, int step, float low, float high) {

        foreach (Part part in stage.Parts) {

            if (part.Kind != PartKind.Thruster || part.Depth <= 0.0) {

                continue;

            }

            if (high <= (float)part.Bottom || low >= (float)part.Top) {

                continue;

            }

            for (int index = 0; index < part.Count; index++) {

                int offset = step - PortCentre(part.Count, index);

                if (offset >= -RcsHalfSteps && offset < RcsHalfSteps) {

                    return true;

                }

            }

        }

        return false;

    }

    private ArrayMesh BuildHullMesh(Stage stage, Piece piece) {

        ArrayMesh mesh = new ArrayMesh();

        Hull hull = stage.Hull;

        Vector2[] profile = OuterProfile(stage);

        // A shield is a different material on the same mould line, so the skin is turned in two
        // runs split at the part's own top rather than being painted after the fact.
        Part shield = FindPart(stage, PartKind.Shield);

        if (shield != null) {

            Skin(mesh, stage, Slice(profile, float.NegativeInfinity, (float)shield.Top), "Shield", ShieldMaterial(), piece);
            Skin(mesh, stage, Slice(profile, (float)shield.Top, float.PositiveInfinity), "Skin", HullMaterial(new Color(0.97f, 0.972f, 0.975f), 0.15f, 1.0f), piece);

        }
        else {

            Skin(mesh, stage, profile, "Skin", HullMaterial(new Color(0.97f, 0.972f, 0.975f), 0.15f, 1.0f), piece);

        }

        Vector2[] core = InnerProfile(stage);

        if (core != null) {

            SurfaceTool inner = new SurfaceTool();

            inner.Begin(Mesh.PrimitiveType.Triangles);

            Revolve(inner, core, RadialSegments, false, TileMetres(stage), null);

            inner.GenerateTangents();

            Commit(mesh, inner, "Core", HullMaterial(new Color(0.27f, 0.27f, 0.278f), 0.40f, 1.0f), piece);

        }

        SurfaceTool ports = new SurfaceTool();

        ports.Begin(Mesh.PrimitiveType.Triangles);

        bool cut = false;

        foreach (Part part in stage.Parts) {

            if (part.Kind != PartKind.Thruster || part.Depth <= 0.0) {

                continue;

            }

            for (int index = 0; index < part.Count; index++) {

                Pocket(ports, PortCentre(part.Count, index), hull, part);

            }

            cut = true;

        }

        if (cut) {

            Commit(mesh, ports, "Ports", Paint(new Color(0.46f, 0.462f, 0.47f), 0.05f, 0.72f), piece);

        }

        return mesh;

    }

    private void Skin(ArrayMesh mesh, Stage stage, Vector2[] profile, string name, StandardMaterial3D material, Piece piece) {

        if (profile.Length < 2) {

            return;

        }

        SurfaceTool surface = new SurfaceTool();

        surface.Begin(Mesh.PrimitiveType.Triangles);

        Revolve(surface, profile, RadialSegments, true, TileMetres(stage), (step, low, high) => InsidePort(stage, step, low, high));

        surface.GenerateTangents();

        Commit(mesh, surface, name, material, piece);

    }

    private static Part FindPart(Stage stage, PartKind kind) {

        foreach (Part part in stage.Parts) {

            if (part.Kind == kind) {

                return part;

            }

        }

        return null;

    }

    private static Vector2[] Slice(Vector2[] profile, float low, float high) {

        List<Vector2> run = new List<Vector2>();

        foreach (Vector2 point in profile) {

            if (point.Y >= low - 1e-4f && point.Y <= high + 1e-4f) {

                run.Add(point);

            }

        }

        return run.ToArray();

    }

    private static void Commit(ArrayMesh mesh, SurfaceTool surface, string name, Material material, Piece piece) {

        surface.Commit(mesh);

        mesh.SurfaceSetName(mesh.GetSurfaceCount() - 1, name);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);

        if (material is StandardMaterial3D skin && piece != null) {

            piece.Skins.Add(skin);

        }

    }

    /// <summary>The mould line itself, with the port edges spliced in so the cutouts land on stations.</summary>
    private static Vector2[] OuterProfile(Stage stage) {

        Hull hull = stage.Hull;

        List<float> heights = new List<float>();

        foreach (Hull.Station station in hull.Stations) {

            heights.Add((float)station.Z);

        }

        foreach (Part part in stage.Parts) {

            if (part.Kind == PartKind.Thruster && part.Depth > 0.0) {

                heights.Add((float)part.Bottom);
                heights.Add((float)part.Top);

            }

            if (part.Kind == PartKind.Shield) {

                heights.Add((float)part.Top);

            }

        }

        heights.Sort();

        List<Vector2> profile = new List<Vector2>();

        foreach (float height in heights) {

            if (profile.Count > 0 && Mathf.Abs(profile[profile.Count - 1].Y - height) < 1e-4f) {

                continue;

            }

            profile.Add(new Vector2((float)hull.RadiusAt(height), height));

        }

        return profile.ToArray();

    }

    /// <summary>Closes an open end: a rim, a wall standing inside the mould line, and the deck the
    /// engine hangs off. A hull drawn as one surface has no wall at all - its open end reads as a
    /// razor edge and the whole stage looks like foil.</summary>
    private static Vector2[] InnerProfile(Stage stage) {

        Hull hull = stage.Hull;

        float baseRadius = (float)hull.RadiusAt(hull.Base);
        float tipRadius = (float)hull.RadiusAt(hull.Tip);

        List<Vector2> profile = new List<Vector2>();

        if (baseRadius > 0.01f) {

            float lining = baseRadius - (float)hull.WallThickness;

            Part engine = FindPart(stage, PartKind.Engine);

            float deck = engine != null ? (float)engine.Top + DeckClearance : (float)hull.Base + BulkheadInset;
            float run = engine != null ? (float)hull.TankBottom : deck;

            profile.Add(new Vector2(baseRadius, (float)hull.Base));
            profile.Add(new Vector2(lining, (float)hull.Base));
            profile.Add(new Vector2(lining, run));

            profile.Add(new Vector2(MountRadius, deck));
            profile.Add(new Vector2(0.0f, deck));

        }

        if (tipRadius > 0.01f) {

            // Drawn as a second run rather than a second surface: one sweep closes whichever ends
            // are open, and a stage with neither gets no interior at all.
            float lining = tipRadius - (float)hull.WallThickness;

            float deck = (float)hull.Tip - BulkheadInset;

            if (profile.Count > 0) {

                profile.Add(new Vector2(0.0f, deck));

            }

            profile.Add(new Vector2(MountRadius, deck));
            profile.Add(new Vector2(lining, (float)hull.Tip));
            profile.Add(new Vector2(tipRadius, (float)hull.Tip));

        }

        return profile.Count >= 2 ? profile.ToArray() : null;

    }

    /// <summary>Sweeps a radius/height profile about the nose axis, welding facets that meet shallowly.</summary>
    private static void Revolve(SurfaceTool surface, Vector2[] profile, int segments, bool outward, float tile, Func<int, float, float, bool> hole) {

        int facets = profile.Length - 1;

        Vector2[] normal = new Vector2[facets];
        float[] arc = new float[profile.Length];

        for (int index = 0; index < facets; index++) {

            Vector2 step = profile[index + 1] - profile[index];

            normal[index] = new Vector2(step.Y, -step.X).Normalized();
            arc[index + 1] = arc[index] + step.Length();

        }

        float weld = Mathf.Cos(Mathf.DegToRad(SmoothAngle));

        for (int index = 0; index < facets; index++) {

            Vector2 low = profile[index];
            Vector2 high = profile[index + 1];

            if (low.X <= 0.0f && high.X <= 0.0f) {

                continue;

            }

            Vector2 lowNormal = index > 0 && normal[index].Dot(normal[index - 1]) > weld
                ? (normal[index] + normal[index - 1]).Normalized()
                : normal[index];

            Vector2 highNormal = index + 1 < facets && normal[index].Dot(normal[index + 1]) > weld
                ? (normal[index] + normal[index + 1]).Normalized()
                : normal[index];

            for (int step = 0; step < segments; step++) {

                if (hole != null && hole(step, low.Y, high.Y)) {

                    continue;

                }

                float first = Mathf.Tau * step / segments;
                float second = Mathf.Tau * (step + 1) / segments;

                float leftU = (float)step / segments * TilesAround;
                float rightU = (float)(step + 1) / segments * TilesAround;

                float lowV = arc[index] / tile;
                float highV = arc[index + 1] / tile;

                // Godot takes clockwise winding as front-facing, so an inward sweep is the same two
                // triangles with their last two corners swapped. Flipping only the shading normal
                // leaves the surface back-facing, and the whole tail is then seen through.
                if (outward) {

                    Ring(surface, low, lowNormal, first, leftU, lowV, true);
                    Ring(surface, low, lowNormal, second, rightU, lowV, true);
                    Ring(surface, high, highNormal, first, leftU, highV, true);

                    Ring(surface, low, lowNormal, second, rightU, lowV, true);
                    Ring(surface, high, highNormal, second, rightU, highV, true);
                    Ring(surface, high, highNormal, first, leftU, highV, true);

                }
                else {

                    Ring(surface, low, lowNormal, first, leftU, lowV, false);
                    Ring(surface, high, highNormal, first, leftU, highV, false);
                    Ring(surface, low, lowNormal, second, rightU, lowV, false);

                    Ring(surface, low, lowNormal, second, rightU, lowV, false);
                    Ring(surface, high, highNormal, first, leftU, highV, false);
                    Ring(surface, high, highNormal, second, rightU, highV, false);

                }

            }

        }

    }

    private static void Ring(SurfaceTool surface, Vector2 point, Vector2 normal, float angle, float u, float v, bool outward) {

        float cosine = Mathf.Cos(angle);
        float sine = Mathf.Sin(angle);

        Vector3 facing = new Vector3(normal.X * cosine, normal.Y, normal.X * sine);

        surface.SetNormal(outward ? facing : -facing);
        surface.SetUV(new Vector2(u, v));

        surface.AddVertex(new Vector3(point.X * cosine, point.Y, point.X * sine));

    }

    private static void Pocket(SurfaceTool surface, int centre, Hull hull, Part part) {

        float middleHeight = (float)part.Centre;

        float outer = (float)hull.RadiusAt(middleHeight);
        float floor = outer - (float)part.Depth;

        float low = (float)part.Bottom;
        float high = (float)part.Top;

        float from = Mathf.Tau * (centre - RcsHalfSteps) / RadialSegments;
        float to = Mathf.Tau * (centre + RcsHalfSteps) / RadialSegments;

        Vector3 middle = Radial((from + to) * 0.5f);

        // Floor, drawn one segment at a time so it keeps the hull's curvature rather than chording it.
        for (int step = -RcsHalfSteps; step < RcsHalfSteps; step++) {

            float a = Mathf.Tau * (centre + step) / RadialSegments;
            float b = Mathf.Tau * (centre + step + 1) / RadialSegments;

            Quad(surface, At(a, floor, low), At(b, floor, low), At(b, floor, high), At(a, floor, high), (Radial(a) + Radial(b)).Normalized());

        }

        // Sides, then sill and lintel: each spans the full depth, so the wall's thickness is what
        // shows at the lip of the cut.
        Quad(surface, At(from, floor, low), At(from, outer, low), At(from, outer, high), At(from, floor, high), middle.Cross(Vector3.Down).Normalized() * -1.0f);
        Quad(surface, At(to, floor, low), At(to, outer, low), At(to, outer, high), At(to, floor, high), middle.Cross(Vector3.Down).Normalized());

        Quad(surface, At(from, floor, low), At(to, floor, low), At(to, outer, low), At(from, outer, low), Vector3.Up);
        Quad(surface, At(from, floor, high), At(to, floor, high), At(to, outer, high), At(from, outer, high), Vector3.Down);

    }

    private static Vector3 Radial(float angle) => new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));

    private static Vector3 At(float angle, float radius, float height) => Radial(angle) * radius + Vector3.Up * height;

    private static void Quad(SurfaceTool surface, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing) {

        if ((b - a).Cross(c - a).Dot(facing) > 0.0f) {

            (b, d) = (d, b);

        }

        Corner(surface, a, facing, new Vector2(0.0f, 0.0f));
        Corner(surface, b, facing, new Vector2(1.0f, 0.0f));
        Corner(surface, c, facing, new Vector2(1.0f, 1.0f));

        Corner(surface, a, facing, new Vector2(0.0f, 0.0f));
        Corner(surface, c, facing, new Vector2(1.0f, 1.0f));
        Corner(surface, d, facing, new Vector2(0.0f, 1.0f));

    }

    private static void Corner(SurfaceTool surface, Vector3 point, Vector3 facing, Vector2 uv) {

        surface.SetNormal(facing);
        surface.SetUV(uv);

        surface.AddVertex(point);

    }

    private static StandardMaterial3D HullMaterial(Color albedo, float metallic, float roughness) {

        // A fully metallic hull reads near-black in sunlight - its brightness is all specular, and a
        // white livery is a dielectric. The paint stays low-metallic and the maps carry the detail.
        return new StandardMaterial3D {

            AlbedoColor = albedo,
            AlbedoTexture = GD.Load<Texture2D>("res://Assets/Vessel/hull_color.jpg"),

            NormalEnabled = true,
            NormalTexture = GD.Load<Texture2D>("res://Assets/Vessel/hull_normal.jpg"),
            NormalScale = 2.0f,

            RoughnessTexture = GD.Load<Texture2D>("res://Assets/Vessel/hull_roughness.jpg"),

            Metallic = metallic,
            MetallicSpecular = 0.5f,
            Roughness = roughness,

        };

    }

    /// <summary>An ablator: near-black, matte and non-metallic, so what the eye reads off it during
    /// an entry is the emission and nothing else.</summary>
    private static StandardMaterial3D ShieldMaterial() {

        StandardMaterial3D material = HullMaterial(new Color(0.088f, 0.080f, 0.074f), 0.0f, 1.0f);

        material.NormalScale = 3.0f;
        material.MetallicSpecular = 0.18f;

        return material;

    }

    private static StandardMaterial3D Paint(Color albedo, float metallic, float roughness) {

        return new StandardMaterial3D {

            AlbedoColor = albedo,

            Metallic = metallic,
            MetallicSpecular = 0.5f,
            Roughness = roughness,

        };

    }

    private static ArrayMesh BuildNozzleMesh(float gauge) {

        // Mounting boss, then the chamber pinching down to the throat, then the bell. Sampling the
        // bell along its own curve is what stops it reading as a plain cone at this size.
        Vector2[] profile = {

            new Vector2(0.000f, -NozzleBase),
            new Vector2(0.077f, -NozzleBase),
            new Vector2(0.077f, -0.101f),
            new Vector2(0.056f, -0.094f),
            new Vector2(0.056f, -0.063f),

            new Vector2(0.043f, -0.038f),
            new Vector2(0.028f, 0.000f),

            new Vector2(0.037f, 0.027f),
            new Vector2(0.050f, 0.056f),
            new Vector2(0.064f, 0.088f),
            new Vector2(0.076f, 0.122f),
            new Vector2(0.081f, NozzleReach),

        };

        float scale = gauge / NozzleGauge;

        for (int index = 0; index < profile.Length; index++) {

            profile[index] *= scale;

        }

        SurfaceTool surface = new SurfaceTool();

        surface.Begin(Mesh.PrimitiveType.Triangles);

        Revolve(surface, profile, NozzleSegments, true, 1.0f, null);

        ArrayMesh mesh = new ArrayMesh();

        Commit(mesh, surface, "Nozzle", NozzleMaterial(), null);

        return mesh;

    }

    private static StandardMaterial3D NozzleMaterial() {

        StandardMaterial3D material = Paint(new Color(0.44f, 0.415f, 0.38f), 0.85f, 0.30f);

        // The bell is a single thin skin, so its inside only exists if back faces are drawn.
        material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

        return material;

    }

    /// <summary>A cluster's nozzles, either recessed in a pocket cut through the wall or standing
    /// on it. Which one it is comes off the part's own depth, not off the stage it belongs to.</summary>
    private void AttachThrusters(Node3D node, Piece piece, Stage stage, Part part) {

        ArrayMesh nozzle = BuildNozzleMesh((float)part.Extent);

        int index = 0;

        foreach ((Vector3 position, Vector3 axis, Vector3 side, float scale) in Mounts(stage, part)) {

            node.AddChild(new MeshInstance3D {

                Name = $"{part.Name}{index++}",
                Mesh = nozzle,

                Transform = new Transform3D(new Basis(side, axis, side.Cross(axis).Normalized()), position),

                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

            });

            AttachJet(node, piece, position, axis, side, scale);

        }

    }

    /// <summary>Outward normal of the mould line at a station, so hardware on a tapered wall stands
    /// off it square rather than leaning through it.</summary>
    private static Vector3 Surface(Stage stage, float angle, float height) {

        double step = 0.05;

        double below = stage.Hull.RadiusAt(height - step);
        double above = stage.Hull.RadiusAt(height + step);

        Vector2 slope = new Vector2((float)(step * 2.0), (float)(above - below));

        Vector2 normal = new Vector2(slope.Y, -slope.X).Normalized();

        return (Radial(angle) * -normal.X + Vector3.Up * -normal.Y).Normalized();

    }

    private void AttachEngine(Node3D node, Piece piece, Part part) {

        Node3D engine = Import("engine", (float)part.Length, Basis.Identity);

        if (engine == null) {

            return;

        }

        Aabb bounds = Bounds(engine, Transform3D.Identity);

        // The plume has to leave the nozzle the model actually has, so the bell is measured off the
        // scaled mesh rather than carried as a constant beside it. Plumbing hangs off one side and
        // widens that axis, so the narrower of the two is the one that reads the bell.
        float bellRadius = Mathf.Min(bounds.Size.X, bounds.Size.Z) * 0.5f;

        engine.Position = new Vector3(0.0f, (float)part.Top - bounds.Size.Y * 0.5f, 0.0f);

        float bellPlane = engine.Position.Y - bounds.Size.Y * 0.5f;

        node.AddChild(engine);

        AttachPlume(node, piece, bellRadius, bellPlane);

    }

    /// <summary>Loads a part and scales it so its own height fills the run it has to fill, seated
    /// on the station that run starts at. On the height rather than on the largest extent, because
    /// a capsule is wider than it is tall and the run it stands in is the tall one.</summary>
    private static Node3D Fit(string name, double bottom, double height) {

        Node3D source = LoadModel(name);

        if (source == null) {

            return null;

        }

        Aabb bounds = Bounds(source, source.Transform);

        if (bounds.Size.Y <= 0.0f) {

            return source;

        }

        float scale = (float)height / bounds.Size.Y;

        source.Transform = new Transform3D(

            Basis.Identity.Scaled(Vector3.One * scale),

            new Vector3(
                -bounds.GetCenter().X * scale,
                (float)bottom - bounds.Position.Y * scale,
                -bounds.GetCenter().Z * scale));

        Node3D holder = new Node3D { Name = name };

        holder.AddChild(source);

        return holder;

    }

    /// <summary>Loads a part, squares up its axes and rescales it to a known size about its centre.</summary>
    private static Node3D Import(string name, float size, Basis fix) {

        Node3D source = LoadModel(name);

        if (source == null) {

            return null;

        }

        source.Basis = fix;

        Aabb bounds = Bounds(source, source.Transform);

        float extent = Mathf.Max(bounds.Size.X, Mathf.Max(bounds.Size.Y, bounds.Size.Z));

        if (extent <= 0.0f) {

            return source;

        }

        float scale = size / extent;

        source.Transform = new Transform3D(fix.Scaled(Vector3.One * scale), -bounds.GetCenter() * scale);

        Node3D holder = new Node3D { Name = name };

        holder.AddChild(source);

        return holder;

    }

    private static Node3D LoadModel(string name) {

        string path = "res://Assets/Vessel/" + name + ".glb";

        if (!ResourceLoader.Exists(path)) {

            GD.PushError($"missing {path}");

            return null;

        }

        PackedScene packed = GD.Load<PackedScene>(path);

        if (packed == null) {

            GD.PushError($"failed to load {path}");

            return null;

        }

        Node3D model = packed.Instantiate<Node3D>();

        model.Name = name;

        return model;

    }

    private static Aabb Bounds(Node node, Transform3D transform) {

        Aabb total = new Aabb();
        bool started = false;

        if (node is VisualInstance3D visual) {

            total = transform * visual.GetAabb();
            started = true;

        }

        foreach (Node child in node.GetChildren()) {

            Transform3D nested = child is Node3D spatial ? transform * spatial.Transform : transform;

            Aabb below = Bounds(child, nested);

            if (below.Size == Vector3.Zero) {

                continue;

            }

            total = started ? total.Merge(below) : below;
            started = true;

        }

        return total;

    }

}
