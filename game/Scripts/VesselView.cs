using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The flight vehicle: imported shells stretched onto its own mould line, its detail parts, and the plume.</summary>
public sealed partial class VesselView : Node3D {

    private const int RadialSegments = 96;

    // The imported bell is 0.2935 of the engine's own length across, so this height puts its mouth
    // on BellRadius exactly; the two move together or the plume leaves the nozzle.
    private const float EngineHeight = 2.05f;
    private const float BellRadius = 0.60f;

    // Only the injector head sits up inside the aft skirt: the turbopumps and their plumbing are
    // the reason for carrying a modelled engine at all, so they stay out where they can be seen.
    private const float EngineRecess = 0.30f;

    // The stations the imported tank shell is stretched between.
    private const float TankLow = (float)Meridian.SkirtTop;
    private const float TankHigh = (float)Meridian.NoseBase;

    // The tank's aft dome, clear of both the boattail's rim and the tank shell's own bottom flange.
    private const float ClosureHeight = 0.75f;

    // On the forward tank, standing off far enough that the mounting boss buries itself in the wall.
    private const float RcsHeight = 6.90f;
    private const float RcsRadius = 1.26f;
    private const float RcsSize = 0.42f;

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

    private float _throttle;

    public void Build(Vessel vessel) {

        Active = this;

        _body = new Node3D { Name = "Body" };

        AddChild(_body);

        float datum = (float)vessel.CentreOfMassZ;

        _body.AddChild(new MeshInstance3D {

            Name = "Hull",
            Mesh = BuildHullMesh(datum),

            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,

        });

        AttachParts(datum);
        AttachPlume(datum);

    }

    public void Sync(Vector3 point, Quaternion orientation, double throttle) {

        Position = point;
        Basis = new Basis(orientation);

        _throttle = Mathf.Lerp(_throttle, (float)throttle, 0.35f);

        bool lit = _throttle > 0.002f;

        _plume.Visible = lit;

        foreach (OmniLight3D light in _plumeLights) {

            light.Visible = lit;

        }

        if (!lit) {

            return;

        }

        _plumeMaterial.SetShaderParameter("throttle", _throttle);

        // A throttled engine runs a shorter, narrower plume rather than a dimmer one of the same size.
        _plume.Scale = new Vector3(0.55f + 0.45f * _throttle, 0.35f + 0.65f * _throttle, 0.55f + 0.45f * _throttle);

        foreach (OmniLight3D light in _plumeLights) {

            light.LightEnergy = light.OmniRange * 0.13f * _throttle;

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

    private static ArrayMesh BuildHullMesh(float datum) {

        ArrayMesh mesh = new ArrayMesh();

        // Skirt, tank and capsule are imported shells covering the whole mould line between them, so
        // all the lathe still owns is the tank's two domes. Both stand off the stations where a shell
        // rim sits, because a disc coplanar with one flickers against it.
        SurfaceTool closure = new SurfaceTool();

        closure.Begin(Mesh.PrimitiveType.Triangles);

        Cap(closure, new Vector2((float)Meridian.BodyRadius, ClosureHeight - datum), Vector3.Down);
        Cap(closure, new Vector2((float)Meridian.BodyRadius, (float)Meridian.NoseBase - 0.06f - datum), Vector3.Up);

        closure.GenerateTangents();
        closure.Commit(mesh);

        mesh.SurfaceSetName(mesh.GetSurfaceCount() - 1, "Closure");
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, Paint(new Color(0.27f, 0.263f, 0.252f), 0.35f, 0.58f));

        return mesh;

    }

    private static StandardMaterial3D Paint(Color albedo, float metallic, float roughness) {

        return new StandardMaterial3D {

            AlbedoColor = albedo,

            Metallic = metallic,
            MetallicSpecular = 0.5f,
            Roughness = roughness,

        };

    }

    private static void Cap(SurfaceTool surface, Vector2 root, Vector3 facing) {

        for (int step = 0; step < RadialSegments; step++) {

            float first = Mathf.Tau * step / RadialSegments;
            float second = Mathf.Tau * (step + 1) / RadialSegments;

            // Front faces are clockwise, so the rim order is what decides which way the disc is seen.
            float a = facing == Vector3.Down ? first : second;
            float b = facing == Vector3.Down ? second : first;

            surface.SetNormal(facing);
            surface.SetUV(new Vector2(0.5f, 0.5f));
            surface.AddVertex(new Vector3(0.0f, root.Y, 0.0f));

            surface.SetNormal(facing);
            surface.SetUV(new Vector2(Mathf.Cos(b) * 0.5f + 0.5f, Mathf.Sin(b) * 0.5f + 0.5f));
            surface.AddVertex(new Vector3(Mathf.Cos(b) * root.X, root.Y, Mathf.Sin(b) * root.X));

            surface.SetNormal(facing);
            surface.SetUV(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
            surface.AddVertex(new Vector3(Mathf.Cos(a) * root.X, root.Y, Mathf.Sin(a) * root.X));

        }

    }

    /// <summary>Stretches a unit shell onto a band of the mould line, so an imported skin lands on the
    /// same stations the mass properties were integrated from.</summary>
    private void Shell(string name, float low, float high, float radius, float datum, bool twoSided = false) {

        Node3D shell = LoadModel(name);

        if (shell == null) {

            return;

        }

        shell.Scale = new Vector3(radius, high - low, radius);
        shell.Position = new Vector3(0.0f, (low + high) * 0.5f - datum, 0.0f);

        if (twoSided) {

            ShowBackFaces(shell);

        }

        _body.AddChild(shell);

    }

    // A shell modelled as a thin open barrel has no inside, so its far wall is culled away and the
    // stage reads as see-through. Drawing both faces costs nothing at this triangle count.
    private static void ShowBackFaces(Node node) {

        if (node is MeshInstance3D instance && instance.Mesh != null) {

            for (int surface = 0; surface < instance.Mesh.GetSurfaceCount(); surface++) {

                if (instance.Mesh.SurfaceGetMaterial(surface) is BaseMaterial3D material) {

                    material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

                }

            }

        }

        foreach (Node child in node.GetChildren()) {

            ShowBackFaces(child);

        }

    }

    private void AttachParts(float datum) {

        // Every one of these is a thin open barrel with no inside, so all three are drawn two-sided.
        Shell("skirt", 0.0f, TankLow, (float)Meridian.BodyRadius, datum, twoSided: true);

        Shell("tank", TankLow, TankHigh, (float)Meridian.BodyRadius, datum, twoSided: true);

        // The capsule sits straight on the tank flange, so its base is the full body radius.
        Shell("nose", (float)Meridian.NoseBase, (float)Meridian.OverallLength, (float)Meridian.BodyRadius, datum, twoSided: true);


        // The engine hangs on the stage axis: its own long axis is already the thrust axis, bell aft.
        Node3D engine = Part("engine", EngineHeight, Basis.Identity);

        if (engine != null) {

            engine.Position = new Vector3(0.0f, -datum - EngineHeight * 0.5f + EngineRecess, 0.0f);

            _body.AddChild(engine);

        }

        // The quad carries its mounting boss on +X and its nozzle cross on -X, so -X is what has to
        // end up pointing off the hull; the boss then buries itself in the equipment bay wall.
        Mount("rcs", RcsSize, new Basis(Vector3.Back, -Mathf.Pi * 0.5f), RcsHeight - datum, RcsRadius, 4, Mathf.Pi * 0.25f);

    }

    private void Mount(string name, float size, Basis fix, float height, float radius, int count, float phase) {

        for (int index = 0; index < count; index++) {

            Node3D part = Part(name, size, fix);

            if (part == null) {

                return;

            }

            float angle = Mathf.Tau * index / count + phase;

            Vector3 outward = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));

            part.Position = outward * radius + new Vector3(0.0f, height, 0.0f);
            part.Basis = new Basis(outward.Cross(Vector3.Up), outward, Vector3.Up);

            _body.AddChild(part);

        }

    }

