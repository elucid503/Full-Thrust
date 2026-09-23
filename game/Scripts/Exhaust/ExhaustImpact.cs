using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

// Surface-anchored billows stay behind as the vessel climbs and the scene origin moves.
public sealed partial class ExhaustImpact : Node3D {

    private const int Steps = 14;
    private const float ReachRadii = 70.0f;

    private static ImageTexture _puff;
    private static NoiseTexture3D _volumeNoise;

    private GpuParticles3D _smoke;
    private GpuParticles3D _spray;
    private ParticleProcessMaterial _dust;
    private ParticleProcessMaterial _steam;
    private ShaderMaterial _smokeSkin;
    private StandardMaterial3D _spraySkin;
    private PlumeTemplate _template;
    private float _bellRadius;
    private bool _water;
    private bool _anchored;
    private Vector3d _anchor;
    private Basis _cloudBasis;

    public bool Active { get; private set; }

    public float Strength { get; private set; }
    public bool Water => _water;
    public Vector3d Point { get; private set; }

    public static ExhaustImpact Create(PlumeTemplate template, float bellRadius, int nozzles) {

        ExhaustImpact impact = new ExhaustImpact { Name = "Impact", TopLevel = true, _template = template, _bellRadius = bellRadius };

        impact._dust = impact.Cloud(new Vector3(0.0f, 0.25f, 0.0f), 3.5f);
        impact._steam = impact.Cloud(new Vector3(0.0f, 1.4f, 0.0f), 5.0f);

        impact._smokeSkin = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Exhaust/Smoke.gdshader"), RenderPriority = 1 };
        _volumeNoise ??= new NoiseTexture3D {

            Width = 48, Height = 48, Depth = 48, Seamless = true,
            Noise = new FastNoiseLite { Seed = 1204, Frequency = 0.075f, FractalOctaves = 3 },

        };
        impact._smokeSkin.SetParameter("flow_noise", _volumeNoise);
        impact._smokeSkin.SetParameter("tint", template.SmokeColour);
        impact._smoke = impact.Emitter("Smoke", impact._dust, impact._smokeSkin, Math.Min(48 * nozzles, 256), 10.0f, bellRadius * 2.4f);

        impact._spraySkin = Skin(0.2f);
        impact._spraySkin.AlbedoColor = new Color(0.82f, 0.90f, 0.98f, 0.85f);
        impact._spray = impact.Emitter("Spray", impact.Droplets(), impact._spraySkin, Math.Min(220 * nozzles, 900), 1.8f, bellRadius * 0.45f);

        impact._smoke.DrawPass1 = new BoxMesh { Size = Vector3.One, Material = impact._smokeSkin };
        impact.AddChild(impact._smoke);
        impact.AddChild(impact._spray);

        return impact;

    }

    internal static ImageTexture PuffTexture => _puff ??= Puff();

    // Spray stays cheap; only the smoke and steam use volume shading.
    private static ImageTexture Puff() {

        const int size = 128;
        FastNoiseLite noise = new FastNoiseLite { Seed = 1204, Frequency = 0.045f, FractalOctaves = 3 };
        using Image image = Image.CreateEmpty(size, size, true, Image.Format.Rgba8);

        for (int y = 0; y < size; y++) {

            for (int x = 0; x < size; x++) {

                float dx = (x + 0.5f) / size * 2.0f - 1.0f;
                float dy = (y + 0.5f) / size * 2.0f - 1.0f;
                float radial = Mathf.Sqrt(dx * dx + dy * dy);
                float grain = 0.5f + 0.5f * noise.GetNoise2D(x, y);
                float edge = Mathf.Clamp(1.0f - radial / (0.55f + 0.45f * grain), 0.0f, 1.0f);
                float alpha = edge * edge * (3.0f - 2.0f * edge);
                float shade = 0.82f + 0.18f * grain;
                image.SetPixel(x, y, new Color(shade, shade, shade, alpha));

            }

        }

        image.GenerateMipmaps();

        return ImageTexture.CreateFromImage(image);

    }

    internal static StandardMaterial3D Skin(float fade) {

        return new StandardMaterial3D {

            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = PuffTexture,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            ProximityFadeEnabled = true,
            ProximityFadeDistance = fade,
            RenderPriority = 1,

        };

    }

