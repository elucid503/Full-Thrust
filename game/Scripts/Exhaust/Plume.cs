using System;
using System.Collections.Generic;

using Godot;

namespace FullThrust.Game;

/// <summary>What one nozzle's flight state looks like to its plume this frame. Ratios are unitless;
/// the bend is the fraction of a layer's own length its tail is pushed sideways, in nozzle space.</summary>
public struct PlumeInputs {

    public bool Lit;
    public float Throttle;
    public float Air;
    public float Mach;
    public float Purge;
    public float Burnoff;
    public float LightShare;

    public float AmbientPressure;
    public float ExitPressure;

    public float Sunlit;
    public Vector3 Sun;

    public Vector3 Bend;
    public float Stretch;

    public float EffectTime;
    public float Delta;

}

// Waterfall's layered near field: analytic volumes flowing down -Y from the nozzle exit, each shaped
// by controller curves. The turbulent far field belongs to the stage's ExhaustTail.
public sealed partial class Plume : Node3D {

    private const float MinimumIntensity = 0.004f;
    private const float VacuumPressure = 30.0f;
    private const float OpenFloor = -4.0f;
    private const float CapEnd = -1.5f;
    private const float ExpansionSeconds = 1.5f;

    /// <summary>Hides every exhaust effect, so its whole cost can be measured against a bare scene.</summary>
    public static bool Enabled { get; set; } = true;

    private sealed class Layer {

        public PlumeLayer Definition;
        public MeshInstance3D Mesh;
        public ShaderMaterial Material;
        public PlumeModifier.Target[] Driven;
        public float[] Values = new float[PlumeModifier.TargetCount];

    }

    private static readonly BoxMesh Proxy = new() { Size = Vector3.One };
    private static readonly int InputCount = Enum.GetValues<PlumeModifier.Input>().Length;
    private static ImageTexture _noise;
    private static FastNoiseLite _random;
    private static Shader _volumetricShader;
    private static Shader _conesShader;
    private static Shader _distortionShader;
    private static int _seeds;

    private readonly List<Layer> _layers = new();
    private readonly List<StandardMaterial3D> _bell = new();
    private readonly PlumeContact _contact = new();

    private PlumeTemplate _template;
    private MeshInstance3D _distortion;
    private ShaderMaterial _distortionMaterial;
    private OmniLight3D _light;
    private float _exitRadius;
    private float _seed;
    private bool _wasLit;
    private float _ignitionAt = float.NegativeInfinity;
    private float _cutoffAt = float.NegativeInfinity;
    private float _heat;
    private float _expansion = -1.0f;

    public bool Burning { get; private set; }
    public float ExitRadius => _exitRadius;
    public FullThrust.Sim.Vessel Source { get; set; }

    /// <summary>The bell surfaces that glow with chamber heat; the owner hands them over.</summary>
    public List<StandardMaterial3D> Bell => _bell;

    /// <summary>Waterfall-style pressure controller: 0.25 is an ideally expanded nozzle, and each
    /// quarter either side is a decade of over- or under-expansion. Chamber pressure follows throttle.</summary>
    public static float Expansion(float exitPressure, float ambientPressure, float throttle) {

        if (ambientPressure <= VacuumPressure) {

            return 1.0f;

        }

        float ratio = exitPressure * Mathf.Max(throttle, 0.1f) / ambientPressure;

        return Mathf.Clamp((Mathf.Log(Mathf.Max(ratio, 0.001f)) / Mathf.Log(10.0f) + 1.0f) * 0.25f, 0.0f, 1.0f);

    }

    public static Plume Create(string name, PlumeTemplate template, float exitRadius, bool jet = false) {

        Prepare();

        Plume plume = new Plume { Name = name, _template = template, _exitRadius = exitRadius, _seed = (float)(_seeds++ * 7.31 % 97.0) };

        foreach (PlumeLayer definition in template.Layers) {

            plume.AddLayer(definition);

        }

        if (!jet) {

            plume.AddDistortion();
            plume.AddLight();

        }

        plume.Visible = false;

        return plume;

    }

