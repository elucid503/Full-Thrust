using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>Terra: the quadtree surface, the cloud deck marched over it, and the scattering shell
/// over both. Reads the body; holds no sim state.</summary>
public sealed partial class Planet : Node3D {

    // The deck the volume is marched between. Under a fifth-scale sky a seven-kilometre top is
    // where cumulus actually stops, and the base is where a coastal deck sits on a summer morning.
    private const float CloudBase = 1200.0f;
    private const float CloudTop = 7000.0f;

    // The shell the march is fired from has to enclose the deck at every angle, so it stands off
    // the top by more than the sagitta of its own tessellation.
    private const float CloudStandoff = 1600.0f;

    private const int CloudSegments = 192;
    private const int CloudRings = 96;

    private const int AtmosphereSegments = 96;
    private const int AtmosphereRings = 48;

    // Where the shadow is taken from, which is the middle of the deck rather than either edge.
    private const float ShadowDeck = 3400.0f;

    // The deck turns fractionally faster than the ground, so cloud shadows creep rather than lock.
    private const double CloudRotationRatio = 1.02;

    // How far the cloud noise itself is carried per second, in tiles of its own volume.
    private const double CloudDrift = 1.6e-6;

    public static Planet Active { get; private set; }

    private CelestialBody _body;

    private Ground _ground;

    private ShaderMaterial[] _faces;
    private ShaderMaterial _clouds;
    private ShaderMaterial _atmosphere;

    // The shells sit on the planet's centre; the quadtree places every patch on its own absolute
    // transform, so this node stays at the origin and nothing under it is offset twice.
    private MeshInstance3D _deck;
    private MeshInstance3D _air;

    public int PatchCount => _ground?.PatchCount ?? 0;
    public int DeepestLevel => _ground?.DeepestLevel ?? 0;

    public void Build(CelestialBody body, Vector3 sunDirection) {

        Active = this;

        _body = body;

        float radius = (float)body.Radius;
        float atmosphereRadius = radius + (float)body.AtmosphereTop;

        Texture2D cloud = GD.Load<Texture2D>("res://Assets/Planet/clouds.jpg");

        BuildFaces(radius, cloud, sunDirection);

        _ground = new Ground { Name = "Surface" };

        AddChild(_ground);

        _ground.Build(body, _faces);

        _atmosphere = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Atmosphere.gdshader") };

        _atmosphere.SetShaderParameter("planet_radius", radius);
        _atmosphere.SetShaderParameter("atmosphere_radius", atmosphereRadius);
        _atmosphere.SetShaderParameter("sun_direction", sunDirection);
        _atmosphere.SetShaderParameter("rayleigh_height", (float)body.Atmosphere.ScaleHeight);

        // The air composites over the ground and the clouds stand in front of the air: from the pad
        // that is a blue sky with cloud on it, and from orbit it is cloud inside a lit limb.
        _atmosphere.RenderPriority = 1;

        _clouds = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Clouds.gdshader") };

        _clouds.SetShaderParameter("cloud_map", cloud);
        _clouds.SetShaderParameter("shape_noise", Volume(128, 0.035f, 4));
        _clouds.SetShaderParameter("detail_noise", Volume(64, 0.060f, 3));

        _clouds.SetShaderParameter("sun_direction", sunDirection);
        _clouds.SetShaderParameter("planet_radius", radius);
        _clouds.SetShaderParameter("base_radius", radius + CloudBase);
        _clouds.SetShaderParameter("top_radius", radius + CloudTop);

        _clouds.RenderPriority = 2;

        _deck = Shell("Clouds", radius + CloudTop + CloudStandoff, CloudSegments, CloudRings, _clouds);

        AddChild(_deck);

        if (body.HasAtmosphere) {

            _air = Shell("Atmosphere", atmosphereRadius * 1.002f, AtmosphereSegments, AtmosphereRings, _atmosphere);

            AddChild(_air);

        }

    }