    /// <summary>Loads a generated part, squares up its axes and rescales it to a known size about its centre.</summary>
    private static Node3D Part(string name, float size, Basis fix) {

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

    private void AttachPlume(float datum) {

        _plumeMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Plume.gdshader") };
        _plumeMaterial.RenderPriority = 3;

        _plume = new MeshInstance3D {

            Name = "Plume",
            Mesh = BuildPlumeMesh(),

            MaterialOverride = _plumeMaterial,

            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,

        };

        _plume.Position = new Vector3(0.0f, -datum - EngineHeight + EngineRecess, 0.0f);

        _body.AddChild(_plume);

        // One source at the throat lights the engine, and a ring out in the plume rakes the tank wall,
        // which a light on the axis alone never can: the barrel's normals are perpendicular to it.
        AddPlumeLight(new Vector3(0.0f, -datum - EngineHeight + EngineRecess - 1.0f, 0.0f), 12.0f);

        for (int index = 0; index < PlumeLightRing; index++) {

            float angle = Mathf.Tau * index / PlumeLightRing;

            AddPlumeLight(new Vector3(Mathf.Cos(angle) * 1.5f, -datum - EngineHeight + EngineRecess - 4.5f, Mathf.Sin(angle) * 1.5f), 22.0f);

        }

    }

    // An under-expanded vacuum plume: nested shells stand in for the volume, dense on the axis and soft at the rim.
    private static ArrayMesh BuildPlumeMesh() {

        SurfaceTool surface = new SurfaceTool();

        surface.Begin(Mesh.PrimitiveType.Triangles);

        for (int shell = 0; shell < PlumeShells; shell++) {

            float radial = (shell + 0.5f) / PlumeShells;
            float girth = 0.16f + 0.84f * radial;

            for (int station = 0; station < PlumeStations; station++) {

                float lowerT = (float)station / PlumeStations;
                float upperT = (float)(station + 1) / PlumeStations;

                float lowerR = girth * BellRadius * (1.0f + PlumeFlare * Mathf.Pow(lowerT, 0.62f));
                float upperR = girth * BellRadius * (1.0f + PlumeFlare * Mathf.Pow(upperT, 0.62f));

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