    private static void Prepare() {

        if (_volumetricShader != null) {

            return;

        }

        // Red is Waterfall's scrolling noise texture; the pair drives the shimmer offset.
        FastNoiseLite red = new FastNoiseLite { Seed = 4813, Frequency = 0.02f, FractalOctaves = 4, FractalGain = 0.55f };
        FastNoiseLite green = new FastNoiseLite { Seed = 9127, Frequency = 0.02f, FractalOctaves = 4, FractalGain = 0.55f };
        using Image a = red.GetSeamlessImage(256, 256);
        using Image b = green.GetSeamlessImage(256, 256);
        using Image joined = Image.CreateEmpty(256, 256, true, Image.Format.Rg8);

        for (int y = 0; y < 256; y++) {

            for (int x = 0; x < 256; x++) {

                joined.SetPixel(x, y, new Color(a.GetPixel(x, y).R, b.GetPixel(x, y).R, 0.0f));

            }

        }

        joined.GenerateMipmaps();
        _noise = ImageTexture.CreateFromImage(joined);

        _random = new FastNoiseLite { Seed = 271, Frequency = 1.0f, FractalOctaves = 2 };
        _volumetricShader = GD.Load<Shader>("res://Shaders/Exhaust/Volumetric.gdshader");
        _conesShader = GD.Load<Shader>("res://Shaders/Exhaust/Cones.gdshader");
        _distortionShader = GD.Load<Shader>("res://Shaders/Exhaust/Distortion.gdshader");

    }

    private void AddLayer(PlumeLayer definition) {

        Shader shader = definition.Model == PlumeLayer.Kind.Cones ? _conesShader : _volumetricShader;
        ShaderMaterial material = new ShaderMaterial { Shader = shader, RenderPriority = 4 + _layers.Count };
        material.SetParameter("noise_texture", _noise);
        material.SetParameter("seed", _seed + _layers.Count * 0.37f);
        definition.Write(material);

        MeshInstance3D mesh = new MeshInstance3D {

            Name = definition.Name,
            Mesh = Proxy,
            MaterialOverride = material,
            Layers = 2,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            IgnoreOcclusionCulling = true,
            Position = new Vector3(0.0f, -Mathf.Max(definition.Offset, 0.0f) * _exitRadius, 0.0f),

        };

        AddChild(mesh);

        HashSet<PlumeModifier.Target> driven = new() { PlumeModifier.Target.Length, PlumeModifier.Target.Radius };

        foreach (PlumeModifier modifier in definition.Modifiers) {

            driven.Add(modifier.Parameter);

        }

        // The surface and its closing cap derive from expansion, so both are always evaluated.
        driven.Add(PlumeModifier.Target.ExpandLinear);
        driven.Add(PlumeModifier.Target.ExpandSquare);
        driven.Add(PlumeModifier.Target.ConeExpansion);

        Layer layer = new Layer { Definition = definition, Mesh = mesh, Material = material };

        foreach (PlumeModifier.Target target in Enum.GetValues<PlumeModifier.Target>()) {

            layer.Values[(int)target] = definition.Base(target);

        }

        driven.CopyTo(layer.Driven = new PlumeModifier.Target[driven.Count]);
        _layers.Add(layer);

    }

    private void AddDistortion() {

        if (_template.Distortion <= 0.0f) {

            return;

        }

        _distortionMaterial = new ShaderMaterial { Shader = _distortionShader, RenderPriority = 3 };
        _distortionMaterial.SetParameter("noise_texture", _noise);
        _distortionMaterial.SetParameter("seed", _seed);
        _distortionMaterial.SetParameter("radius_metres", _exitRadius * 1.1f);

        _distortion = new MeshInstance3D {

            Name = "Shimmer",
            Mesh = Proxy,
            MaterialOverride = _distortionMaterial,
            Layers = 2,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            IgnoreOcclusionCulling = true,

        };

        AddChild(_distortion);

    }

    private void AddLight() {

        _light = new OmniLight3D {

            Position = new Vector3(0.0f, -_exitRadius, 0.0f),
            OmniRange = _exitRadius * _template.LightRange,
            OmniAttenuation = 1.8f,
            ShadowEnabled = false,

        };

        AddChild(_light);

    }

