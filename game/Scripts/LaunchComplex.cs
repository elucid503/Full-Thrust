using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The stand the vehicle leaves from: an apron, and the launch mount on it. Generated
/// rather than modelled, and deliberately nothing more than that - the trench, the strongback and
/// the rest of the yard wait for the complex to have its own milestone.</summary>
public sealed partial class LaunchComplex : Node3D {

    // Laid out with the pad deck at the origin, east downrange along +X and up along +Y, which is
    // the frame LaunchSite hands back.
    private const float ApronReach = 46.0f;
    private const float ApronThickness = 0.7f;
    private const float ApronRise = 0.16f;

    private const float MountReach = 6.8f;
    private const float MountBeam = 1.25f;
    private const float MountOpening = 3.1f;

    private const float LegRadius = 0.34f;

    // Past this the complex is a speck the floating origin can no longer hold steady, and every
    // node of it is costing a transform for nothing.
    private const double DrawRange = 60_000.0;

    private const float ConcreteTile = 4.0f;
    private const float SteelTile = 2.2f;

    private CelestialBody _body;
    private LaunchSite _site;

    private Node3D _yard;

    public void Build(CelestialBody body, LaunchSite site) {

        _body = body;
        _site = site;

        StandardMaterial3D concrete = Surface("pad_concrete", ConcreteTile, new Color(0.72f, 0.71f, 0.68f), 0.0f, 0.92f);

        StandardMaterial3D steel = Steel(new Color(0.62f, 0.63f, 0.60f), 0.20f, 0.46f);
        StandardMaterial3D scorched = Steel(new Color(0.20f, 0.19f, 0.18f), 0.35f, 0.62f);

        _yard = new Node3D { Name = "Yard" };

        AddChild(_yard);

        Slab("Apron", new Vector3(ApronReach * 2.0f, ApronThickness, ApronReach * 2.0f),
            new Vector3(0.0f, ApronRise - ApronThickness * 0.5f, 0.0f), concrete);

        Mount(steel, scorched);

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

            Tube("MountLeg", LegRadius, deck - MountBeam, corner * new Vector3(1.0f, 0.0f, 1.0f) + Vector3.Up * ((deck - MountBeam) * 0.5f + ApronRise), steel, 12);

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

        MeshInstance3D box = new MeshInstance3D {

            Name = name,

            Mesh = new BoxMesh { Size = size },

            MaterialOverride = material,

            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,

        };

        box.Transform = new Transform3D(basis == default ? Basis.Identity : basis, position);

        _yard.AddChild(box);

    }

    private void Tube(string name, float radius, float height, Vector3 position, Material material, int segments) {

        MeshInstance3D tube = new MeshInstance3D {

            Name = name,

            Mesh = new CylinderMesh {

                TopRadius = radius,
                BottomRadius = radius,
                Height = height,

                RadialSegments = segments,
                Rings = 1,

            },

            MaterialOverride = material,

            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,

        };

        tube.Position = position;

        _yard.AddChild(tube);

    }

    // Triplanar on the mesh's own coordinates rather than the world's: the world here is a planet
    // away from the origin, and a texture projected off those numbers has no precision left in it.
    private static StandardMaterial3D Surface(string name, float tile, Color tint, float metallic, float roughness) {

        return new StandardMaterial3D {

            AlbedoTexture = GD.Load<Texture2D>($"res://Assets/Planet/{name}_colour.jpg"),
            NormalTexture = GD.Load<Texture2D>($"res://Assets/Planet/{name}_normal.jpg"),
            NormalEnabled = true,

            AlbedoColor = tint,

            Metallic = metallic,
            Roughness = roughness,

            Uv1Triplanar = true,
            Uv1Scale = Vector3.One / tile,

            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,

        };

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
