using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The complex the vehicle stands on: apron, flame trench, launch mount, strongback and
/// the yard around them. Generated rather than modelled - it is all slabs, tubes and truss, and
/// every dimension is driven by the vehicle it has to hold and the site it stands on.</summary>
public sealed partial class LaunchComplex : Node3D {

    // Laid out with the pad deck at the origin, east downrange along +X and up along +Y, which is
    // the frame LaunchSite hands back.
    private const float ApronReach = 46.0f;
    private const float ApronThickness = 0.7f;
    private const float ApronRise = 0.16f;

    private const float TrenchHalfWidth = 5.4f;
    private const float TrenchHalfLength = 12.0f;
    private const float TrenchDepth = 5.5f;
    private const float TrenchWall = 0.9f;

    private const float DeflectorHeight = 3.9f;
    private const float DeflectorLength = 18.0f;

    private const float MountReach = 6.8f;
    private const float MountBeam = 1.25f;
    private const float MountOpening = 3.1f;

    private const float LegRadius = 0.34f;

    // The strongback stands off the vehicle's skin by enough to swing clear, and reaches most of
    // the way up the stack - past the second stage's tank, not past the capsule.
    private const float BackStandoff = 3.6f;
    private const float BackHalfWidth = 0.95f;
    private const float BackHeight = 25.5f;
    private const float BackBay = 2.4f;
    private const float LongeronRadius = 0.11f;
    private const float BraceRadius = 0.055f;

    private const int UmbilicalCount = 3;

    private const float MastRadius = 40.0f;
    private const float MastHeight = 46.0f;
    private const int MastCount = 3;

    private const float TankRadius = 3.1f;
    private const float TankHeight = 9.5f;

    private const float RoadHalfWidth = 4.5f;
    private const float RoadReach = 180.0f;

    // Past this the complex is a speck the floating origin can no longer hold steady, and every
    // node of it is costing a transform for nothing.
    private const double DrawRange = 60_000.0;

    private const float ConcreteTile = 4.0f;
    private const float AsphaltTile = 6.0f;
    private const float SteelTile = 2.2f;

    private CelestialBody _body;
    private LaunchSite _site;

    private Node3D _yard;