    public void Drive(in PlumeInputs inputs) {

        if (inputs.Lit && !_wasLit) {

            _ignitionAt = inputs.EffectTime;

        }

        if (!inputs.Lit && _wasLit) {

            _cutoffAt = inputs.EffectTime;

        }

        _wasLit = inputs.Lit;

        float ignition = Mathf.Clamp((inputs.EffectTime - _ignitionAt) / Mathf.Max(_template.IgnitionSeconds, 0.01f), 0.0f, 1.0f);
        float cutoff = Mathf.Clamp((inputs.EffectTime - _cutoffAt) / Mathf.Max(_template.CutoffSeconds, 0.01f), 0.0f, 1.0f);

        Burning = inputs.Throttle > 0.0001f || inputs.Purge > 0.0001f || inputs.Burnoff > 0.0001f || (cutoff < 1.0f && _cutoffAt > float.NegativeInfinity);

        Glow(inputs);

        Visible = Burning && Enabled;

        if (!Visible) {

            return;

        }

        float flicker = 0.5f + 0.5f * _random.GetNoise1D(inputs.EffectTime * _template.FlickerHertz + _seed * 13.7f);
        float wobble = 0.5f + 0.5f * _random.GetNoise1D(inputs.EffectTime * _template.WobbleHertz + _seed * 5.3f + 1000.0f);
        _contact.Sync(this, _exitRadius, Source);

        Span<float> controllers = stackalloc float[InputCount];
        controllers[(int)PlumeModifier.Input.Throttle] = inputs.Throttle;
        controllers[(int)PlumeModifier.Input.Air] = inputs.Air;
        controllers[(int)PlumeModifier.Input.Mach] = inputs.Mach;
        controllers[(int)PlumeModifier.Input.Flicker] = flicker;
        controllers[(int)PlumeModifier.Input.Wobble] = wobble;
        controllers[(int)PlumeModifier.Input.Ignition] = ignition;
        controllers[(int)PlumeModifier.Input.Cutoff] = cutoff;
        controllers[(int)PlumeModifier.Input.Purge] = inputs.Purge;
        controllers[(int)PlumeModifier.Input.Burnoff] = inputs.Burnoff;
        // The expanding plume is eased so layers crossing the thin upper air blend rather than snap; a
        // fresh ignition starts from the true state.
        float expansion = Expansion(inputs.ExitPressure, inputs.AmbientPressure, inputs.Throttle);
        bool fresh = _expansion < 0.0f || inputs.EffectTime - _ignitionAt < inputs.Delta * 1.5f;
        _expansion = fresh ? expansion : Mathf.Lerp(_expansion, expansion, 1.0f - Mathf.Exp(-inputs.Delta / ExpansionSeconds));
        controllers[(int)PlumeModifier.Input.Expansion] = _expansion;
        controllers[(int)PlumeModifier.Input.Sunlit] = inputs.Sunlit;

        foreach (Layer layer in _layers) {

            DriveLayer(layer, controllers, inputs);

        }

        if (_distortion != null) {

            float atmosphere = Mathf.Sqrt(Mathf.Clamp(inputs.Air, 0.0f, 1.0f))
                * Mathf.SmoothStep(VacuumPressure, VacuumPressure * 10.0f, inputs.AmbientPressure);
            float shimmer = _template.Distortion * atmosphere * (inputs.Throttle + inputs.Burnoff * 0.2f);
            _distortion.Visible = shimmer > MinimumIntensity;

            if (_distortion.Visible) {

                _distortionMaterial.SetParameter("cloud_buffer_ready", false);
                Planet.Active?.CloudPass.BindRefraction(_distortionMaterial);
                float reach = _exitRadius * 24.0f * Mathf.Max(inputs.Stretch, 0.25f);
                float width = _exitRadius * 1.1f + reach * 0.5f;
                Vector3 bend = inputs.Bend * reach;
                float extent = width + bend.Length() + _contact.Padding;
                Vector3 boundsMin = new Vector3(-extent, -reach, -extent);
                Vector3 boundsMax = new Vector3(extent, 0.0f, extent);
                _distortionMaterial.SetParameter("strength", shimmer * inputs.LightShare);
                _distortionMaterial.SetParameter("length_metres", reach);
                _distortionMaterial.SetParameter("effect_time", inputs.EffectTime);
                _distortionMaterial.SetParameter("crossflow", bend);
                _distortionMaterial.SetParameter("bounds_min", boundsMin);
                _distortionMaterial.SetParameter("bounds_max", boundsMax);
                _distortion.CustomAabb = new Aabb(boundsMin, boundsMax - boundsMin);
                _contact.Write(_distortionMaterial, Vector3.Zero, reach);

            }

        }

        if (_light != null) {

            float energy = (inputs.Throttle + inputs.Burnoff * 0.25f) * _template.LightEnergy * (0.85f + 0.3f * flicker) * inputs.LightShare;
            _light.Visible = energy > 0.01f;
            _light.LightEnergy = energy;
            _light.LightColor = _template.LightColour.Lerp(_template.LightAirColour, inputs.Air);

        }

    }

