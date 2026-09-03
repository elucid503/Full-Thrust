using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The flight vehicle: a lathed hull off its own mould line, the engine, and the plume.</summary>
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
    private const float MountDeck = 0.42f;

    // The engine hangs below the mount deck with its powerhead just inside the skirt.
    private const float EngineDeck = (float)Meridian.EngineDeck;
    private const float EngineLength = (float)Meridian.EngineLength;

    // Flush RCS: the ports are holes cut clean through the tank wall, and the only hardware that
    // shows is the pair of nozzles recessed at the bottom of each one.
    private const int RcsPorts = Meridian.RcsPorts;
    private const int RcsHalfSteps = 3;
    private const float RcsHeight = (float)Meridian.RcsHeight;
    private const float RcsHalfHeight = (float)Meridian.RcsHalfHeight;
    private const float RcsDepth = 0.26f;
    private const float RcsCant = 0.50f;

    // A canted bell reaches further up the pocket than its own length, and further in than its own
    // depth: the pocket is cut to clear both, and the pair sits close enough that neither mouth
    // cuts back out through the sill or the lintel.
    private const float RcsOffset = 0.085f;

    // Where the mounting plate and the bell mouth sit on the nozzle's own axis. The plate lands flat
    // on the pocket floor; standing the nozzle on its throat instead buries half of it in the plate.
    private const float NozzleBase = 0.126f;
    private const float NozzleReach = 0.135f;

    private const float PlumeLength = 18.0f;
    private const float PlumeFlare = 3.6f;

    private const int PlumeStations = 64;
    private const int PlumeShells = 14;
    private const int PlumeSegments = 72;
    private const int PlumeLightRing = 4;

    public static VesselView Active { get; private set; }

    private Node3D _body;

    private MeshInstance3D _plume;
    private ShaderMaterial _plumeMaterial;
    private readonly List<OmniLight3D> _plumeLights = new List<OmniLight3D>();

    private float _bellRadius;
    private float _bellPlane;

    private float _thrust;

    public void Build(Vessel vessel) {

        Active = this;

        _body = new Node3D { Name = "Body" };

        AddChild(_body);

        float datum = (float)vessel.CentreOfMassZ;

        _body.AddChild(new MeshInstance3D {

            Name = "Hull",
            Mesh = BuildHullMesh(vessel.Hull, datum),

            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,

        });

        AttachThrusters(datum);
        AttachEngine(datum);
        AttachPlume();

    }

    public void Sync(Vector3 point, Quaternion orientation, double thrust) {

        Position = point;
        Basis = new Basis(orientation);

        _thrust = Mathf.Lerp(_thrust, (float)thrust, 0.35f);

        bool lit = _thrust > 0.002f;

        _plume.Visible = lit;

        foreach (OmniLight3D light in _plumeLights) {

            light.Visible = lit;

        }

        if (!lit) {

            return;

        }

        _plumeMaterial.SetShaderParameter("throttle", _thrust);

        // A throttled engine runs a shorter, narrower plume rather than a dimmer one of the same size.
        _plume.Scale = new Vector3(0.55f + 0.45f * _thrust, 0.35f + 0.65f * _thrust, 0.55f + 0.45f * _thrust);

        foreach (OmniLight3D light in _plumeLights) {

            light.LightEnergy = light.OmniRange * 0.13f * _thrust;

        }

    }

    /// <summary>Live plume tuning from the debug bridge; scalars, or comma-separated colours.</summary>
    public bool Tune(string parameter, string value) {

        string[] parts = value.Split(',');

        if (parts.Length == 3) {

            _plumeMaterial.SetShaderParameter(parameter, new Color(parts[0].ToFloat(), parts[1].ToFloat(), parts[2].ToFloat()));

        }
        else {

            _plumeMaterial.SetShaderParameter(parameter, value.ToFloat());

        }

        return true;

    }

    private static float TileMetres => Mathf.Tau * (float)Meridian.BodyRadius / TilesAround;

    private static float PortLow => RcsHeight - RcsHalfHeight;
    private static float PortHigh => RcsHeight + RcsHalfHeight;

    // Ports sit between the quadrant axes so none of them ever fires straight along a control axis
    // on its own; the window is a whole number of segments wide, so its edges land on the grid.
    private static int PortCentre(int index) => RadialSegments / (RcsPorts * 2) + RadialSegments / RcsPorts * index;

    private static bool InsidePort(int step, float low, float high) {

        if (high <= PortLow || low >= PortHigh) {

            return false;

        }

        for (int index = 0; index < RcsPorts; index++) {

            int offset = step - PortCentre(index);

            if (offset >= -RcsHalfSteps && offset < RcsHalfSteps) {

                return true;

            }

        }

        return false;

    }

    private static ArrayMesh BuildHullMesh(Hull hull, float datum) {

        ArrayMesh mesh = new ArrayMesh();

        SurfaceTool skin = new SurfaceTool();

        skin.Begin(Mesh.PrimitiveType.Triangles);

        Revolve(skin, OuterProfile(hull, datum), RadialSegments, true, (step, low, high) => InsidePort(step, low + datum, high + datum));

        skin.GenerateTangents();

        Commit(mesh, skin, "Skin", HullMaterial(new Color(0.97f, 0.972f, 0.975f), 0.15f, 1.0f));

        SurfaceTool core = new SurfaceTool();

        core.Begin(Mesh.PrimitiveType.Triangles);

        Revolve(core, InnerProfile(hull, datum), RadialSegments, false, null);

        core.GenerateTangents();

        Commit(mesh, core, "Core", HullMaterial(new Color(0.27f, 0.27f, 0.278f), 0.40f, 1.0f));

        SurfaceTool ports = new SurfaceTool();

        ports.Begin(Mesh.PrimitiveType.Triangles);

        for (int index = 0; index < RcsPorts; index++) {

            Pocket(ports, PortCentre(index), hull, datum);

        }

        Commit(mesh, ports, "Ports", Paint(new Color(0.46f, 0.462f, 0.47f), 0.05f, 0.72f));

        return mesh;

    }

    private static void Commit(ArrayMesh mesh, SurfaceTool surface, string name, Material material) {

        surface.Commit(mesh);

        mesh.SurfaceSetName(mesh.GetSurfaceCount() - 1, name);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);

    }

    /// <summary>The mould line itself, with the port edges spliced in so the cutouts land on stations.</summary>
    private static Vector2[] OuterProfile(Hull hull, float datum) {

        List<float> heights = new List<float>();

        foreach (Hull.Station station in hull.Stations) {

            heights.Add((float)station.Z);

        }

        heights.Add(PortLow);
        heights.Add(PortHigh);

        heights.Sort();

        List<Vector2> profile = new List<Vector2>();

        foreach (float height in heights) {

            if (profile.Count > 0 && Mathf.Abs(profile[profile.Count - 1].Y + datum - height) < 1e-4f) {

                continue;

            }

            profile.Add(new Vector2((float)hull.RadiusAt(height), height - datum));

        }

        return profile.ToArray();

    }

    /// <summary>Tail rim, skirt lining and thrust cone as one profile, so the tail closes in a single surface.</summary>
    private static Vector2[] InnerProfile(Hull hull, float datum) {

        // A hull drawn as one surface has no wall: its open tail reads as a razor edge and the whole
        // stage looks like foil. The interior shell stands off by the mould line's own wall thickness.
        float lining = (float)(hull.RadiusAt(0.0) - hull.WallThickness);

        return new[] {

            new Vector2((float)hull.RadiusAt(0.0), -datum),
            new Vector2(lining, -datum),

            new Vector2(lining, (float)Meridian.SkirtTop - datum),

            new Vector2(MountRadius, MountDeck - datum),
            new Vector2(0.0f, MountDeck - datum),

        };

    }

    /// <summary>Sweeps a radius/height profile about the nose axis, welding facets that meet shallowly.</summary>
    private static void Revolve(SurfaceTool surface, Vector2[] profile, int segments, bool outward, Func<int, float, float, bool> hole) {

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

                float lowV = arc[index] / TileMetres;
                float highV = arc[index + 1] / TileMetres;

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

    private static void Pocket(SurfaceTool surface, int centre, Hull hull, float datum) {

        float outer = (float)hull.RadiusAt(RcsHeight);
        float floor = outer - RcsDepth;

        float low = PortLow - datum;
        float high = PortHigh - datum;

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

    private static StandardMaterial3D Paint(Color albedo, float metallic, float roughness) {

        return new StandardMaterial3D {

            AlbedoColor = albedo,

            Metallic = metallic,
            MetallicSpecular = 0.5f,
            Roughness = roughness,

        };

    }

    private static ArrayMesh BuildNozzleMesh() {

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

        SurfaceTool surface = new SurfaceTool();

        surface.Begin(Mesh.PrimitiveType.Triangles);

        Revolve(surface, profile, NozzleSegments, true, null);

        ArrayMesh mesh = new ArrayMesh();

        Commit(mesh, surface, "Nozzle", NozzleMaterial());

        return mesh;

    }

    private static StandardMaterial3D NozzleMaterial() {

        StandardMaterial3D material = Paint(new Color(0.44f, 0.415f, 0.38f), 0.85f, 0.30f);

        // The bell is a single thin skin, so its inside only exists if back faces are drawn.
        material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

        return material;

    }

    private void AttachThrusters(float datum) {

        ArrayMesh nozzle = BuildNozzleMesh();

        float floor = (float)Meridian.BodyRadius - RcsDepth;

        for (int index = 0; index < RcsPorts; index++) {

            float angle = Mathf.Tau * PortCentre(index) / RadialSegments;

            Vector3 outward = Radial(angle);
            Vector3 side = Vector3.Up.Cross(outward).Normalized();

            // One nozzle canted forward and one aft: a port that only fired radially could not pitch.
            for (int sense = -1; sense <= 1; sense += 2) {

                Vector3 axis = (outward * Mathf.Cos(RcsCant) + Vector3.Up * (Mathf.Sin(RcsCant) * sense)).Normalized();

                _body.AddChild(new MeshInstance3D {

                    Name = $"Rcs{index}{(sense > 0 ? "Fore" : "Aft")}",
                    Mesh = nozzle,

                    Transform = new Transform3D(
                        new Basis(side, axis, side.Cross(axis).Normalized()),
                        outward * floor + axis * NozzleBase + Vector3.Up * (RcsHeight + RcsOffset * sense - datum)),

                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

                });

            }

        }

    }

    private void AttachEngine(float datum) {

        Node3D engine = Import("engine", EngineLength, Basis.Identity);

        if (engine == null) {

            return;

        }

        Aabb bounds = Bounds(engine, Transform3D.Identity);

        // The plume has to leave the nozzle the model actually has, so the bell is measured off the
        // scaled mesh rather than carried as a constant beside it. Plumbing hangs off one side and
        // widens that axis, so the narrower of the two is the one that reads the bell.
        _bellRadius = Mathf.Min(bounds.Size.X, bounds.Size.Z) * 0.5f;

        engine.Position = new Vector3(0.0f, EngineDeck - bounds.Size.Y * 0.5f - datum, 0.0f);

        _bellPlane = engine.Position.Y - bounds.Size.Y * 0.5f;

        _body.AddChild(engine);

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

    private void AddPlumeLight(Vector3 position, float range) {

        OmniLight3D light = new OmniLight3D {

            LightColor = new Color(1.0f, 0.74f, 0.46f),

            OmniRange = range,
            OmniAttenuation = 0.7f,

            ShadowEnabled = false,
            Visible = false,

        };

        light.Position = position;

        _body.AddChild(light);

        _plumeLights.Add(light);

    }

    private void AttachPlume() {

        _plumeMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Plume.gdshader") };
        _plumeMaterial.RenderPriority = 3;

        _plume = new MeshInstance3D {

            Name = "Plume",
            Mesh = BuildPlumeMesh(_bellRadius),

            MaterialOverride = _plumeMaterial,

            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,

        };

        _plume.Position = new Vector3(0.0f, _bellPlane, 0.0f);

        _body.AddChild(_plume);

        // One source at the throat lights the engine, and a ring out in the plume rakes the tank wall,
        // which a light on the axis alone never can: the barrel's normals are perpendicular to it.
        AddPlumeLight(new Vector3(0.0f, _bellPlane - 1.0f, 0.0f), 12.0f);

        for (int index = 0; index < PlumeLightRing; index++) {

            float angle = Mathf.Tau * index / PlumeLightRing;

            AddPlumeLight(new Vector3(Mathf.Cos(angle) * 1.5f, _bellPlane - 4.5f, Mathf.Sin(angle) * 1.5f), 22.0f);

        }

    }

    // An under-expanded vacuum plume: nested shells stand in for the volume, dense on the axis and soft at the rim.
    private static ArrayMesh BuildPlumeMesh(float bell) {

        SurfaceTool surface = new SurfaceTool();

        surface.Begin(Mesh.PrimitiveType.Triangles);

        for (int shell = 0; shell < PlumeShells; shell++) {

            float radial = (shell + 0.5f) / PlumeShells;
            float girth = 0.16f + 0.84f * radial;

            for (int station = 0; station < PlumeStations; station++) {

                float lowerT = (float)station / PlumeStations;
                float upperT = (float)(station + 1) / PlumeStations;

                float lowerR = girth * bell * (1.0f + PlumeFlare * Mathf.Pow(lowerT, 0.62f));
                float upperR = girth * bell * (1.0f + PlumeFlare * Mathf.Pow(upperT, 0.62f));

                float lowerY = -PlumeLength * lowerT;
                float upperY = -PlumeLength * upperT;

                for (int step = 0; step < PlumeSegments; step++) {

                    float a = Mathf.Tau * step / PlumeSegments;
                    float b = Mathf.Tau * (step + 1) / PlumeSegments;

                    PlumeVertex(surface, lowerR, lowerY, a, lowerT, radial);
                    PlumeVertex(surface, upperR, upperY, a, upperT, radial);
                    PlumeVertex(surface, upperR, upperY, b, upperT, radial);

                    PlumeVertex(surface, lowerR, lowerY, a, lowerT, radial);
                    PlumeVertex(surface, upperR, upperY, b, upperT, radial);
                    PlumeVertex(surface, lowerR, lowerY, b, lowerT, radial);

                }

            }

        }

        return surface.Commit();

    }

    private static void PlumeVertex(SurfaceTool surface, float radius, float y, float angle, float axial, float radial) {

        float cosine = Mathf.Cos(angle);
        float sine = Mathf.Sin(angle);

        surface.SetNormal(new Vector3(cosine, 0.0f, sine));
        surface.SetUV(new Vector2(axial, radial));

        surface.AddVertex(new Vector3(radius * cosine, y, radius * sine));

    }

}