    // Puffs start at a few bell radii and swell to a building's size: the cloud is its overlap, not its particles.
    private ParticleProcessMaterial Cloud(Vector3 lift, float growth) {

        return new ParticleProcessMaterial {

            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = _bellRadius * 3.0f,
            EmissionRingInnerRadius = _bellRadius * 1.0f,
            EmissionRingHeight = _bellRadius * 2.0f,

            Direction = Vector3.Up,
            Spread = 50.0f,
            InitialVelocityMin = _bellRadius * 3.0f,
            InitialVelocityMax = _bellRadius * 7.0f,
            RadialVelocityMin = _bellRadius * 10.0f,
            RadialVelocityMax = _bellRadius * 18.0f,
            RadialVelocityCurve = Ramp(new[] { (0.0f, 1.0f), (0.2f, 0.3f), (0.5f, 0.0f), (1.0f, 0.0f) }),
            Gravity = lift,
            DampingMin = 6.0f,
            DampingMax = 9.0f,
            DampingCurve = Ramp(new[] { (0.0f, 1.0f), (0.3f, 0.9f), (0.5f, 0.0f), (1.0f, 0.0f) }),
            LifetimeRandomness = 0.35f,

            ScaleMin = _bellRadius * 12.0f,
            ScaleMax = _bellRadius * 18.0f,
            ScaleCurve = Ramp(new[] { (0.0f, 0.55f), (0.08f, 1.0f), (1.0f, growth) }),
            AngleMin = -180.0f,
            AngleMax = 180.0f,
            AngularVelocityMin = -14.0f,
            AngularVelocityMax = 14.0f,

            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 1.4f,
            TurbulenceNoiseScale = Mathf.Max(_bellRadius * 12.0f, 4.0f),
            TurbulenceNoiseSpeed = new Vector3(0.0f, 0.2f, 0.0f),
            TurbulenceInfluenceMin = 0.08f,
            TurbulenceInfluenceMax = 0.18f,

            ColorRamp = Fade(new[] { (0.0f, 0.0f), (0.06f, 0.85f), (0.45f, 0.7f), (1.0f, 0.0f) }),

        };

    }

    private ParticleProcessMaterial Droplets() {

        return new ParticleProcessMaterial {

            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = _bellRadius * 1.6f,
            EmissionRingInnerRadius = _bellRadius * 0.4f,
            EmissionRingHeight = _bellRadius * 0.2f,

            Direction = Vector3.Up,
            Spread = 35.0f,
            InitialVelocityMin = _bellRadius * 9.0f,
            InitialVelocityMax = _bellRadius * 22.0f,
            RadialVelocityMin = _bellRadius * 18.0f,
            RadialVelocityMax = _bellRadius * 34.0f,
            Gravity = new Vector3(0.0f, -(float)Sim.Vessel.StandardGravity, 0.0f),
            LifetimeRandomness = 0.4f,

            ScaleMin = _bellRadius * 1.5f,
            ScaleMax = _bellRadius * 3.0f,
            ScaleCurve = Ramp(new[] { (0.0f, 0.5f), (0.3f, 1.0f), (1.0f, 1.6f) }),

            ColorRamp = Fade(new[] { (0.0f, 0.0f), (0.1f, 0.85f), (0.6f, 0.5f), (1.0f, 0.0f) }),

        };

    }