    private void DriveLayer(Layer layer, ReadOnlySpan<float> controllers, in PlumeInputs inputs) {

        PlumeLayer definition = layer.Definition;
        float[] values = layer.Values;

        foreach (PlumeModifier.Target target in layer.Driven) {

            values[(int)target] = definition.Base(target);

        }

        foreach (PlumeModifier modifier in definition.Modifiers) {

            float input = controllers[(int)modifier.Controller];

            if (modifier.IsColour) {

                if (modifier.Gradient != null) {

                    layer.Material.SetParameter(PlumeModifier.Uniform(modifier.Parameter), modifier.Gradient.Sample(Mathf.Clamp(input, 0.0f, 1.0f)));

                }

                continue;

            }

            values[(int)modifier.Parameter] = modifier.Apply(values[(int)modifier.Parameter], input);

        }

        float brightness = values[(int)PlumeModifier.Target.Brightness];
        float length = values[(int)PlumeModifier.Target.Length] * _exitRadius * inputs.Stretch;
        float radius = values[(int)PlumeModifier.Target.Radius] * _exitRadius;
        float offset = Mathf.Max(definition.Offset, 0.0f) * _exitRadius;

        // Volumetric layers march through the contact field and flow around a receiving hull; the
        // shock train simply ends where the hull stands in the jet.
        bool marched = definition.Model == PlumeLayer.Kind.Volumetric && _contact.Padding > 0.0f;
        float floor = float.IsFinite(_contact.Block) && !marched ? -(_contact.Block - offset) / Mathf.Max(length, 0.001f) : OpenFloor;
        layer.Mesh.Visible = brightness > MinimumIntensity && length > 0.001f && radius > 0.001f && floor < 0.0f;

        if (!layer.Mesh.Visible) {

            return;

        }

        foreach (PlumeModifier.Target target in layer.Driven) {

            switch (target) {

                case PlumeModifier.Target.Length:
                case PlumeModifier.Target.Radius:
                case PlumeModifier.Target.Brightness:
                case PlumeModifier.Target.StartTint:
                case PlumeModifier.Target.EndTint:
                    break;

                case PlumeModifier.Target.TileX:
                case PlumeModifier.Target.TileY:
                    layer.Material.SetParameter("tile", new Vector2(values[(int)PlumeModifier.Target.TileX], values[(int)PlumeModifier.Target.TileY]));
                    break;

                case PlumeModifier.Target.SpeedY:
                    layer.Material.SetParameter("speed", new Vector2(definition.Speed.X, values[(int)PlumeModifier.Target.SpeedY]));
                    break;

                default:
                    layer.Material.SetParameter(PlumeModifier.Uniform(target), values[(int)target]);
                    break;

            }

        }

        float bottom;
        float widest;

        if (definition.Model == PlumeLayer.Kind.Cones) {

            bottom = -1.0f;
            widest = 1.0f + Mathf.Abs(values[(int)PlumeModifier.Target.ConeExpansion]);

        }
        else {

            (Vector2 ends, Vector3 cap, float span) = Quadric(values[(int)PlumeModifier.Target.ExpandLinear], values[(int)PlumeModifier.Target.ExpandSquare]);
            layer.Material.SetParameter("plume_ends", ends);
            layer.Material.SetParameter("cap_quadric", cap);
            bottom = ends.Y;
            widest = span;

        }

        Vector2 shear = new Vector2(inputs.Bend.X, inputs.Bend.Z) * length;
        float reach = widest * radius + shear.Length() + (marched ? _contact.Padding : 0.0f);
        Vector3 low = new Vector3(-reach, Mathf.Max(bottom, floor) * length, -reach);
        Vector3 high = new Vector3(reach, marched ? _contact.Padding : 0.0f, reach);

        if (definition.Model == PlumeLayer.Kind.Volumetric) {

            layer.Material.SetParameter("exit_radius", _exitRadius);
            _contact.Write(layer.Material, layer.Mesh.Position, -low.Y);

        }

        if (definition.SunPhase > 0.0f) {

            layer.Material.SetParameter("sun_local", (layer.Mesh.GlobalBasis.Inverse() * inputs.Sun).Normalized());

        }

        layer.Material.SetParameter("brightness", brightness);
        layer.Material.SetParameter("plume_scale", new Vector3(radius, length, radius));
        layer.Material.SetParameter("plume_shear", shear);
        layer.Material.SetParameter("plume_floor", floor);
        layer.Material.SetParameter("effect_time", inputs.EffectTime);
        layer.Material.SetParameter("bounds_min", low);
        layer.Material.SetParameter("bounds_max", high);
        layer.Mesh.CustomAabb = new Aabb(low, high - low);

    }

