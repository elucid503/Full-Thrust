using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>Terra: surface, cloud deck, scattering shell. Reads the body; holds no sim state.</summary>
public sealed partial class Planet : Node3D {

    private const float CloudAltitude = 10000.0f;
    private const float AtmosphereDepth = 62000.0f;

    // The deck turns fractionally faster than the ground, so cloud shadows creep rather than lock.
    private const double CloudRotationRatio = 1.02;

    private const int SurfaceSegments = 512;
    private const int SurfaceRings = 256;

    public static Planet Active { get; private set; }

    private CelestialBody _body;

    private Node3D _ground;
    private Node3D _deck;

    private ShaderMaterial _surface;
    private ShaderMaterial _clouds;
    private ShaderMaterial _atmosphere;

    public void Build(CelestialBody body, Vector3 sunDirection) {

        Active = this;

        _body = body;

        float radius = (float)body.Radius;

        Texture2D albedo = GD.Load<Texture2D>("res://Assets/Planet/albedo.jpg");
        Texture2D terrain = GD.Load<Texture2D>("res://Assets/Planet/terrain.png");
        Texture2D night = GD.Load<Texture2D>("res://Assets/Planet/night.png");
        Texture2D cloud = GD.Load<Texture2D>("res://Assets/Planet/clouds.jpg");

        _surface = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Surface.gdshader") };

        _surface.SetShaderParameter("albedo_map", albedo);
        _surface.SetShaderParameter("terrain_map", terrain);
        _surface.SetShaderParameter("night_map", night);
        _surface.SetShaderParameter("cloud_map", cloud);

        _surface.SetShaderParameter("planet_radius", radius);
        _surface.SetShaderParameter("cloud_altitude", CloudAltitude);
        _surface.SetShaderParameter("sun_direction", sunDirection);

        _clouds = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Clouds.gdshader") };

        _clouds.SetShaderParameter("cloud_map", cloud);
        _clouds.SetShaderParameter("sun_direction", sunDirection);
        _clouds.RenderPriority = 1;

        _atmosphere = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Atmosphere.gdshader") };

        _atmosphere.SetShaderParameter("planet_radius", radius);
        _atmosphere.SetShaderParameter("atmosphere_radius", radius + AtmosphereDepth);
        _atmosphere.SetShaderParameter("sun_direction", sunDirection);
        _atmosphere.RenderPriority = 2;

        _ground = new Node3D { Name = "Ground" };
        _deck = new Node3D { Name = "Deck" };

        AddChild(_ground);
        AddChild(_deck);

        _ground.AddChild(Shell("Surface", radius, SurfaceSegments, SurfaceRings, _surface));
        _deck.AddChild(Shell("Clouds", radius + CloudAltitude, 384, 192, _clouds));

        AddChild(Shell("Atmosphere", radius + AtmosphereDepth, 64, 32, _atmosphere));

    }

    /// <summary>Live shader tuning from the debug bridge; scalars, or comma-separated vectors.</summary>
    public bool Tune(string target, string parameter, string value) {

        ShaderMaterial material = target switch {

            "surface" => _surface,
            "clouds" => _clouds,
            "atmosphere" => _atmosphere,

            _ => null,

        };

        if (material == null) {

            return false;

        }

        string[] parts = value.Split(',');

        if (parts.Length == 3) {

            material.SetShaderParameter(parameter, new Vector3(parts[0].ToFloat(), parts[1].ToFloat(), parts[2].ToFloat()));

        }
        else {

            material.SetShaderParameter(parameter, value.ToFloat());

        }

        return true;

    }

    public void Sync(double time) {

        Position = Frames.Point(Vector3d.Zero);

        double turn = Mathf.Tau * time / _body.RotationPeriodSeconds;

        _ground.Rotation = new Vector3(0.0f, (float)turn, 0.0f);
        _deck.Rotation = new Vector3(0.0f, (float)(turn * CloudRotationRatio), 0.0f);

        _surface.SetShaderParameter("cloud_drift", (float)(-turn * (CloudRotationRatio - 1.0) / Mathf.Tau));
        _atmosphere.SetShaderParameter("planet_centre", Position);

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
