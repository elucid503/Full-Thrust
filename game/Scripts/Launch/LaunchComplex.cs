using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class LaunchComplex : Node3D {

    internal const float ApronWidth = 48.0f;
    internal const float ApronDepth = 40.0f;

    // Past this the complex is a speck the floating origin can no longer hold steady, and every
    // node of it is costing a transform for nothing.
    private const double DrawRange = 60_000.0;

    private const float SteelTile = 2.2f;

    private CelestialBody _body;
    private LaunchSite _site;

    private Node3D _mount;
    private readonly Dictionary<Material, SurfaceTool> _batches = new();

    public void Build(CelestialBody body, LaunchSite site) {

        _body = body;
        _site = site;

        StandardMaterial3D steel = Steel(new Color(0.62f, 0.63f, 0.60f), 0.20f, 0.46f);
        StandardMaterial3D scorched = Steel(new Color(0.36f, 0.35f, 0.32f), 0.25f, 0.70f);

        _mount = new Node3D { Name = "Mount" };

        AddChild(_mount);

        Node3D gantry = GD.Load<PackedScene>("res://Assets/LaunchSite/Gantry.glb").Instantiate<Node3D>();
        gantry.Name = "Gantry";
        _mount.AddChild(gantry);
        SetDrawRange(gantry);

        Mount(steel, scorched);
        Infrastructure();
        CommitBatches();

    }

    /// <summary>Carries the complex round with the planet and drops it once it is too far to matter.</summary>
    public void Sync(double time, Vector3d eye) {

        Vector3d pad = _site.PositionAt(_body, time);

        bool near = (eye - pad).LengthSquared < DrawRange * DrawRange;

        Visible = near;

        if (!near) {

            return;

        }

        Vector3 east = Frames.Direction(_body.ToInertial(_site.East, time));
        Vector3 up = Frames.Direction(_body.ToInertial(_site.Up, time));

        // Set as columns rather than handed to the constructor: a Basis built from three vectors
        // takes them as rows, which is the transpose of the frame wanted and lays the pad flat.
        Basis frame = Basis.Identity;

        frame.X = east;
        frame.Y = up;
        frame.Z = east.Cross(up);

        Transform = new Transform3D(frame, Frames.Point(pad));

    }

    // A compact open launch table keeps the deck datum and clear exhaust route independent
    // of the imported service tower. Flanges and recessed webs give the steel real depth.
    private void Mount(Material steel, Material scorched) {

        float deck = (float)LaunchSite.MountHeight;
        for (int side = -1; side <= 1; side += 2) {

            Slab("Deck", new Vector3(12, 0.14f, 2.9f), new Vector3(0, deck - 0.07f, side * 4.55f), steel);
            Slab("Deck", new Vector3(2.9f, 0.14f, 6.2f), new Vector3(side * 4.55f, deck - 0.07f, 0), steel);
            Slab("BeamWeb", new Vector3(12, 0.70f, 0.18f), new Vector3(0, deck - 0.49f, side * 4.8f), scorched);
            Slab("BeamWeb", new Vector3(0.18f, 0.70f, 9.4f), new Vector3(side * 4.8f, deck - 0.49f, 0), scorched);
            Slab("LowerFlange", new Vector3(12, 0.12f, 0.8f), new Vector3(0, deck - 0.80f, side * 4.8f), steel);
            Slab("LowerFlange", new Vector3(0.8f, 0.12f, 9.4f), new Vector3(side * 4.8f, deck - 0.80f, 0), steel);

        }

        // Each hold-down is anchored outside the aperture. A braced arm reaches the vehicle;
        // the old blocks sat in the opening without a connection to the deck.
        for (int index = 0; index < 4; index++) {

            float angle = Mathf.Tau * index / 4.0f + Mathf.Pi * 0.25f;

            Vector3 radial = new(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
            Vector3 tangent = radial.Cross(Vector3.Up);
            Basis frame = Basis.LookingAt(-radial, Vector3.Up);
            Vector3 anchor = radial * 4.8f + Vector3.Up * deck;
            Vector3 contact = radial * 1.95f + Vector3.Up * (deck + 0.65f);
            Slab("AnchorPlate", new Vector3(0.9f, 0.12f, 0.8f), anchor + Vector3.Up * 0.06f, steel, frame);
            Slab("Pedestal", new Vector3(0.40f, 0.64f, 0.40f), anchor + Vector3.Up * 0.4f, steel, frame);
            Strut(anchor + Vector3.Up * 0.65f, contact, 0.26f, 0.28f, scorched);
            Strut(anchor + Vector3.Up * 0.15f, contact + radial * 0.50f, 0.16f, 0.18f, steel);
            Slab("BearingPad", new Vector3(0.48f, 0.20f, 0.32f), contact, steel, frame);
            using CylinderMesh pin = new() { TopRadius = 0.14f, BottomRadius = 0.14f, Height = 0.62f, RadialSegments = 20, Rings = 1 };
            Batch(pin, steel, new Transform3D(new Basis(new Quaternion(Vector3.Up, tangent)), anchor + Vector3.Up * 0.65f));

        }

    }

    private void Strut(Vector3 from, Vector3 to, float width, float depth, Material material) {

        Vector3 delta = to - from;
        Slab("Strut", new Vector3(width, delta.Length(), depth), (from + to) * 0.5f, material,
            new Basis(new Quaternion(Vector3.Up, delta.Normalized())));

    }

    private static void SetDrawRange(Node node) {

        if (node is GeometryInstance3D geometry) {

            geometry.VisibilityRangeEnd = 3500.0f;
            geometry.VisibilityRangeEndMargin = 250.0f;
            geometry.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;

        }
        foreach (Node child in node.GetChildren()) {

            SetDrawRange(child);

        }

    }

    private void Slab(string name, Vector3 size, Vector3 position, Material material, Basis basis = default) {

        using BoxMesh box = new() { Size = size };
        Batch(box, material, new Transform3D(basis == default ? Basis.Identity : basis, position));

    }

    private void Batch(Mesh mesh, Material material, Transform3D transform) {

        if (!_batches.TryGetValue(material, out SurfaceTool surface)) {

            surface = new SurfaceTool();
            surface.Begin(Mesh.PrimitiveType.Triangles);
            _batches.Add(material, surface);

        }

        surface.AppendFrom(mesh, 0, transform);

    }

    private void CommitBatches() {

        foreach ((Material material, SurfaceTool surface) in _batches) {

            surface.Index();
            surface.GenerateTangents();
            ArrayMesh mesh = surface.Commit();
            mesh.SurfaceSetMaterial(0, material);
            _mount.AddChild(new MeshInstance3D {

                Name = "Structure",
                Mesh = mesh,
                VisibilityRangeEnd = 3500.0f,
                VisibilityRangeEndMargin = 250.0f,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,

            });
            surface.Dispose();

        }

        _batches.Clear();

    }

    private void Infrastructure() {

        StandardMaterial3D concrete = new() {

            AlbedoTexture = GD.Load<Texture2D>("res://Assets/Planet/Ground/Pad/pad_concrete_colour.jpg"),
            NormalTexture = GD.Load<Texture2D>("res://Assets/Planet/Ground/Pad/pad_concrete_normal.jpg"),
            AlbedoColor = new Color(0.65f, 0.66f, 0.65f),
            Roughness = 0.94f,
            NormalEnabled = true,
            NormalScale = 0.28f,
            Uv1Triplanar = true,
            Uv1Scale = Vector3.One * 0.15f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,

        };
        StandardMaterial3D seams = new() { AlbedoColor = new Color(0.23f, 0.24f, 0.24f), Roughness = 1.0f };
        StandardMaterial3D yellow = new() { AlbedoColor = new Color(0.72f, 0.49f, 0.13f), Roughness = 0.8f };
        Slab("Apron", new Vector3(ApronWidth, 0.14f, ApronDepth), new Vector3(0, -0.06f, 0), concrete);
        Slab("TowerFoundation", new Vector3(6.2f, 0.18f, 6.2f), new Vector3(-10, -0.01f, 3), concrete);
        for (int x = -1; x <= 1; x += 2) {

            for (int z = -1; z <= 1; z += 2) {

                Vector3 foot = new(x * 4.8f, 0, z * 4.8f);
                Slab("PierFoot", new Vector3(1.8f, 0.25f, 1.8f), foot + Vector3.Up * 0.125f, concrete);
                Slab("Pier", new Vector3(0.9f, 3.39f, 0.9f), foot + Vector3.Up * 1.945f, concrete);

            }

        }
        for (int grid = -3; grid <= 3; grid++) {

            Slab("Joint", new Vector3(0.025f, 0.006f, ApronDepth), new Vector3(grid * 6, 0.016f, 0), seams);
            Slab("Joint", new Vector3(ApronWidth, 0.006f, 0.025f), new Vector3(0, 0.016f, grid * 5), seams);

        }
        for (int side = -1; side <= 1; side += 2) {

            Slab("ApronMark", new Vector3(42, 0.009f, 0.12f), new Vector3(0, 0.024f, side * 18.5f), yellow);

        }

    }

    private static StandardMaterial3D Steel(Color tint, float metallic, float roughness) {

        return new StandardMaterial3D {

            NormalTexture = GD.Load<Texture2D>("res://Assets/Vessel/Hull/hull_normal.jpg"),
            RoughnessTexture = GD.Load<Texture2D>("res://Assets/Vessel/Hull/hull_roughness.jpg"),

            NormalEnabled = true,
            NormalScale = 0.12f,

            AlbedoColor = tint,

            Metallic = metallic,
            Roughness = roughness,

            Uv1Triplanar = true,
            Uv1Scale = Vector3.One / SteelTile,

            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,

        };

    }

}
