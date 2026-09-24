using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>What one stage's merged far field responds to this frame. Ground values are scene
/// positions and directions; the distance is metres along the tail's axis to where it strikes.</summary>
public struct TailInputs {

    public float Throttle;
    public float Air;
    public float Expansion;

    public Vector3 Bend;
    public float Stretch;

    public bool Grounded;
    public Vector3 GroundPoint;
    public Vector3 GroundNormal;
    public float GroundDistance;

    public float EffectTime;

}

// One marched volume per stage. Each nozzle's Waterfall stack hands over to it a few exit radii
// downstream, where the streams have begun to mix; in vacuum the flow stays laminar and it vanishes.
public sealed partial class ExhaustTail : Node3D {

    private const float MinimumStrength = 0.01f;
    private const float SmokeReach = 1.0f;
    private const float WallReachRadii = 30.0f;

    private static Shader _shader;
    private static NoiseTexture3D _noise;
    private static int _seeds;

    private readonly PlumeContact _contact = new();
    private readonly Vector4[] _exits = new Vector4[32];
    private readonly Vector4[] _directions = new Vector4[32];

    private PlumeTemplate _template;
    private MeshInstance3D _volume;
    private ShaderMaterial _material;
    private float _bellRadius;
    private float _ring;
    private int _count;
    private float _top;

    public Vessel Source { get; set; }

    public static ExhaustTail Create(PlumeTemplate template, float bellRadius, float ring) {

        _shader ??= GD.Load<Shader>("res://Shaders/Exhaust/Tail.gdshader");
        _noise ??= new NoiseTexture3D {

            Width = 64,
            Height = 64,
            Depth = 64,
            Seamless = true,
            SeamlessBlendSkirt = 0.25f,
            Noise = new FastNoiseLite { Seed = 7331, Frequency = 0.07f, FractalOctaves = 3 },

        };

        ExhaustTail tail = new ExhaustTail { Name = "Tail", _template = template, _bellRadius = bellRadius, _ring = ring };

        tail._material = new ShaderMaterial { Shader = _shader, RenderPriority = 2 };
        tail._material.SetParameter("flow_noise", _noise);
        tail._material.SetParameter("seed", (float)(_seeds++ * 7.31 % 97.0));

        tail._volume = new MeshInstance3D {

            Name = "Volume",
            Mesh = new BoxMesh { Size = Vector3.One },
            MaterialOverride = tail._material,
            Layers = 2,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            IgnoreOcclusionCulling = true,

        };

        tail.AddChild(tail._volume);
        tail._volume.Visible = false;

        return tail;

    }

    public void Use(PlumeTemplate template) => _template = template;

    public void BeginStreams() {

        _count = 0;
        _top = 0.0f;

    }

    public void AddStream(Plume nozzle, float power) {

        if (power <= 0.001f || _count == _exits.Length) {

            return;

        }

        Transform3D local = GlobalTransform.AffineInverse() * nozzle.GlobalTransform;
        Vector3 direction = -local.Basis.Y.Normalized();
        _exits[_count] = new Vector4(local.Origin.X, local.Origin.Y, local.Origin.Z, nozzle.ExitRadius);
        _directions[_count++] = new Vector4(direction.X, direction.Y, direction.Z, power);
        _top = Mathf.Max(_top, local.Origin.Y + nozzle.ExitRadius);

    }

