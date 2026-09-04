using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>One frame: integrate, rebase the origin, then every view reads; nothing else ticks.</summary>
public sealed partial class Main : Node3D {

    public static readonly Vector3 SunDirection = new Vector3(0.93f, 0.20f, 0.31f).Normalized();

    private const float EarthshineEnergy = 0.26f;

    // Wide enough to hold the vessel and its plume; the probe is a mirror of the planet, not a room.
    private const float ProbeExtent = 48.0f;

    // The vessel is the only shadow caster, so the cascade only has to cover the chase arm and a little slack.
    private const float ShadowSlack = 45.0f;

    private Flight _flight;
    private Planet _planet;
    private LaunchComplex _complex;
    private VesselView _vessel;
    private MapView _map;
    private Hud _hud;
    private OrbitCamera _camera;

    private DirectionalLight3D _sun;
    private DirectionalLight3D _earthshine;
    private ReflectionProbe _earthlight;
    private WorldEnvironment _environment;

    private readonly Dictionary<Vessel, VesselView> _debris = new Dictionary<Vessel, VesselView>();

    public override void _Ready() {

        _flight = GetNode<Flight>("Flight");
        _planet = GetNode<Planet>("Planet");
        _complex = GetNode<LaunchComplex>("Complex");
        _vessel = GetNode<VesselView>("Vessel");
        _map = GetNode<MapView>("Map");
        _hud = GetNode<Hud>("Hud");
        _camera = GetNode<OrbitCamera>("CameraRig");

        _sun = GetNode<DirectionalLight3D>("Sun");
        _earthshine = GetNode<DirectionalLight3D>("Earthshine");
        _earthlight = GetNode<ReflectionProbe>("Earthlight");
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
        _sun.ShadowNormalBias = 0.8f;
        _sun.ShadowBlur = 0.35f;

        _earthshine.LightColor = new Color(0.62f, 0.72f, 0.88f);
        _earthshine.ShadowEnabled = false;

        // The sky is a star map, so on its own it leaves a metal nothing to mirror. In low orbit the
        // planet is the brightest thing in the scene and belongs in the reflection, not just the diffuse.
        _earthlight.Size = new Vector3(ProbeExtent, ProbeExtent, ProbeExtent);
        _earthlight.MaxDistance = 0.0f;
        _earthlight.UpdateMode = ReflectionProbe.UpdateModeEnum.Always;
        _earthlight.AmbientMode = ReflectionProbe.AmbientModeEnum.Disabled;
        _earthlight.BoxProjection = false;
        _earthlight.EnableShadows = false;
        _earthlight.CullMask = 1;
        _earthlight.Intensity = 1.0f;

        _environment.Environment = BuildEnvironment();

        _planet.Build(_flight.Body, SunDirection);
        _complex.Build(_flight.Body, _flight.Site);
        _vessel.Build(_flight.Vessel);
        _hud.Build(_flight);

        _flight.Staged += Release;
        _flight.Scrubbed += Scrub;
        _flight.VesselChanged += SelectVessel;

        Vector3 nadir = -Frames.Direction(_flight.Vessel.Position.Normalized);
        Vector3 prograde = Frames.Direction(_flight.Vessel.Velocity.Normalized);

        _camera.AimAt((nadir * 0.52f + prograde * 0.86f).Normalized());

        Step(0.0);

    }

    public override void _Process(double delta) {

        Step(delta);

    }

    /// <summary>A stage that has just come away takes its own geometry with it, so what flies off is
    /// exactly what was bolted on.</summary>
    private void Release(Vessel debris) {

        VesselView view = _vessel.Hand(debris);

        if (view == null) {

            return;

        }

        AddChild(view);

        _debris[debris] = view;

    }

    private void SelectVessel(Vessel previous, Vessel selected) {

        VesselView view = _debris[selected];
        _debris.Remove(selected);
        _debris[previous] = _vessel;
        _vessel = view;
        _vessel.MakeActive();

    }

    private void Scrub(Vessel debris) {

        if (!_debris.Remove(debris, out VesselView view)) {

            return;

        }

        view.QueueFree();

    }

    /// <summary>Milliseconds the last simulation step took, for the debug bridge.</summary>
    public double FlightMilliseconds { get; private set; }

    private void Step(double delta) {

        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        _flight.Advance(delta);

        FlightMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        Vector3 up = Frames.Direction(_flight.Vessel.Position.Normalized);

        _earthshine.LookAtFromPosition(Vector3.Zero, up, Mathf.Abs(up.Y) > 0.99f ? Vector3.Right : Vector3.Up);
        _earthshine.LightEnergy = EarthshineEnergy * Mathf.Max(up.Dot(SunDirection), 0.0f);

        _sun.DirectionalShadowMaxDistance = _camera.Distance + ShadowSlack;

        Vector3 focus = Frames.Point(_flight.Vessel.Position);

        _vessel.Visible = _flight.Vessel.Intact;

        _vessel.Sync(focus, Frames.Rotation(_flight.Vessel.Orientation));

        SyncDebris(focus);

        _earthlight.Position = focus;

        // Two metres of clearance over whatever the ground is doing under the vehicle, which is
        // close enough to the ground under the camera at any arm length it can be swung to.
        float clearance = (float)(_flight.Body.SurfaceRadiusUnder(_flight.Vessel.Position, _flight.Time) + 2.0);

        _camera.Sync(focus, Frames.Point(Vector3d.Zero), clearance);

        // The ground subdivides towards whoever is looking at it, and culls what is under their
        // horizon - so it has to be the camera actually rendering, or the map frames a whole planet
        // and gets back only the hemisphere the vehicle can see.
        Vector3 viewpoint = _map.Open ? _map.Camera.GlobalPosition : _camera.Eye;

        Vector3d eye = Frames.Origin + Frames.Sim(viewpoint);

        _planet.Sync(_flight.Time, eye);
        _complex.Sync(_flight.Time, eye);

        _map.Sync(delta);

        _hud.Sync();

    }

    // A spent stage far enough out is a dot the floating origin can no longer hold steady, so it is
    // left to the map rather than drawn as a jittering speck.
    private void SyncDebris(Vector3 focus) {

        foreach (KeyValuePair<Vessel, VesselView> entry in _debris) {

            Vector3 at = Frames.Point(entry.Key.Position);

            bool near = at.DistanceSquaredTo(focus) < Flight.DebrisRange * Flight.DebrisRange;

            entry.Value.Visible = near;

            if (near) {

                entry.Value.Sync(at, Frames.Rotation(entry.Key.Orientation));

            }

        }

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

            // Sunlit ground has an albedo near a third, and at unit exposure a third of the way up
            // an ACES curve with six stops of headroom is a dark photograph. The headroom is there
            // for the plume, not for the daylight.
            TonemapExposure = 1.55f,

            GlowEnabled = true,
            GlowIntensity = 0.55f,
            GlowStrength = 1.0f,
            GlowBloom = 0.05f,
            GlowHdrThreshold = 1.1f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen,

        };

    }

}
