using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>One frame: integrate, rebase the origin, then every view reads; nothing else ticks.</summary>
public sealed partial class Main : Node3D {

    public static readonly Vector3 SunDirection = new Vector3(0.93f, 0.20f, 0.31f).Normalized();

    private const float EarthshineEnergy = 0.26f;

    private Flight _flight;
    private Planet _planet;
    private OrbitCamera _camera;

    private DirectionalLight3D _sun;
    private DirectionalLight3D _earthshine;
    private WorldEnvironment _environment;

    public override void _Ready() {

        _flight = GetNode<Flight>("Flight");
        _planet = GetNode<Planet>("Planet");
        _camera = GetNode<OrbitCamera>("CameraRig");

        _sun = GetNode<DirectionalLight3D>("Sun");
        _earthshine = GetNode<DirectionalLight3D>("Earthshine");
        _environment = GetNode<WorldEnvironment>("WorldEnvironment");

        _sun.LookAtFromPosition(Vector3.Zero, -SunDirection, Vector3.Up);

        _sun.LightEnergy = 1.0f;
        _sun.LightColor = new Color(1.0f, 0.973f, 0.941f);
        _sun.ShadowEnabled = false;

        _earthshine.LightColor = new Color(0.62f, 0.72f, 0.88f);
        _earthshine.ShadowEnabled = false;

        _environment.Environment = BuildEnvironment();

        _planet.Build(_flight.Body, SunDirection);
        Vector3 nadir = -Frames.Direction(_flight.Vessel.Position.Normalized);
        Vector3 prograde = Frames.Direction(_flight.Vessel.Velocity.Normalized);

        _camera.AimAt((nadir * 0.52f + prograde * 0.86f).Normalized());

        Step(0.0);

    }

    public override void _Process(double delta) {

        Step(delta);

    }

    private void Step(double delta) {

        _flight.Advance(delta);

        _planet.Sync(_flight.Time);

        Vector3 up = Frames.Direction(_flight.Vessel.Position.Normalized);

        _earthshine.LookAtFromPosition(Vector3.Zero, up, Mathf.Abs(up.Y) > 0.99f ? Vector3.Right : Vector3.Up);
        _earthshine.LightEnergy = EarthshineEnergy * Mathf.Max(up.Dot(SunDirection), 0.0f);

        _camera.Sync(Frames.Point(_flight.Vessel.Position));

    }

    private static Godot.Environment BuildEnvironment() {

        ShaderMaterial starfield = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Sky.gdshader") };

        starfield.SetShaderParameter("star_map", GD.Load<Texture2D>("res://Assets/Sky/stars.png"));

        return new Godot.Environment {

            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = starfield, RadianceSize = Sky.RadianceSizeEnum.Size128, ProcessMode = Sky.ProcessModeEnum.Realtime },

            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.44f, 0.52f, 0.64f),
            AmbientLightEnergy = 0.03f,

            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapWhite = 6.0f,
            TonemapExposure = 1.0f,

            GlowEnabled = true,
            GlowIntensity = 0.55f,
            GlowStrength = 1.0f,
            GlowBloom = 0.05f,
            GlowHdrThreshold = 1.1f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen,

        };

    }

}