    public void Build(CelestialBody body, LaunchSite site) {

        _body = body;
        _site = site;

        StandardMaterial3D concrete = Surface("pad_concrete", ConcreteTile, new Color(0.72f, 0.71f, 0.68f), 0.0f, 0.92f);
        StandardMaterial3D asphalt = Surface("pad_asphalt", AsphaltTile, new Color(0.30f, 0.30f, 0.31f), 0.0f, 0.94f);

        StandardMaterial3D steel = Steel(new Color(0.62f, 0.63f, 0.60f), 0.20f, 0.46f);
        StandardMaterial3D scorched = Steel(new Color(0.20f, 0.19f, 0.18f), 0.35f, 0.62f);
        StandardMaterial3D paint = Steel(new Color(0.78f, 0.30f, 0.16f), 0.10f, 0.55f);

        _yard = new Node3D { Name = "Yard" };

        AddChild(_yard);

        Apron(concrete, asphalt);
        Trench(concrete, scorched);
        Mount(steel, scorched);
        Strongback(steel, paint);
        Masts(steel);
        Yard(concrete, steel);

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

    // The apron, with a rectangular mouth left in it for the trench. Four slabs rather than one:
    // a hole is the only thing a box cannot be, and four boxes are cheaper than a lathe for it.
    private void Apron(Material concrete, Material asphalt) {

        float shoulder = (ApronReach - TrenchHalfLength) * 0.5f;

        Slab("ApronNorth", new Vector3(ApronReach * 2.0f, ApronThickness, shoulder * 2.0f),
            new Vector3(0.0f, ApronRise - ApronThickness * 0.5f, -TrenchHalfLength - shoulder), concrete);

        Slab("ApronSouth", new Vector3(ApronReach * 2.0f, ApronThickness, shoulder * 2.0f),
            new Vector3(0.0f, ApronRise - ApronThickness * 0.5f, TrenchHalfLength + shoulder), concrete);

        float flank = (ApronReach - TrenchHalfWidth) * 0.5f;

        Slab("ApronWest", new Vector3(flank * 2.0f, ApronThickness, TrenchHalfLength * 2.0f),
            new Vector3(-TrenchHalfWidth - flank, ApronRise - ApronThickness * 0.5f, 0.0f), concrete);

        Slab("ApronEast", new Vector3(flank * 2.0f, ApronThickness, TrenchHalfLength * 2.0f),
            new Vector3(TrenchHalfWidth + flank, ApronRise - ApronThickness * 0.5f, 0.0f), concrete);

        Slab("Road", new Vector3(RoadReach, 0.24f, RoadHalfWidth * 2.0f),
            new Vector3(-ApronReach - RoadReach * 0.5f, 0.08f, 0.0f), asphalt);

    }

    private void Trench(Material concrete, Material scorched) {

        Slab("TrenchFloor", new Vector3(TrenchHalfWidth * 2.0f, 0.9f, TrenchHalfLength * 2.0f),
            new Vector3(0.0f, -TrenchDepth - 0.45f, 0.0f), concrete);

        for (int side = -1; side <= 1; side += 2) {

            Slab("TrenchWall", new Vector3(TrenchWall, TrenchDepth + ApronRise, TrenchHalfLength * 2.0f),
                new Vector3(side * (TrenchHalfWidth + TrenchWall * 0.5f), (ApronRise - TrenchDepth) * 0.5f, 0.0f), concrete);

            Slab("TrenchEnd", new Vector3(TrenchHalfWidth * 2.0f + TrenchWall * 2.0f, 1.4f, TrenchWall),
                new Vector3(0.0f, ApronRise - 0.7f, side * (TrenchHalfLength + TrenchWall * 0.5f)), concrete);

        }

        // A ridge across the trench, so the jet that comes down the middle is turned along it and
        // leaves at both ends rather than straight back up at the vehicle that made it.
        MeshInstance3D wedge = new MeshInstance3D {

            Name = "Deflector",

            Mesh = new PrismMesh { Size = new Vector3(DeflectorLength, DeflectorHeight, TrenchHalfWidth * 2.0f - 0.4f) },

            MaterialOverride = scorched,

        };

        wedge.Position = new Vector3(0.0f, -TrenchDepth + DeflectorHeight * 0.5f, 0.0f);
        wedge.RotateY(Mathf.Pi * 0.5f);

        _yard.AddChild(wedge);

    }

    // The table the vehicle stands on: a square ring of deck beams on four legs, with the middle
    // left open so the bell hangs through it and fires into the trench.
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

    // A lattice beam standing alongside the vehicle, hinged at the apron. Four longerons and the
    // braces between them, which is all a transporter-erector is.
    private void Strongback(Material steel, Material paint) {

        float x = -BackStandoff;

        Slab("Hinge", new Vector3(4.6f, 2.2f, 4.6f), new Vector3(x - 1.0f, ApronRise + 1.1f, 0.0f), steel);

        float low = ApronRise + 2.2f;
        float high = low + BackHeight;

        Vector3[] feet = {

            new Vector3(x - BackHalfWidth, low, -BackHalfWidth),
            new Vector3(x - BackHalfWidth, low, BackHalfWidth),
            new Vector3(x + BackHalfWidth, low, -BackHalfWidth),
            new Vector3(x + BackHalfWidth, low, BackHalfWidth),

        };

        foreach (Vector3 foot in feet) {

            Beam("Longeron", foot, foot + Vector3.Up * BackHeight, LongeronRadius, steel);

        }

        int bays = Mathf.Max(1, Mathf.RoundToInt(BackHeight / BackBay));

        for (int bay = 0; bay < bays; bay++) {

            float bottom = low + BackHeight * bay / bays;
            float top = low + BackHeight * (bay + 1) / bays;

            // Ladder rungs at every joint and one diagonal in each face, handed the other way each
            // bay so the beam is stiff in both directions rather than only one.
            for (int face = 0; face < 4; face++) {

                Vector3 first = feet[face];
                Vector3 second = feet[Neighbour(face)];

                Beam("Rung", Level(first, top), Level(second, top), BraceRadius, steel);

                bool flip = (bay + face) % 2 == 0;

                Beam("Brace", Level(flip ? first : second, bottom), Level(flip ? second : first, top), BraceRadius, steel);

            }

        }

        for (int index = 0; index < UmbilicalCount; index++) {

            float height = low + BackHeight * (index + 1) / (UmbilicalCount + 1);

            Slab("Umbilical", new Vector3(2.0f, 0.5f, 1.1f), new Vector3(x + BackHalfWidth + 1.0f, height, 0.0f), paint);

        }

        Slab("Crown", new Vector3(2.6f, 0.5f, 2.6f), new Vector3(x, high + 0.25f, 0.0f), steel);

    }

    // Three masts on a ring, which is what actually takes a strike rather than the vehicle.
    private void Masts(Material steel) {

        for (int index = 0; index < MastCount; index++) {

            float angle = Mathf.Tau * index / MastCount + Mathf.Pi * 0.5f;

            Vector3 foot = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * MastRadius;

            MeshInstance3D mast = new MeshInstance3D {

                Name = "Mast",

                Mesh = new CylinderMesh {

                    TopRadius = 0.42f,
                    BottomRadius = 1.15f,
                    Height = MastHeight,
                    RadialSegments = 14,
                    Rings = 1,

                },

                MaterialOverride = steel,

                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,

            };

            mast.Position = foot + Vector3.Up * (MastHeight * 0.5f + ApronRise);

            _yard.AddChild(mast);

            Tube("MastTip", 0.12f, 7.0f, foot + Vector3.Up * (MastHeight + 3.5f), steel, 8);

        }

    }

    private void Yard(Material concrete, Material steel) {

        Tube("FuelTank", TankRadius, TankHeight, new Vector3(-62.0f, TankHeight * 0.5f + ApronRise, -19.0f), steel, 20);
        Tube("OxidiserTank", TankRadius, TankHeight, new Vector3(-62.0f, TankHeight * 0.5f + ApronRise, 8.0f), steel, 20);

        MeshInstance3D water = new MeshInstance3D {

            Name = "WaterTank",

            Mesh = new SphereMesh { Radius = 4.4f, Height = 8.8f, RadialSegments = 20, Rings = 12 },

            MaterialOverride = steel,

            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,

        };

        water.Position = new Vector3(38.0f, 6.0f, -38.0f);

        _yard.AddChild(water);

        Tube("WaterLeg", 0.45f, 2.0f, new Vector3(38.0f, 1.0f, -38.0f), steel, 10);

        Slab("Blockhouse", new Vector3(17.0f, 5.5f, 11.0f), new Vector3(-78.0f, 2.9f, 28.0f), concrete);
        Slab("Shop", new Vector3(24.0f, 7.0f, 14.0f), new Vector3(-96.0f, 3.6f, -24.0f), concrete);

    }

    private static int Neighbour(int face) => face switch { 0 => 1, 1 => 3, 2 => 0, _ => 2 };

    private static Vector3 Level(Vector3 point, float height) => new Vector3(point.X, height, point.Z);

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

    private void Beam(string name, Vector3 from, Vector3 to, float radius, Material material) {

        Vector3 along = to - from;

        float length = along.Length();

        if (length < 0.01f) {

            return;

        }

        MeshInstance3D beam = new MeshInstance3D {

            Name = name,

            Mesh = new CylinderMesh {

                TopRadius = radius,
                BottomRadius = radius,
                Height = length,

                RadialSegments = 8,
                Rings = 1,

            },

            MaterialOverride = material,

            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,

        };

        // A cylinder is drawn up its own Y, so the member is turned onto the run it has to span.
        Vector3 axis = along / length;

        Vector3 reference = Mathf.Abs(axis.Y) > 0.99f ? Vector3.Right : Vector3.Up;

        Vector3 side = reference.Cross(axis).Normalized();

        Basis frame = Basis.Identity;

        frame.X = side;
        frame.Y = axis;
        frame.Z = side.Cross(axis);

        beam.Transform = new Transform3D(frame, (from + to) * 0.5f);

        _yard.AddChild(beam);

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