    private GpuParticles3D Emitter(string name, ParticleProcessMaterial process, Material skin, int amount, float life, float extent) {

        return new GpuParticles3D {

            Name = name,
            Amount = amount,
            Lifetime = life,
            LocalCoords = true,
            Emitting = false,
            FixedFps = 30,
            Interpolate = true,
            DrawOrder = GpuParticles3D.DrawOrderEnum.ViewDepth,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = skin },
            VisibilityAabb = new Aabb(Vector3.One * -(extent * 40.0f + 80.0f), Vector3.One * (extent * 80.0f + 160.0f)),
            Layers = 2,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

        };

    }

    internal static CurveTexture Ramp((float, float)[] points) {

        Curve curve = new Curve { MaxValue = 8.0f };

        foreach ((float x, float y) in points) {

            curve.AddPoint(new Vector2(x, y));

        }

        return new CurveTexture { Curve = curve, Width = 64 };

    }

    internal static GradientTexture1D Fade((float, float)[] alphas) {

        Gradient gradient = new Gradient { InterpolationMode = Gradient.InterpolationModeEnum.Cubic };
        gradient.RemovePoint(1);
        gradient.SetOffset(0, alphas[0].Item1);
        gradient.SetColor(0, new Color(1.0f, 1.0f, 1.0f, alphas[0].Item2));

        for (int index = 1; index < alphas.Length; index++) {

            gradient.AddPoint(alphas[index].Item1, new Color(1.0f, 1.0f, 1.0f, alphas[index].Item2));

        }

        return new GradientTexture1D { Gradient = gradient, Width = 64 };

    }

    /// <summary>Casts the stage's combined exhaust down onto the body and drives the emitters
    /// from how hard it lands. Sim positions are inertial doubles; the emitter is placed in scene floats.</summary>
    public void Sync(CelestialBody body, double time, Vector3d exit, Vector3d axis, float power, Vector3 wind, Vector3 sunDirection) {

        // Follow the rotating surface while the scene origin follows the vessel.
        Vector3d anchor = _anchored ? body.ToInertial(_anchor, time) : Vector3d.Zero;
        if (_anchored) { GlobalTransform = new Transform3D(_cloudBasis, Frames.Point(anchor)); }
        _smokeSkin.SetParameter("effect_time", (float)(time % 4096.0));

        float reach = _bellRadius * ReachRadii;
        double hit = Hit(body, time, exit, axis, reach);

        if (!Plume.Enabled || power <= 0.001f || hit < 0.0) {

            Stop();
            return;

        }

        Vector3d point = exit + axis * hit;
        Vector3d up = point.Normalized;
        float strength = power * Mathf.Clamp(1.0f - (float)hit / reach, 0.0f, 1.0f) * _template.SmokeAmount;
        strength *= Mathf.SmoothStep(0.0f, 0.35f, strength);

        if (strength <= 0.01f) {

            Stop();
            return;

        }

        bool water = body.Terrain != null && body.Terrain.Elevation(body.ToBodyFixed(point, time)) < 0.0;

        if (water != _water || !Active) {

            _water = water;
            _smoke.ProcessMaterial = water ? _steam : _dust;

        }

        Vector3 vertical = Frames.Direction(up);
        Vector3 side = Mathf.Abs(vertical.Y) > 0.99f ? Vector3.Right : Vector3.Up.Cross(vertical).Normalized();
        if (!_anchored || (point - anchor).Length > 1000.0) {

            _anchor = body.ToBodyFixed(point, time);
            anchor = point;
            _cloudBasis = new Basis(side, vertical, side.Cross(vertical));
            _anchored = true;
            _smoke.Restart();
            _spray.Restart();

        }

        GlobalTransform = new Transform3D(_cloudBasis, Frames.Point(anchor));
        Vector3 emission = _cloudBasis.Inverse() * Frames.Direction(point - anchor) + Vector3.Up * (_bellRadius * 1.5f);
        _dust.EmissionShapeOffset = emission;
        _steam.EmissionShapeOffset = emission;
        ((ParticleProcessMaterial)_spray.ProcessMaterial).EmissionShapeOffset = emission;

        Vector3 drift = wind - vertical * wind.Dot(vertical);
        ParticleProcessMaterial cloud = water ? _steam : _dust;
        cloud.Gravity = _cloudBasis.Inverse() * (vertical * (water ? 1.1f : 0.15f) + drift * 0.08f);

        // The volume shader attenuates sunlight through each rolling billow.
        float daylight = Mathf.Clamp(vertical.Dot(sunDirection) * 2.5f + 0.3f, 0.12f, 1.0f);
        Color tint = water ? new Color(0.94f, 0.96f, 0.99f) : _template.SmokeColour;
        Color glow = _template.LightAirColour * (0.25f * strength * (1.0f - daylight));
        _smokeSkin.SetParameter("tint", tint);
        _smokeSkin.SetParameter("sun_direction", sunDirection);
        _smokeSkin.SetParameter("daylight", daylight);
        _smokeSkin.SetParameter("flame_light", glow);

        _spraySkin.AlbedoColor = new Color(0.82f, 0.90f, 0.98f, 0.85f) * Mathf.Max(daylight, 0.25f);

        if (water) {

            // The jet digs a crater its own width and several deep at full thrust, capped at a few metres.
            Planet.Active?.Wakes.Jet(point, time, _bellRadius * (3.0 + (float)hit / _bellRadius * 0.12f), Math.Min(strength * _bellRadius * 1.2f, 2.5f), strength);

        }

        _smoke.AmountRatio = strength;
        _smoke.Emitting = true;
        _spray.Emitting = water;
        _spray.AmountRatio = strength;
        Strength = strength;
        Point = point;
        Active = true;

    }

    private void Stop() {

        Strength = 0.0f;

        if (!Active) {

            return;

        }

        _smoke.Emitting = false;
        _spray.Emitting = false;
        Active = false;

    }

    // Where the axis meets the surface, or -1 when it does not within reach. The height above the
    // surface is bisected along the axis; the ground is locally flat, so a dozen halvings suffice.
    private static double Hit(CelestialBody body, double time, Vector3d exit, Vector3d axis, double reach) {

        double Height(double t) {

            Vector3d point = exit + axis * t;
            return point.Length - body.SurfaceRadiusUnder(point, time);

        }

        if (Height(0.0) <= 0.0) {

            return 0.0;

        }

        if (Height(reach) > 0.0) {

            return -1.0;

        }

        double low = 0.0;
        double high = reach;

        for (int step = 0; step < Steps; step++) {

            double middle = (low + high) * 0.5;

            if (Height(middle) > 0.0) {

                low = middle;

            }
            else {

                high = middle;

            }

        }

        return (low + high) * 0.5;

    }

}
