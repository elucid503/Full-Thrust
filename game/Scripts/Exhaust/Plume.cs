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
    public float Cluster;
    public float LightShare;

    public float AmbientPressure;
    public float ExitPressure;

    public Vector3 Bend;
    public float Stretch;

    public float EffectTime;
    public float Delta;

}

/// <summary>A nozzle's exhaust: a stack of additive layers, a heat shimmer, a light and the glow
/// of the bell it leaves. Y is the nozzle axis and the flow runs down -Y from the origin.</summary>
public sealed partial class Plume : Node3D {

    private const float MinimumIntensity = 0.004f;
    private const float VacuumPressure = 30.0f;

    /// <summary>Hides every exhaust effect, so its whole cost can be measured against a bare scene.</summary>
    public static bool Enabled { get; set; } = true;

    private sealed class Layer {

        public PlumeLayer Definition;
        public MeshInstance3D Mesh;
        public ShaderMaterial Material;
        public PlumeModifier.Target[] Driven;
        public float[] Values = new float[Enum.GetValues<PlumeModifier.Target>().Length];

    }

    private static CylinderMesh _cylinder;
    private static ImageTexture _noise;
    private static FastNoiseLite _flicker;
    private static Shader _layerShader;
    private static Shader _distortionShader;
    private static int _seeds;

    private readonly List<Layer> _layers = new();
    private readonly List<StandardMaterial3D> _bell = new();

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

    public bool Burning { get; private set; }
    public float ExitRadius => _exitRadius;

    /// <summary>The bell surfaces that glow with chamber heat; the owner hands them over.</summary>
    public List<StandardMaterial3D> Bell => _bell;

    public static Plume Create(string name, PlumeTemplate template, float exitRadius, bool cluster = false, bool jet = false) {

        Prepare();

        Plume plume = new Plume { Name = name, _template = template, _exitRadius = exitRadius, _seed = (float)(_seeds++ * 7.31 % 97.0) };

        foreach (PlumeLayer definition in cluster ? template.ClusterLayers : template.Layers) {

            plume.AddLayer(definition);

        }

        if (!jet && !cluster) {

            plume.AddDistortion();
            plume.AddLight();

        }

        plume.Visible = false;

        return plume;

    }

    private static void Prepare() {

        if (_cylinder != null) {

            return;

        }

        _cylinder = new CylinderMesh {

            TopRadius = 1.0f, BottomRadius = 1.0f, Height = 1.0f,
            RadialSegments = 32, Rings = 48, CapTop = false, CapBottom = false,

        };

        // Two independent channels: one drives brightness, the pair drives the shimmer offset.
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

        _flicker = new FastNoiseLite { Seed = 271, Frequency = 1.0f, FractalOctaves = 2 };
        _layerShader = GD.Load<Shader>("res://Shaders/Exhaust/Plume.gdshader");
        _distortionShader = GD.Load<Shader>("res://Shaders/Exhaust/Distortion.gdshader");

    }

    private void AddLayer(PlumeLayer definition) {

        ShaderMaterial material = new ShaderMaterial { Shader = _layerShader, RenderPriority = 3 + _layers.Count };
        material.SetShaderParameter("noise_texture", _noise);
        material.SetShaderParameter("seed", _seed + _layers.Count * 0.37f);
        definition.Write(material);

        MeshInstance3D mesh = new MeshInstance3D {

            Name = definition.Name,
            Mesh = _cylinder,
            MaterialOverride = material,
            Layers = 2,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            IgnoreOcclusionCulling = true,
            Position = new Vector3(0.0f, -definition.Offset * _exitRadius, 0.0f),

        };

        AddChild(mesh);

        HashSet<PlumeModifier.Target> driven = new() { PlumeModifier.Target.Length, PlumeModifier.Target.Radius };

        foreach (PlumeModifier modifier in definition.Modifiers) {

            driven.Add(modifier.Parameter);

        }

        // Cells scale with the nozzle even when nothing else drives them.
        if (definition.CellStrength > 0.0f) {

            driven.Add(PlumeModifier.Target.CellStrength);

        }

        Layer layer = new Layer { Definition = definition, Mesh = mesh, Material = material };
        driven.CopyTo(layer.Driven = new PlumeModifier.Target[driven.Count]);
        _layers.Add(layer);

    }