    public void Drive(in TailInputs inputs) {

        float atmosphere = Mathf.SmoothStep(0.02f, 0.35f, inputs.Air);
        float strength = inputs.Throttle * atmosphere * (1.0f - Mathf.SmoothStep(0.6f, 0.9f, inputs.Expansion));

        // Thin air lets the jet run longer and balloon before it mixes out.
        float thin = 1.0f - Mathf.Clamp(inputs.Air, 0.0f, 1.0f);
        float radius = _ring > 0.0f ? (_ring + _bellRadius) * 0.85f : _bellRadius;
        float length = _template.TailLength * radius * Mathf.Sqrt(Mathf.Max(inputs.Throttle, 0.05f)) * (1.0f + 0.8f * thin) * inputs.Stretch;
        float spread = _template.TailSpread * (1.0f + 1.5f * thin);
        float reach = length * SmokeReach;
        _volume.Visible = Plume.Enabled && _template.TailLength > 0.0f && _count > 0 && strength > MinimumStrength;

        if (!_volume.Visible) {

            return;

        }

        _contact.Sync(this, radius, Source);

        Vector3 bend = inputs.Bend * length;
        float free = reach;
        float ground = -1.0f;
        Vector3 point = Vector3.Zero;
        Vector3 normal = Vector3.Up;

        if (inputs.Grounded && inputs.GroundDistance > 0.0f && inputs.GroundDistance < reach) {

            ground = inputs.GroundDistance;
            point = ToLocal(inputs.GroundPoint);
            normal = (GlobalBasis.Inverse() * inputs.GroundNormal).Normalized();
            free = Mathf.Min(reach, ground + (radius + spread * ground) * 2.0f);

        }

        float wide = radius + spread * free + bend.Length() + _ring;
        Vector3 low = new Vector3(-wide, -free, -wide);
        Vector3 high = new Vector3(wide, Mathf.Max(_top, 0.0f), wide);

        if (ground > 0.0f) {

            // The wall jet's disc along the ground, and the height its lifting edge reaches.
            float impact = radius + spread * ground;
            float across = Mathf.Min(reach - ground, WallReachRadii * radius) + impact;
            float thickness = impact * 0.45f + 0.08f * across;
            float rise = thickness * 2.4f + across * across / Mathf.Max(length, 0.001f) * 0.25f;
            Vector3 disc = new Vector3(
                across * Mathf.Sqrt(Mathf.Max(1.0f - normal.X * normal.X, 0.0f)),
                across * Mathf.Sqrt(Mathf.Max(1.0f - normal.Y * normal.Y, 0.0f)),
                across * Mathf.Sqrt(Mathf.Max(1.0f - normal.Z * normal.Z, 0.0f)));
            Vector3 lifted = normal * rise;
            low = low.Min(point - disc + lifted.Min(Vector3.Zero));
            high = high.Max(point + disc + lifted.Max(Vector3.Zero));

        }

        low -= Vector3.One * _contact.Padding;
        high += Vector3.One * _contact.Padding;

        float merge = _count > 1 ? _ring * 8.0f : 0.0f;

        _material.SetParameter("stream_count", _count);
        _material.SetParameter("stream_exits", _exits);
        _material.SetParameter("stream_directions", _directions);
        _material.SetParameter("jet_radius", radius);
        _material.SetParameter("jet_length", length);
        _material.SetParameter("spread", spread);
        _material.SetParameter("merge_distance", merge);
        _material.SetParameter("handover", _template.TailHandover * _bellRadius);
        _material.SetParameter("crossflow", bend);
        _material.SetParameter("flow_speed", _template.TailSpeed * radius * Mathf.Sqrt(inputs.Throttle));
        _material.SetParameter("turbulence", _template.TailTurbulence * Mathf.SmoothStep(0.0f, 0.3f, inputs.Air));
        _material.SetParameter("emission", _template.TailEmission * strength);
        _material.SetParameter("hot_colour", _template.TailHot);
        _material.SetParameter("cool_colour", _template.TailCool);
        _material.SetParameter("ground_point", point);
        _material.SetParameter("ground_normal", normal);
        _material.SetParameter("ground_distance", ground);
        _material.SetParameter("effect_time", inputs.EffectTime);
        _material.SetParameter("bounds_min", low);
        _material.SetParameter("bounds_max", high);
        _volume.CustomAabb = new Aabb(low, high - low);
        _contact.Write(_material, Vector3.Zero, reach);

    }

    public bool Tune(string parameter, string value) => Plume.Tune(_material, parameter, value);

}
