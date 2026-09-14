using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class LaunchComplex : Node3D {

    internal const float ApronWidth = 48.0f;
    internal const float ApronDepth = 40.0f;

    private const float MountReach = 6.8f;
    private const float MountBeam = 1.25f;
    private const float MountOpening = 3.1f;

    private const float LegRadius = 0.34f;

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
        StandardMaterial3D scorched = Steel(new Color(0.20f, 0.19f, 0.18f), 0.35f, 0.62f);

        _mount = new Node3D { Name = "Mount" };

        AddChild(_mount);

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

    // The table the vehicle stands on: a square ring of deck beams on four legs, with the middle
    // left open so the bell hangs through it.
    private void Mount(Material steel, Material scorched) {

        float deck = (float)LaunchSite.MountHeight;

        float span = MountReach - MountOpening;

        for (int side = -1; side <= 1; side += 2) {

            Slab("DeckBeam", new Vector3(MountReach * 2.0f, MountBeam, span),
                new Vector3(0.0f, deck - MountBeam * 0.5f, side * (MountOpening + span * 0.5f)), steel);

            Slab("DeckBeam", new Vector3(span, MountBeam, MountOpening * 2.0f),
                new Vector3(side * (MountOpening + span * 0.5f), deck - MountBeam * 0.5f, 0.0f), steel);

        }

        foreach (Vector3 corner in Corners(MountReach - LegRadius * 2.0f)) {

            Tube("MountLeg", LegRadius, deck - MountBeam, corner * new Vector3(1.0f, 0.0f, 1.0f) + Vector3.Up * ((deck - MountBeam) * 0.5f), steel, 12);

        }

        // Four clamps at the vehicle's own radius, which is what actually holds it down until the
        // engine has come up to pressure.
        for (int index = 0; index < 4; index++) {

            float angle = Mathf.Tau * index / 4.0f + Mathf.Pi * 0.25f;

            Vector3 out_ = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));

            Slab("HoldDown", new Vector3(1.9f, 1.1f, 0.7f), out_ * 2.1f + Vector3.Up * (deck + 0.55f), scorched,
                new Basis(Vector3.Up, angle));

        }

    }

    private static Vector3[] Corners(float reach) {

        return new[] {

            new Vector3(-reach, 0.0f, -reach),
            new Vector3(reach, 0.0f, -reach),
            new Vector3(-reach, 0.0f, reach),
            new Vector3(reach, 0.0f, reach),

        };

    }

    private void Slab(string name, Vector3 size, Vector3 position, Material material, Basis basis = default) {

        using BoxMesh box = new() { Size = size };
        Batch(box, material, new Transform3D(basis == default ? Basis.Identity : basis, position));

    }

    private void Tube(string name, float radius, float height, Vector3 position, Material material, int segments) {

        using CylinderMesh tube = new() {

            TopRadius = radius,
            BottomRadius = radius,
            Height = height,

            RadialSegments = segments,
            Rings = 1,

        };

        Batch(tube, material, new Transform3D(Basis.Identity, position));

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

    private void Beam(Vector3 from, Vector3 to, float width, Material material) {

        Vector3 delta = to - from;
        Basis basis = new(new Quaternion(Vector3.Up, delta.Normalized()));
        Slab("Beam", new Vector3(width, delta.Length(), width), (from + to) * 0.5f, material, basis);

    }

    private void Rail(Vector3 from, Vector3 to, Material material) {

        Vector3 delta = to - from;
        int posts = Mathf.Max(1, Mathf.CeilToInt(delta.Length() / 1.6f));

        for (int index = 0; index <= posts; index++) {

            Vector3 foot = from.Lerp(to, (float)index / posts);
            Beam(foot, foot + Vector3.Up * 1.05f, 0.055f, material);

        }

        Beam(from + Vector3.Up * 1.05f, to + Vector3.Up * 1.05f, 0.06f, material);
        Beam(from + Vector3.Up * 0.50f, to + Vector3.Up * 0.50f, 0.045f, material);

    }

    private void Infrastructure() {

        StandardMaterial3D concrete = new() {

            AlbedoColor = new Color(0.43f, 0.46f, 0.46f),
            Roughness = 0.94f,
            NormalEnabled = true,
            NormalTexture = GD.Load<Texture2D>("res://Assets/Vessel/hull_normal.jpg"),
            NormalScale = 0.16f,
            Uv1Triplanar = true,
            Uv1Scale = Vector3.One * 0.32f,

        };
        StandardMaterial3D white = Steel(new Color(0.86f, 0.88f, 0.87f), 0.0f, 0.52f);
        StandardMaterial3D dark = Steel(new Color(0.12f, 0.16f, 0.18f), 0.45f, 0.62f);
        StandardMaterial3D zinc = Steel(new Color(0.50f, 0.55f, 0.57f), 0.72f, 0.36f);
        StandardMaterial3D yellow = new() { AlbedoColor = new Color(0.78f, 0.52f, 0.12f), Roughness = 0.72f };
        StandardMaterial3D seams = new() { AlbedoColor = new Color(0.17f, 0.19f, 0.19f), Roughness = 1.0f };

        Slab("Apron", new Vector3(ApronWidth, 0.14f, ApronDepth), new Vector3(0, -0.06f, 0), concrete);

        for (int grid = -3; grid <= 3; grid++) {

            Slab("Joint", new Vector3(0.025f, 0.006f, 40), new Vector3(grid * 6, 0.016f, 0), seams);
            Slab("Joint", new Vector3(48, 0.006f, 0.025f), new Vector3(0, 0.016f, grid * 5), seams);

        }

        Slab("FlameChannel", new Vector3(29, 0.06f, 6), new Vector3(0, 0.055f, 0), dark);

        for (int side = -1; side <= 1; side += 2) {
            Slab("BlastWall", new Vector3(28, 1.6f, 0.6f), new Vector3(0, 0.8f, side * 3.7f), concrete);
            Rail(new Vector3(-6.5f, 4.5f, side * 6.45f), new Vector3(6.5f, 4.5f, side * 6.45f), yellow);

            for (int outlet = -5; outlet <= 5; outlet++) {

                Vector3 nozzle = new(outlet * 1.0f, 1.6f, side * 3.45f);
                Beam(nozzle, nozzle + new Vector3(0, 0.1f, -side * 0.32f), 0.11f, zinc);

            }

            Beam(new Vector3(-6, 1.5f, side * 4.0f), new Vector3(6, 1.5f, side * 4.0f), 0.30f, zinc);

            for (int section = 0; section < 12; section++) {

                float x = side * (section + 0.5f) * 0.45f;
                float slope = side * (0.9f - section * 0.055f);
                Slab("Deflector", new Vector3(0.65f, 0.15f, 6.2f), new Vector3(x, 2.05f - section * 0.155f, 0), zinc,
                    new Basis(Vector3.Back, -slope));

            }

            Slab("ApronMark", new Vector3(42, 0.009f, 0.12f), new Vector3(0, 0.024f, side * 17.5f), yellow);

        }

        Vector3 tower = new(-12.5f, 0.0f, 9.0f);
        const float reach = 2.6f;
        const float height = 36.0f;

        foreach (Vector3 corner in Corners(reach)) {

            Slab("TowerFoot", new Vector3(1.5f, 0.6f, 1.5f), tower + corner + Vector3.Up * 0.3f, concrete);
            Beam(tower + corner, tower + corner + Vector3.Up * height, 0.42f, white);

        }

        for (int level = 0; level <= 8; level++) {

            float y = level * 4.0f;
            Vector3 floor = tower + Vector3.Up * y;
            Slab("Landing", new Vector3(5.6f, 0.16f, 5.6f), floor, dark);

            for (int side = -1; side <= 1; side += 2) {

                Vector3 a = floor + new Vector3(-reach, 0, side * reach);
                Vector3 b = floor + new Vector3(reach, 0, side * reach);
                Rail(a, b, zinc);
                Rail(floor + new Vector3(side * reach, 0, -reach), floor + new Vector3(side * reach, 0, reach), zinc);

                if (level < 8) {

                    Beam(a, b + Vector3.Up * 4, 0.17f, white);
                    Beam(b, a + Vector3.Up * 4, 0.17f, white);
                    Beam(floor + new Vector3(side * reach, 0, -reach), floor + new Vector3(side * reach, 4, reach), 0.17f, white);

                }

            }

            if (level < 8) {

                for (int step = 0; step < 20; step++) {

                    float travel = (step + 0.5f) / 20.0f;
                    Slab("Stair", new Vector3(1.0f, 0.08f, 0.25f), floor + new Vector3(1.6f, travel * 4, -2.3f + travel * 4.6f), zinc);

                }

                Rail(floor + new Vector3(2.15f, 0, -2.3f), floor + new Vector3(2.15f, 4, 2.3f), yellow);

            }

        }

        Slab("LiftShaft", new Vector3(1.5f, height, 1.5f), tower + new Vector3(-1.3f, height * 0.5f, 0), dark);
        Tube("LightningMast", 0.075f, 7, tower + Vector3.Up * (height + 3.5f), zinc, 10);
        Slab("CableTray", new Vector3(0.6f, 32, 0.25f), tower + new Vector3(-2.9f, 16, 0), zinc);

        for (int line = 0; line < 3; line++) {

            Tube("ServicePipe", 0.10f + line * 0.03f, 30, tower + new Vector3(-3.0f, 15, -1.2f + line * 0.5f), zinc, 10);

        }

        // The access platform ends outside the launch clearance envelope.
        Vector3 bridgeStart = tower + new Vector3(2.8f, 28, -0.8f);
        Vector3 bridgeEnd = new(-3.8f, 28, 2.7f);
        Beam(bridgeStart, bridgeEnd, 0.65f, white);
        Rail(bridgeStart + Vector3.Back * 0.6f, bridgeEnd + Vector3.Back * 0.6f, zinc);
        Rail(bridgeStart - Vector3.Back * 0.6f, bridgeEnd - Vector3.Back * 0.6f, zinc);

        for (int tank = 0; tank < 2; tank++) {

            Vector3 at = new(-17.5f + tank * 4, 2.2f, -12.5f);
            Tube("DelugeTank", 1.55f, 4.2f, at, white, 32);
            Tube("TankRim", 1.61f, 0.15f, at + Vector3.Up * 2, zinc, 32);
            Beam(at - Vector3.Up * 1.5f, new Vector3(at.X, 0.7f, -4), 0.25f, zinc);

        }

        for (int step = 0; step < 18; step++) {

            Slab("MountStair", new Vector3(1.2f, 0.10f, 0.33f), new Vector3(8.0f, (step + 1) * 0.25f, 11.5f - step * 0.30f), zinc);

        }

        Rail(new Vector3(8.65f, 0.25f, 11.5f), new Vector3(8.65f, 4.5f, 6.4f), yellow);
        Slab("Landing", new Vector3(2.2f, 0.15f, 1.4f), new Vector3(7.4f, 4.42f, 6.1f), dark);

    }

    private static StandardMaterial3D Steel(Color tint, float metallic, float roughness) {

        return new StandardMaterial3D {

            AlbedoTexture = GD.Load<Texture2D>("res://Assets/Vessel/hull_color.jpg"),
            NormalTexture = GD.Load<Texture2D>("res://Assets/Vessel/hull_normal.jpg"),
            RoughnessTexture = GD.Load<Texture2D>("res://Assets/Vessel/hull_roughness.jpg"),

            NormalEnabled = true,

            AlbedoColor = tint,

            Metallic = metallic,
            Roughness = roughness,

            Uv1Triplanar = true,
            Uv1Scale = Vector3.One / SteelTile,

            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,

        };

    }

}
