using System;
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

    // Keep nearby vegetation shadows bounded independently of a distant free camera.
    private const float ShadowSlack = 45.0f;

    private Flight _flight;
    private Planet _planet;
    private LaunchComplex _complex;
    private VesselView _vessel;
    private MapView _map;
    private Hud _hud;
    private OrbitCamera _camera;
    private FreeCamera _free;
    private DebugPanel _debug;

    private DirectionalLight3D _sun;
    private DirectionalLight3D _earthshine;
    private ReflectionProbe _earthlight;
    private WorldEnvironment _environment;

    private ShaderMaterial _starfield;
    private ulong _nextSkyUpdate;

    private readonly Dictionary<Vessel, VesselView> _debris = new Dictionary<Vessel, VesselView>();

    public override void _Ready() {

        SceneTransition.Begin(this);

        _flight = GetNode<Flight>("Flight");
        _planet = GetNode<Planet>("Planet");
        _complex = GetNode<LaunchComplex>("Complex");
        _vessel = GetNode<VesselView>("Vessel");
        _map = GetNode<MapView>("Map");
        _hud = GetNode<Hud>("Hud");
        _camera = GetNode<OrbitCamera>("CameraRig");
        _free = GetNode<FreeCamera>("FreeCamera");
        _debug = GetNode<DebugPanel>("Debug");

        _sun = GetNode<DirectionalLight3D>("Sun");
        _earthshine = GetNode<DirectionalLight3D>("Earthshine");
        _earthlight = GetNode<ReflectionProbe>("Earthlight");
        _environment = GetNode<WorldEnvironment>("WorldEnvironment");
        _environment.Compositor = null;

        _sun.LookAtFromPosition(Vector3.Zero, -SunDirection, Vector3.Up);

        _sun.LightEnergy = 1.0f;
        _sun.LightColor = new Color(1.0f, 0.973f, 0.941f);

        // Godot's frustum culler goes degenerate over a planet-sized scene, so the cascade is kept to the vessel.
        _sun.ShadowEnabled = true;
        _sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
        _sun.DirectionalShadowBlendSplits = true;
        _sun.DirectionalShadowFadeStart = 0.85f;
        _sun.ShadowBias = 0.06f;
        _sun.ShadowNormalBias = 0.8f;
        _sun.ShadowBlur = 0.35f;

        _earthshine.LightColor = new Color(0.62f, 0.72f, 0.88f);
        _earthshine.ShadowEnabled = false;

        // The sky is a star map, so on its own it leaves a metal nothing to mirror. In low orbit the
        // planet is the brightest thing in the scene and belongs in the reflection, not just the diffuse.
        _earthlight.Size = new Vector3(ProbeExtent, ProbeExtent, ProbeExtent);
        _earthlight.MaxDistance = 0.0f;
        // Amortize the six cubemap faces instead of redrawing the entire surrounding world each frame.
        _earthlight.UpdateMode = ReflectionProbe.UpdateModeEnum.Once;
        _earthlight.MeshLodThreshold = 4.0f;
        _earthlight.AmbientMode = ReflectionProbe.AmbientModeEnum.Disabled;
        _earthlight.BoxProjection = false;
        _earthlight.EnableShadows = false;
        _earthlight.CullMask = 1;
        _earthlight.ReflectionMask = VesselView.HardwareLayer;
        _earthlight.Intensity = 1.0f;

        _environment.Environment = BuildEnvironment();
        _environment.CameraAttributes = new CameraAttributesPractical {

            ExposureMultiplier = 1.0f,

            AutoExposureEnabled = true,
            AutoExposureMinSensitivity = 160.0f,
            AutoExposureMaxSensitivity = 800.0f,
            AutoExposureScale = 0.30f,
            AutoExposureSpeed = 1.2f,

        };

        _planet.Build(_flight.Body, SunDirection);
        _complex.Build(_flight.Body, _flight.Site);
        _vessel.Build(_flight.Vessel);
        _hud.Build(_flight);
        _debug.Build(this, _flight, _free, _camera, _hud);
        AddChild(new GraphicsOptions { Name = "GraphicsOptions" });

        _flight.Staged += Release;
        _flight.Scrubbed += Scrub;
        _flight.VesselChanged += SelectVessel;

        Vector3 vertical = Frames.Direction(_flight.Site.UpAt(_flight.Body, _flight.Time));
        double bearing = 295.0 * Math.PI / 180.0;
        Vector3 along = Frames.Direction(_flight.Body.ToInertial(
            _flight.Site.North * Math.Cos(bearing) + _flight.Site.East * Math.Sin(bearing), _flight.Time));
        _camera.AimAt(along * Mathf.Cos(Mathf.DegToRad(12)) - vertical * Mathf.Sin(Mathf.DegToRad(12)), vertical);

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
    public double UpdateMilliseconds { get; private set; }
    public double HudMilliseconds { get; private set; }
    public double VesselMilliseconds { get; private set; }
    public double PlanetMilliseconds { get; private set; }

    private void Step(double delta) {

        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        if (!SceneTransition.Loading) { _flight.Advance(delta); }
        // Camera cuts and paused loading still need a precise floating origin.
        Frames.Anchor = FreeCamera.Flying ? _free.Where : null;
        Frames.Rebase(_flight.Vessel.Position);

        FlightMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        Vector3 up = Frames.Direction(_flight.Vessel.Position.Normalized);

        _earthshine.LookAtFromPosition(Vector3.Zero, up, Mathf.Abs(up.Y) > 0.99f ? Vector3.Right : Vector3.Up);
        _earthshine.LightEnergy = EarthshineEnergy * Mathf.Max(up.Dot(SunDirection), 0.0f);

        Vector3 focus = Frames.Point(_flight.Vessel.Position);

        _vessel.Visible = _flight.Vessel.Fate != VesselFate.BurnedUp;

        long vesselStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _vessel.Sync(focus, Frames.Rotation(_flight.Vessel.Orientation));

        SyncDebris(focus);
        VesselMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - vesselStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        _earthlight.Position = focus;

        // Two metres of clearance over whatever the ground is doing under the vehicle, which is
        // close enough to the ground under the camera at any arm length it can be swung to.
        float clearance = (float)(_flight.Body.SurfaceRadiusUnder(_flight.Vessel.Position, _flight.Time) + 2.0);

        _camera.Sync(focus, Frames.Point(Vector3d.Zero), clearance);

        _free.Fly(SceneTransition.Loading ? 0.0 : delta);
        _map.Sync(delta);

        // Two bounded cascades retain trunk/rock contact shadows without covering kilometres of scatter.
        _sun.DirectionalShadowMaxDistance = Mathf.Clamp(_camera.Distance + ShadowSlack, 180.0f, 300.0f);

        // The ground subdivides towards whoever is looking at it, and culls what is under their
        // horizon - so it has to be the camera actually rendering, or the map frames a whole planet
        // and gets back only the hemisphere the vehicle can see.
        Vector3 viewpoint = _map.Open ? _map.Camera.GlobalPosition
            : FreeCamera.Flying ? _free.GlobalPosition : _camera.Eye;

        // SSAO radius is world metres. At a few metres from the hull that kernel covers most of the
        // screen, which is both the close-up hitch and a contact shadow the size of the vehicle.
        if (_environment.Environment.SsaoEnabled) {

            float gap = (viewpoint - focus).Length();
            float reach = Mathf.SmoothStep(8.0f, 50.0f, gap);

            _environment.Environment.SsaoRadius = Mathf.Lerp(0.28f, 1.4f, reach);
            _environment.Environment.SsaoDetail = Mathf.Lerp(0.15f, 0.5f, reach);

        }

        Vector3d eye = Frames.Origin + Frames.Sim(viewpoint);

        SyncSky(eye);

        long planetStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        Vector3d flightEye = _map.ReturnToFreeCamera ? _free.Where : Frames.Origin + Frames.Sim(_camera.Eye);
        _planet.Sync(_flight.Time, eye, _map.Open ? flightEye : null);
        PlanetMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - planetStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _complex.Sync(_flight.Time, eye);

        long hudStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _hud.Sync();
        HudMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - hudStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        _debug.Sync();
        UpdateMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

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

    private void SyncSky(Vector3d eye) {

        double altitude = Math.Max(eye.Length - _flight.Body.Radius, 0.0);

        Vector3 up = Frames.Direction(eye.Normalized);

        float air = 1.0f - SmoothRange(0.0f, (float)_flight.Body.AtmosphereTop, (float)altitude);
        float day = SmoothRange(-0.16f, 0.06f, up.Dot(SunDirection));
        _environment.Environment.AmbientLightEnergy = Mathf.Lerp(0.045f, 0.26f, air * day);
        ulong now = Time.GetTicksMsec();

        // Sky radiance changes slowly; avoid regenerating the cubemap for subpixel horizon motion.
        if (now < _nextSkyUpdate) { return; }
        _nextSkyUpdate = now + 100;

        float lowerAtmosphere = 1.0f - SmoothRange((float)_flight.Body.AtmosphereTop * 0.75f,
            (float)_flight.Body.AtmosphereTop, (float)altitude);
        float starVisibility = (1.0f - day) * (1.0f - lowerAtmosphere * day);

        _starfield.SetShaderParameter("planet_up", up);
        _starfield.SetShaderParameter("sun_direction", SunDirection);
        _starfield.SetShaderParameter("atmosphere_amount", air);
        _starfield.SetShaderParameter("daylight", day);
        _starfield.SetShaderParameter("star_visibility", starVisibility * starVisibility);

    }

    private static float SmoothRange(float low, float high, float value) {

        float weight = Mathf.Clamp((value - low) / (high - low), 0.0f, 1.0f);

        return weight * weight * (3.0f - 2.0f * weight);

    }

    private Godot.Environment BuildEnvironment() {

        _starfield = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Sky.gdshader") };

        _starfield.SetShaderParameter("star_map", GD.Load<Texture2D>("res://Assets/Sky/stars.png"));

        return new Godot.Environment {

            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _starfield, RadianceSize = Sky.RadianceSizeEnum.Size128, ProcessMode = Sky.ProcessModeEnum.Incremental },

            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.44f, 0.52f, 0.64f),
            AmbientLightEnergy = 0.045f,

            // Ambient colour gives a metal nothing to mirror, so anything approaching fully metallic
            // renders black. The sky's radiance is a reflection source that costs the night side
            // none of the diffuse energy that raising ambient would.
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,

            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapAgxWhite = 8.0f,
            TonemapAgxContrast = 1.08f,
            TonemapExposure = 1.0f,

            SsaoEnabled = true,
            SsaoRadius = 1.4f,
            SsaoIntensity = 0.85f,
            SsaoPower = 1.25f,
            SsaoDetail = 0.5f,
            SsaoLightAffect = 0.0f,

            GlowEnabled = true,
            GlowIntensity = 0.32f,
            GlowStrength = 0.85f,
            GlowBloom = 0.0f,
            GlowHdrThreshold = 1.8f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen,

        };

    }

}