    private void BuildFaces(float radius, Texture2D cloud, Vector3 sunDirection) {

        Shader shader = GD.Load<Shader>("res://Shaders/Ground.gdshader");

        Texture2D rockColour = GD.Load<Texture2D>("res://Assets/Planet/rock_colour.jpg");
        Texture2D rockNormal = GD.Load<Texture2D>("res://Assets/Planet/rock_normal.jpg");
        Texture2D soilColour = GD.Load<Texture2D>("res://Assets/Planet/soil_colour.jpg");
        Texture2D soilNormal = GD.Load<Texture2D>("res://Assets/Planet/soil_normal.jpg");
        Texture2D waveNormal = GD.Load<Texture2D>("res://Assets/Planet/wave_normal.png");

        _faces = new ShaderMaterial[6];

        for (int face = 0; face < 6; face++) {

            ShaderMaterial material = new ShaderMaterial { Shader = shader };

            material.SetShaderParameter("surface_map", GD.Load<Texture2D>($"res://Assets/Planet/surface_{face}.jpg"));
            material.SetShaderParameter("climate_map", GD.Load<Texture2D>($"res://Assets/Planet/climate_{face}.png"));
            material.SetShaderParameter("night_map", GD.Load<Texture2D>($"res://Assets/Planet/night_{face}.jpg"));
            material.SetShaderParameter("cloud_map", cloud);

            material.SetShaderParameter("rock_colour", rockColour);
            material.SetShaderParameter("rock_normal", rockNormal);
            material.SetShaderParameter("soil_colour", soilColour);
            material.SetShaderParameter("soil_normal", soilNormal);
            material.SetShaderParameter("wave_normal", waveNormal);

            material.SetShaderParameter("planet_radius", radius);
            material.SetShaderParameter("cloud_altitude", ShadowDeck);
            material.SetShaderParameter("sun_direction", sunDirection);

            _faces[face] = material;

        }

    }

    /// <summary>Live shader tuning from the debug bridge; scalars, or comma-separated vectors.</summary>
    public bool Tune(string target, string parameter, string value) {

        string[] parts = value.Split(',');

        Variant setting = parts.Length == 3
            ? new Vector3(parts[0].ToFloat(), parts[1].ToFloat(), parts[2].ToFloat())
            : value.ToFloat();

        if (target == "surface" || target == "ground") {

            foreach (ShaderMaterial face in _faces) {

                face.SetShaderParameter(parameter, setting);

            }

            return true;

        }

        ShaderMaterial material = target switch {

            "clouds" => _clouds,
            "atmosphere" => _atmosphere,

            _ => null,

        };

        if (material == null) {

            return false;

        }

        material.SetShaderParameter(parameter, setting);

        return true;

    }

    public void Sync(double time, Vector3d eye) {

        Vector3 centre = Frames.Point(Vector3d.Zero);

        _deck.Position = centre;

        if (_air != null) {

            _air.Position = centre;

        }

        _ground.Sync(time, eye);

        float deck = (float)(_body.SpinAt(time) * CloudRotationRatio);

        foreach (ShaderMaterial face in _faces) {

            face.SetShaderParameter("planet_centre", centre);
            face.SetShaderParameter("cloud_spin", deck);

        }

        _clouds.SetShaderParameter("planet_centre", centre);
        _clouds.SetShaderParameter("cloud_spin", deck);
        _clouds.SetShaderParameter("drift", (float)(time * CloudDrift));

        _atmosphere.SetShaderParameter("planet_centre", centre);

    }

    // The frequency is in voxels of the volume itself, not in metres: the shader decides how many
    // metres a tile of it covers. Godot generates these on its own threads at load, which is a
    // volume that never has to be built by a tool or carried in the repository.
    private static NoiseTexture3D Volume(int size, float frequency, int octaves) {

        FastNoiseLite noise = new FastNoiseLite {

            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = frequency,

            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = octaves,
            FractalGain = 0.55f,

        };

        return new NoiseTexture3D {

            Noise = noise,

            Width = size,
            Height = size,
            Depth = size,

            Seamless = true,
            Normalize = true,

        };

    }

    private static MeshInstance3D Shell(string name, float radius, int segments, int rings, Material material) {

        SphereMesh mesh = new SphereMesh {

            Radius = radius,
            Height = radius * 2.0f,

            RadialSegments = segments,
            Rings = rings,

        };

        return new MeshInstance3D {

            Name = name,
            Mesh = mesh,

            MaterialOverride = material,

            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

        };

    }

}
