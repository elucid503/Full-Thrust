using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>One frame: integrate, rebase the origin, then every view reads; nothing else ticks.</summary>
public sealed partial class Main : Node3D {

    public static readonly Vector3 SunDirection = new Vector3(0.93f, 0.20f, 0.31f).Normalized();

    private const float EarthshineEnergy = 0.26f;

    // The vessel is the only shadow caster, so the cascade only has to cover the chase arm and a little slack.
    private const float ShadowSlack = 45.0f;

    private Flight _flight;
    private Planet _planet;
    private VesselView _vessel;
    private Telemetry _telemetry;
    private OrbitCamera _camera;

    private DirectionalLight3D _sun;
    private DirectionalLight3D _earthshine;
    private WorldEnvironment _environment;

    public override void _Ready() {

        _flight = GetNode<Flight>("Flight");
        _planet = GetNode<Planet>("Planet");
        _vessel = GetNode<VesselView>("Vessel");
        _telemetry = GetNode<Telemetry>("Telemetry");
        _camera = GetNode<OrbitCamera>("CameraRig");

        _sun = GetNode<DirectionalLight3D>("Sun");
        _earthshine = GetNode<DirectionalLight3D>("Earthshine");
        _environment = GetNode<WorldEnvironment>("WorldEnvironment");

        _sun.LookAtFromPosition(Vector3.Zero, -SunDirection, Vector3.Up);

        _sun.LightEnergy = 1.0f;
        _sun.LightColor = new Color(1.0f, 0.973f, 0.941f);

        // Godot's frustum culler goes degenerate over a planet-sized scene, so the cascade is kept to the vessel.
        _sun.ShadowEnabled = true;
        _sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal;
        _sun.DirectionalShadowBlendSplits = false;
        _sun.DirectionalShadowFadeStart = 1.0f;
        _sun.ShadowBias = 0.06f;
        _sun.ShadowNormalBias = 1.6f;
        _sun.ShadowBlur = 1.2f;

        _earthshine.LightColor = new Color(0.62f, 0.72f, 0.88f);
        _earthshine.ShadowEnabled = false;

        _environment.Environment = BuildEnvironment();

        _planet.Build(_flight.Body, SunDirection);
        _vessel.Build(_flight.Vessel);

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

        _sun.DirectionalShadowMaxDistance = _camera.Distance + ShadowSlack;

        _vessel.Sync(Frames.Point(_flight.Vessel.Position), Frames.Rotation(_flight.Vessel.Orientation), _flight.Vessel.Throttle);

        _telemetry.Sync(_flight);

        _camera.Sync(Frames.Point(_flight.Vessel.Position));

    }

    private static Godot.Environment BuildEnvironment() {

        ShaderMaterial starfield = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Sky.gdshader") };

        starfield.SetShaderParameter("star_map", GD.Load<Texture2D>("res://Assets/Sky/stars.png"));

        return new Godot.Environment {

            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = starfield, RadianceSize = Sky.RadianceSizeEnum.Size256, ProcessMode = Sky.ProcessModeEnum.Realtime },

            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.44f, 0.52f, 0.64f),
            AmbientLightEnergy = 0.03f,

            // Ambient colour gives a metal nothing to mirror, so anything approaching fully metallic
            // renders black. The sky's radiance is a reflection source that costs the night side
            // none of the diffuse energy that raising ambient would.
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,

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