    // Waterfall's surface is r² = 1 + b·y + a·y² over y in [-1, 0], closed below by a tangent cap
    // ending at -1.5, or by a straight taper when the flow converges too steeply for an ellipse.
    private static (Vector2 Ends, Vector3 Cap, float Widest) Quadric(float linear, float square) {

        float a = linear * linear - 10.0f * square;
        float b = -2.0f * linear - 10.0f * square;
        float end = 1.0f - b + a;
        float widest = Mathf.Max(1.0f, end);

        if (Mathf.Abs(a) > 0.000001f) {

            float vertex = -b / (2.0f * a);

            if (vertex > -1.0f && vertex < 0.0f) {

                widest = Mathf.Max(widest, 1.0f - b * b / (4.0f * a));

            }

        }

        if (end <= 0.0001f) {

            float apex = Apex(a, b);

            return (new Vector2(apex, apex), new Vector3(0.0f, 0.0f, 1.0f), Mathf.Sqrt(widest));

        }

        float slope = b - 2.0f * a;
        const float gap = -1.0f - CapEnd;

        if (slope * gap - end > 0.0f) {

            return (new Vector2(-1.0f, -1.0f - end / slope), new Vector3(0.0f, slope, end + slope), Mathf.Sqrt(widest));

        }

        float capA = (slope * gap - end) / (gap * gap);
        float capB = slope + 2.0f * capA;
        float capC = -CapEnd * (capA * CapEnd + capB);

        if (capA < 0.0f) {

            float vertex = -capB / (2.0f * capA);

            if (vertex > CapEnd && vertex < -1.0f) {

                widest = Mathf.Max(widest, capC - capB * capB / (4.0f * capA));

            }

        }

        return (new Vector2(-1.0f, CapEnd), new Vector3(capA, capB, capC), Mathf.Sqrt(widest));

    }

    // The highest point below the exit where a converging surface closes to the axis.
    private static float Apex(float a, float b) {

        if (Mathf.Abs(a) < 0.000001f) {

            return Mathf.Clamp(-1.0f / b, -1.0f, 0.0f);

        }

        float root = Mathf.Sqrt(Mathf.Max(b * b - 4.0f * a, 0.0f));
        float first = (-b + root) / (2.0f * a);
        float second = (-b - root) / (2.0f * a);
        float apex = -1.0f;

        foreach (float candidate in stackalloc[] { first, second }) {

            if (candidate < 0.0f && candidate >= -1.0f) {

                apex = Mathf.Max(apex, candidate);

            }

        }

        return apex;

    }

    private void Glow(in PlumeInputs inputs) {

        if (_bell.Count == 0) {

            return;

        }

        float target = inputs.Throttle;
        float rate = target > _heat ? _template.HeatRiseSeconds : _template.HeatFallSeconds;
        _heat += (target - _heat) * (1.0f - Mathf.Exp(-inputs.Delta / Mathf.Max(rate, 0.01f)));

        // Leave only a trace of incandescence at sustained full power on the cooled bell.
        float energy = _template.GlowEnergy * 0.002f * Mathf.Pow(Mathf.SmoothStep(0.85f, 1.0f, _heat), 3.0f);
        bool hot = Enabled && energy > 0.001f;

        foreach (StandardMaterial3D skin in _bell) {

            skin.EmissionEnabled = hot;

            if (hot) {

                skin.Emission = _template.GlowColour;
                skin.EmissionEnergyMultiplier = energy;

            }

        }

    }

    public bool Tune(string parameter, string value) {

        bool ok = false;

        foreach (Layer layer in _layers) {

            ok |= Tune(layer.Material, parameter, value);

        }

        return ok;

    }

    internal static bool Tune(ShaderMaterial material, string parameter, string value) {

        if (material.GetParameter(parameter).VariantType == Variant.Type.Nil) {

            return false;

        }

        string[] parts = value.Split(',');

        if (parts.Length >= 3) {

            material.SetParameter(parameter, new Color(parts[0].ToFloat(), parts[1].ToFloat(), parts[2].ToFloat()));

        }
        else {

            material.SetParameter(parameter, value.ToFloat());

        }

        return true;

    }

}