    private void AddDistortion() {

        if (_template.Distortion <= 0.0f) {

            return;

        }

        _distortionMaterial = new ShaderMaterial { Shader = _distortionShader, RenderPriority = -10 };
        _distortionMaterial.SetShaderParameter("noise_texture", _noise);
        _distortionMaterial.SetShaderParameter("seed", _seed);
        _distortionMaterial.SetShaderParameter("radius_metres", _exitRadius * 1.1f);

        _distortion = new MeshInstance3D {

            Name = "Shimmer",
            Mesh = _cylinder,
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

        float flicker = 0.5f + 0.5f * _flicker.GetNoise1D((inputs.EffectTime * _template.FlickerHertz + _seed) * 100.0f);

        Span<float> controllers = stackalloc float[Enum.GetValues<PlumeModifier.Input>().Length];
        controllers[(int)PlumeModifier.Input.Throttle] = inputs.Throttle;
        controllers[(int)PlumeModifier.Input.Air] = inputs.Air;
        controllers[(int)PlumeModifier.Input.Mach] = inputs.Mach;
        controllers[(int)PlumeModifier.Input.Flicker] = flicker;
        controllers[(int)PlumeModifier.Input.Ignition] = ignition;
        controllers[(int)PlumeModifier.Input.Cutoff] = cutoff;
        controllers[(int)PlumeModifier.Input.Purge] = inputs.Purge;
        controllers[(int)PlumeModifier.Input.Burnoff] = inputs.Burnoff;
        controllers[(int)PlumeModifier.Input.Cluster] = inputs.Cluster;

        (float cellLength, float cellContrast) = Cells(inputs);

        foreach (Layer layer in _layers) {

            DriveLayer(layer, controllers, inputs, cellLength, cellContrast);

        }

        if (_distortion != null) {

            float shimmer = _template.Distortion * inputs.Air * inputs.Throttle;
            _distortion.Visible = shimmer > 0.02f;

            if (_distortion.Visible) {

                float reach = _exitRadius * 6.0f;
                _distortionMaterial.SetShaderParameter("strength", shimmer);
                _distortionMaterial.SetShaderParameter("length_metres", reach);
                _distortionMaterial.SetShaderParameter("effect_time", inputs.EffectTime);
                _distortion.CustomAabb = new Aabb(new Vector3(-_exitRadius * 3.0f, -reach, -_exitRadius * 3.0f), new Vector3(_exitRadius * 6.0f, reach, _exitRadius * 6.0f));

            }

        }

        if (_light != null) {

            float energy = (inputs.Throttle + inputs.Burnoff * 0.25f) * _template.LightEnergy * (0.85f + 0.3f * flicker) * inputs.LightShare;
            _light.Visible = energy > 0.01f;
            _light.LightEnergy = energy;
            _light.LightColor = _template.LightColour.Lerp(_template.LightAirColour, inputs.Air);

        }

    }

    private void DriveLayer(Layer layer, ReadOnlySpan<float> controllers, in PlumeInputs inputs, float cellLength, float cellContrast) {

        PlumeLayer definition = layer.Definition;

        foreach (PlumeModifier.Target target in layer.Driven) {

            layer.Values[(int)target] = definition.Base(target);

        }

        foreach (PlumeModifier modifier in definition.Modifiers) {

            float input = controllers[(int)modifier.Controller];

            if (modifier.IsColour) {

                if (modifier.Gradient != null) {

                    layer.Material.SetShaderParameter(modifier.Parameter == PlumeModifier.Target.StartTint ? "start_tint" : "end_tint", modifier.Gradient.Sample(Mathf.Clamp(input, 0.0f, 1.0f)));

                }

                continue;

            }

            layer.Values[(int)modifier.Parameter] = modifier.Apply(layer.Values[(int)modifier.Parameter], input);

        }

        float brightness = layer.Values[(int)PlumeModifier.Target.Brightness];
        layer.Mesh.Visible = brightness > MinimumIntensity;

        if (!layer.Mesh.Visible) {

            return;

        }

        float length = layer.Values[(int)PlumeModifier.Target.Length] * _exitRadius * inputs.Stretch;
        float radius = layer.Values[(int)PlumeModifier.Target.Radius] * _exitRadius;
        Vector3 crossflow = inputs.Bend * length;

        foreach (PlumeModifier.Target target in layer.Driven) {

            float value = layer.Values[(int)target];

            switch (target) {

                case PlumeModifier.Target.Length:
                    layer.Material.SetShaderParameter("length_metres", length);
                    break;

                case PlumeModifier.Target.Radius:
                    layer.Material.SetShaderParameter("radius_metres", radius);
                    break;

                case PlumeModifier.Target.CellStrength:
                    layer.Material.SetShaderParameter("cell_strength", value * cellContrast);
                    layer.Material.SetShaderParameter("cell_length", cellLength);
                    break;

                case PlumeModifier.Target.TileX:
                case PlumeModifier.Target.TileY:
                    layer.Material.SetShaderParameter("tile", new Vector2(layer.Values[(int)PlumeModifier.Target.TileX], layer.Values[(int)PlumeModifier.Target.TileY]));
                    break;

                case PlumeModifier.Target.ScrollY:
                    layer.Material.SetShaderParameter("scroll", new Vector2(definition.Scroll.X, value));
                    break;

                case PlumeModifier.Target.StartTint:
                case PlumeModifier.Target.EndTint:
                    break;

                default:
                    layer.Material.SetShaderParameter(Uniform(target), value);
                    break;

            }

        }

        layer.Material.SetShaderParameter("crossflow", crossflow);
        layer.Material.SetShaderParameter("effect_time", inputs.EffectTime);

        float flare = 1.0f + layer.Values[(int)PlumeModifier.Target.ExpandOffset] + layer.Values[(int)PlumeModifier.Target.ExpandLinear]
            + layer.Values[(int)PlumeModifier.Target.ExpandSquare] + layer.Values[(int)PlumeModifier.Target.ExpandBounded];
        float reach = radius * Mathf.Max(flare, 1.0f) + crossflow.Length();
        layer.Mesh.CustomAabb = new Aabb(new Vector3(-reach, -length, -reach), new Vector3(reach * 2.0f, length, reach * 2.0f));

    }

    private static string Uniform(PlumeModifier.Target target) {

        return target switch {

            PlumeModifier.Target.Brightness => "brightness",
            PlumeModifier.Target.ExpandOffset => "expand_offset",
            PlumeModifier.Target.ExpandLinear => "expand_linear",
            PlumeModifier.Target.ExpandSquare => "expand_square",
            PlumeModifier.Target.ExpandBounded => "expand_bounded",
            PlumeModifier.Target.Falloff => "falloff",
            PlumeModifier.Target.FalloffStart => "falloff_start",
            PlumeModifier.Target.Fresnel => "fresnel",
            PlumeModifier.Target.FresnelInvert => "fresnel_invert",
            PlumeModifier.Target.Noise => "noise_strength",
            PlumeModifier.Target.FadeIn => "fade_in",
            PlumeModifier.Target.FadeOut => "fade_out",
            PlumeModifier.Target.TintFalloff => "tint_falloff",

            _ => throw new ArgumentOutOfRangeException(nameof(target)),

        };

    }

    // Cell spacing grows with under-expansion; contrast follows the mismatch and dies in vacuum.
    private (float Length, float Contrast) Cells(in PlumeInputs inputs) {

        if (inputs.AmbientPressure < VacuumPressure || inputs.ExitPressure <= 0.0f) {

            return (0.0f, 0.0f);

        }

        float ratio = inputs.ExitPressure / inputs.AmbientPressure;
        float mismatch = Mathf.Abs(Mathf.Log(ratio));
        float contrast = Mathf.Clamp(mismatch / 1.5f, 0.0f, 1.0f) * Mathf.SmoothStep(VacuumPressure, VacuumPressure * 10.0f, inputs.AmbientPressure);
        float spacing = 2.0f * _exitRadius * Mathf.Min(0.9f + 1.1f * Mathf.Sqrt(Mathf.Max(ratio, 1.0f)), 8.0f);

        return (spacing, contrast * _template.CellContrast);

    }

    private void Glow(in PlumeInputs inputs) {

        if (_bell.Count == 0) {

            return;

        }

        float target = inputs.Throttle;
        float rate = target > _heat ? _template.HeatRiseSeconds : _template.HeatFallSeconds;
        _heat += (target - _heat) * (1.0f - Mathf.Exp(-inputs.Delta / Mathf.Max(rate, 0.01f)));

        bool hot = _heat > 0.01f;

        foreach (StandardMaterial3D skin in _bell) {

            skin.EmissionEnabled = hot;

            if (hot) {

                skin.Emission = _template.GlowColour;
                skin.EmissionEnergyMultiplier = _template.GlowEnergy * _heat * _heat;

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

    private static bool Tune(ShaderMaterial material, string parameter, string value) {

        if (material.GetShaderParameter(parameter).VariantType == Variant.Type.Nil) {

            return false;

        }

        string[] parts = value.Split(',');

        if (parts.Length >= 3) {

            material.SetShaderParameter(parameter, new Color(parts[0].ToFloat(), parts[1].ToFloat(), parts[2].ToFloat()));

        }
        else {

            material.SetShaderParameter(parameter, value.ToFloat());

        }

        return true;

    }

}
