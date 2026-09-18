using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class VesselView {

    private static readonly EngineLimits RcsLimits = new();
    private const float NozzleMouth = 0.081f;
    private const float SheathFlux = 150_000.0f;

    private sealed class Engine {

        public int Index { get; init; }
        public string FuelName { get; set; }
        public float Power { get; set; }
        public Node3D Pivot { get; init; }

        public MeshInstance3D Plume { get; init; }
        public ShaderMaterial PlumeMaterial { get; init; }
        public OmniLight3D Light { get; init; }

    }

    private sealed class Jet {

        public MeshInstance3D Volume { get; init; }
        public ShaderMaterial Material { get; init; }
        public Vector3 Exit { get; init; }
        public Vector3 Axis { get; init; }
        public float Radius { get; init; }
        public float Duty { get; set; }
        public EngineState Valve { get; } = new();

    }

    private static CylinderMesh _flameMesh;
    private static BoxMesh _proxy;
    private static NoiseTexture3D _noise;
    private static int _seeds;

    private EntryField _entryField;
    private MeshInstance3D _wake;
    private ShaderMaterial _wakeMaterial;
    private OmniLight3D _entryLight;
    private double _lastExhaustTime;
    private float _effectTime;
    private float _effectDelta;
    private float _projectionAge;

    private static ShaderMaterial VolumeMaterial(string shader, int priority) {

        _noise ??= new NoiseTexture3D {

            Width = 48,
            Height = 48,
            Depth = 48,
            Seamless = true,
            SeamlessBlendSkirt = 0.25f,
            Noise = new FastNoiseLite { Seed = 4813, Frequency = 0.09f, FractalOctaves = 3 },

        };

        ShaderMaterial material = new ShaderMaterial { Shader = GD.Load<Shader>(shader), RenderPriority = priority };

        material.SetShaderParameter("seed", (float)(_seeds++ * 7.31));
        material.SetShaderParameter("flow_noise", _noise);

        return material;

    }

    private static MeshInstance3D Volume(string name, ShaderMaterial material) {

        _proxy ??= new BoxMesh { Size = Vector3.One };

        return new MeshInstance3D {

            Name = name,
            Mesh = _proxy,
            MaterialOverride = material,
            Layers = 2,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,

        };

    }

    private static void Bounds(MeshInstance3D volume, ShaderMaterial material, Vector3 low, Vector3 high) {

        material.SetShaderParameter("bounds_min", low);
        material.SetShaderParameter("bounds_max", high);
        volume.CustomAabb = new Aabb(low, high - low);

    }

    private static void Colour(ShaderMaterial material, Chemistry chemistry) {

        material.SetShaderParameter("core_colour", chemistry.Core);
        material.SetShaderParameter("tail_colour", chemistry.Tail);
        material.SetShaderParameter("luminosity", chemistry.Luminosity);

    }

    private static void AttachPlume(Node3D pivot, Piece piece, float bellRadius, float reach) {

        ShaderMaterial material = (ShaderMaterial)GD.Load<ShaderMaterial>("res://Effects/Plume.tres").Duplicate();
        Colour(material, Chemistry.For(piece.Stage.Fuel));
        MeshInstance3D plume = Flame($"Plume{piece.Engines.Count + 1}", material);
        plume.Position = new Vector3(0.0f, -reach, 0.0f);
        pivot.AddChild(plume);

        OmniLight3D light = new OmniLight3D {

            Position = new Vector3(0.0f, -reach - bellRadius, 0.0f),
            OmniRange = bellRadius * 16.0f,
            OmniAttenuation = 1.8f,
            ShadowEnabled = false,
            Visible = false,

        };

        pivot.AddChild(light);

        piece.Engines.Add(new Engine {

            Index = piece.Engines.Count,
            Pivot = pivot,
            Plume = plume,
            PlumeMaterial = material,
            Light = light,

        });

    }

    private static IEnumerable<(Vector3 Position, Vector3 Axis, Vector3 Side, float Scale)> Mounts(Stage stage, Part part) {

        float gauge = (float)part.Extent;
        float height = (float)part.Centre;

        float scale = gauge / NozzleGauge;

        // A recessed pair shares one circular port, so the bells sit closer than they do on a pod.
        float spread = part.Depth > 0.0 ? RcsOffset : (float)part.Length * 0.28f;

        for (int index = 0; index < part.Count; index++) {

            float angle = Mathf.Tau * PortCentre(part.Count, index) / RadialSegments;

            Vector3 side = Vector3.Up.Cross(Radial(angle)).Normalized();

            // One nozzle canted forward and one aft: a port that only fired radially could not pitch.
            for (int sense = -1; sense <= 1; sense += 2) {

                float station = height + spread * sense;
                float seat = (float)(stage.Hull.RadiusAt(station) - part.Depth);
                Vector3 outward = Surface(stage, angle, station);
                Vector3 along = outward.Cross(side).Normalized();
                Vector3 axis = (outward * Mathf.Cos(RcsCant) + along * (Mathf.Sin(RcsCant) * sense)).Normalized();
                Vector3 position = Radial(angle) * seat + axis * (NozzleBase * scale) + Vector3.Up * station;

                yield return (position, axis, side, scale);

            }

        }

    }

    private static TriangleMesh JetSurface(Node3D node) {

        List<Vector3> faces = new List<Vector3>();

        foreach (MeshInstance3D mesh in Meshes(node)) {

            Transform3D transform = mesh.Transform;

            for (Node parent = mesh.GetParent(); parent != node; parent = parent.GetParent()) {

                if (parent is Node3D spatial) {

                    transform = spatial.Transform * transform;

                }

            }

            foreach (Vector3 point in mesh.Mesh.GetFaces()) {

                faces.Add(transform * point);

            }

        }

        TriangleMesh surface = new TriangleMesh();
        surface.CreateFromFaces(faces.ToArray());
        return surface;

    }

    private static void AttachJet(Node3D node, Piece piece, Vector3 position, Vector3 axis, Vector3 side, float scale, TriangleMesh surface = null) {

        ShaderMaterial material = (ShaderMaterial)GD.Load<ShaderMaterial>("res://Effects/Plume.tres").Duplicate();
        Colour(material, Chemistry.Hydrazine);
        MeshInstance3D volume = Flame("Jet", material);

        Vector3 exit = position + axis * (NozzleReach * scale);

        if (surface != null) {

            // Imported hardware supplies its own exit; the generated bell's length does not apply.
            float reach = Mathf.Max(0.4f, NozzleGauge * scale * 4.0f);
            Godot.Collections.Dictionary hit = surface.IntersectSegment(position + axis * reach, position - axis * reach);
            exit = hit.Count > 0 ? hit["position"].AsVector3() : position - axis * (NozzleBase * scale);
            exit -= axis * (NozzleMouth * scale * 0.05f);

        }

        volume.Transform = new Transform3D(new Basis(side, -axis, side.Cross(-axis).Normalized()), exit);
        node.AddChild(volume);

        piece.Jets.Add(new Jet {

            Volume = volume,
            Material = material,
            Exit = exit,
            Axis = axis,
            Radius = NozzleMouth * scale,

        });

    }

    private void AttachSheath() {

        _sheathMaterial = VolumeMaterial("res://Shaders/Entry.gdshader", 4);
        _wakeMaterial = VolumeMaterial("res://Shaders/EntryWake.gdshader", 2);
        _sheath = Volume("Sheath", _sheathMaterial);
        _wake = Volume("EntryWake", _wakeMaterial);
        _body.AddChild(_sheath);
        _body.AddChild(_wake);

        _entryLight = new OmniLight3D {

            LightColor = new Color(1.0f, 0.28f, 0.08f),
            ShadowEnabled = false,
            Visible = false,

        };

        _body.AddChild(_entryLight);
        BakeProfile();

    }

    private void PrepareEntryFields() {

        for (int index = 0; index < _pieces.Count; index++) {

            Piece piece = _pieces[index];
            piece.SingleEntry = new EntryField(new Vessel(_vessel.Name, new[] { piece.Stage }));
            if (index == _pieces.Count - 1) {

                piece.StackEntry = piece.SingleEntry;
                continue;

            }

            List<Stage> remaining = new();
            for (int stage = index; stage < _pieces.Count; stage++) { remaining.Add(_pieces[stage].Stage); }
            piece.StackEntry = new EntryField(new Vessel(_vessel.Name, remaining));

        }

    }

    private void BakeProfile() {

        Piece active = Find(_vessel.Active);
        _entryField = _pieces.Count == 1 ? active.SingleEntry : active.StackEntry;

        foreach (ShaderMaterial material in new[] { _sheathMaterial, _wakeMaterial }) {

            material.SetShaderParameter("hull_field", _entryField.Distance);
            material.SetShaderParameter("field_domain", _entryField.Domain);
            material.SetShaderParameter("body_radius", _entryField.Radius);

        }

    }

    private (float Pressure, float Density) Air() {

        CelestialBody body = Flight.Active.Body;

        if (!body.HasAtmosphere) {

            return (0.0f, 0.0f);

        }

        double altitude = body.AltitudeOf(_vessel.Position);

        return ((float)body.Atmosphere.PressureAt(altitude), (float)body.Atmosphere.DensityAt(altitude));

    }

    private void SyncPlume() {

        double now = Flight.Active.Time;
        _effectDelta = Mathf.Max((float)(now - _lastExhaustTime), 0.0f);
        _lastExhaustTime = now;
        _effectTime += _effectDelta;

        (float pressure, float density) = Air();

        foreach (Piece piece in _pieces) {

            if (piece.Engines.Count == 0) {

                continue;

            }

            Chemistry chemistry = Chemistry.For(piece.Stage.Fuel);
            float air = Mathf.Clamp(density / 1.225f, 0.0f, 1.0f);

            foreach (Engine engine in piece.Engines) {

                EngineState state = piece.Stage.EngineStates[engine.Index];
                if (engine.FuelName != piece.Stage.Fuel?.Name) {

                    Colour(engine.PlumeMaterial, chemistry);
                    engine.FuelName = piece.Stage.Fuel?.Name;

                }
                engine.Power = _vessel.Intact ? (float)state.Power : 0.0f;
                engine.PlumeMaterial.SetShaderParameter("purge", (float)state.Purge);
                engine.PlumeMaterial.SetShaderParameter("burnoff", (float)state.Burnoff);

                bool burning = _vessel.Intact && (engine.Power > 0.0001f || state.Purge > 0.0001 || state.Burnoff > 0.0001);
                engine.Plume.Visible = burning;
                engine.Light.Visible = burning;
                if (!burning) { continue; }

                DriveVolume(engine.Plume, engine.PlumeMaterial, engine.Power, piece.BellRadius, pressure, false);

                engine.Light.LightColor = chemistry.Core.Lerp(chemistry.Flame, air * chemistry.Afterburn);
                engine.Light.LightEnergy = (engine.Power + (float)state.Burnoff * 0.25f) * chemistry.Luminosity * 1.15f / Mathf.Sqrt(piece.Engines.Count);

            }

        }

        SyncJets(pressure);

    }

    private static MeshInstance3D Flame(string name, ShaderMaterial material) {

        MeshInstance3D flame = Volume(name, material);
        _flameMesh ??= new CylinderMesh {

            TopRadius = 1.0f, BottomRadius = 1.0f, Height = 1.0f,
            RadialSegments = 32, Rings = 16, CapTop = false, CapBottom = false,

        };
        flame.Mesh = _flameMesh;
        return flame;

    }

    private void DriveVolume(MeshInstance3D volume, ShaderMaterial material, float throttle, float bellRadius, float pressure, bool jet, float duty = 1.0f) {

        float vacuum = 1.0f - Mathf.Clamp(pressure / 101325.0f, 0.0f, 1.0f);
        float reach = material.GetShaderParameter("length_radii").AsSingle() * (jet ? 0.25f : 1.0f);
        float length = bellRadius * reach * Mathf.Lerp(0.35f, 1.0f, Mathf.Sqrt(throttle));
        float spread = Mathf.Lerp(material.GetShaderParameter("air_spread").AsSingle(),
            material.GetShaderParameter("vacuum_spread").AsSingle(), vacuum);
        float radius = bellRadius + length * spread;
        material.SetShaderParameter("exit_radius", bellRadius);
        material.SetShaderParameter("plume_length", length);
        material.SetShaderParameter("spread", spread);
        material.SetShaderParameter("throttle", throttle * duty);
        material.SetShaderParameter("effect_time", _effectTime);
        volume.CustomAabb = new Aabb(new Vector3(-radius, -length, -radius), new Vector3(radius * 2.0f, length, radius * 2.0f));

    }

    private void SyncJets(float pressure) {

        bool armed = _vessel.HasRcs;
        double limit = _vessel.ThrusterTorqueLimit;
        Vector3 torque = armed && limit > 0.0 ? Frames.Direction(_vessel.AppliedRcsTorque) / (float)limit : Vector3.Zero;
        Vector3 push = armed ? Frames.Direction(_vessel.TranslationCommand).Clamp(-Vector3.One, Vector3.One) : Vector3.Zero;
        Vector3 centre = new Vector3(0.0f, (float)_vessel.CentreOfMassZ, 0.0f);

        foreach (Piece piece in _pieces) {

            foreach (Jet jet in piece.Jets) {

                float wanted = 0.0f;

                if (armed && piece.Stage.HasReactionControl) {

                    Vector3 thrust = -jet.Axis;
                    Vector3 moment = (jet.Exit - centre).Cross(thrust).Normalized();
                    wanted = Mathf.Clamp(Mathf.Max(torque.Dot(moment), 0.0f) + Mathf.Max(push.Dot(thrust), 0.0f), 0.0f, 1.0f);

                }

                // RCS valves pulse at chamber pressure; duty changes emission, not the nozzle's pressure regime.
                jet.Valve.Advance(wanted, RcsLimits, _effectDelta, armed && piece.Stage.HasReactionControl, true);
                jet.Duty = (float)jet.Valve.Power;
                jet.Material.SetShaderParameter("purge", (float)jet.Valve.Purge * 0.18f);
                jet.Material.SetShaderParameter("burnoff", 0.0f);
                jet.Volume.Visible = jet.Duty > 0.001f || jet.Valve.Purge > 0.005;

                if (jet.Volume.Visible) {

                    DriveVolume(jet.Volume, jet.Material, 1.0f, jet.Radius, pressure, true, jet.Duty);

                }

            }

        }

    }

    private void SyncSheath() {

        AeroForces air = _vessel.Aero;
        CelestialBody body = Flight.Active.Body;
        double altitude = body.AltitudeOf(_vessel.Position);
        float wanted = air.InAir ? Mathf.Clamp((float)(air.HeatFlux / SheathFlux), 0.0f, 1.4f) : 0.0f;

        // Lift faint upper-atmosphere emission while retaining the established peak brightness.
        wanted = wanted < 1.0f ? Mathf.Sqrt(wanted) : wanted;
        float depth = (float)(body.AtmosphereTop - altitude);
        wanted *= Mathf.SmoothStep(0.0f, (float)Math.Max(body.AtmosphereTop * 0.02, 1.0), depth);
        wanted *= Mathf.SmoothStep(2.5f, 5.0f, (float)air.Mach);
        _sheathHeat = Mathf.Lerp(_sheathHeat, wanted, 1.0f - Mathf.Exp(-_effectDelta / 0.12f));

        bool burning = _sheathHeat > 0.008f && air.InAir;
        _sheath.Visible = burning;
        _wake.Visible = burning;
        _entryLight.Visible = burning;

        if (!burning) {

            return;

        }

        Vector3d relative = _vessel.Velocity - Flight.Active.Body.AirVelocityAt(_vessel.Position);
        Vector3 flight = Frames.Direction(_vessel.Orientation.Conjugate.Rotate(relative).Normalized);
        float cosine = Mathf.Clamp(flight.Y, -1.0f, 1.0f);
        float sine = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - cosine * cosine));
        Vector3 radial = sine > 0.0001f ? new Vector3(flight.X, 0.0f, flight.Z) / sine : Vector3.Right;
        Vector3 side = radial * cosine - Vector3.Up * sine;
        Basis flow = new Basis(side, flight, side.Cross(flight).Normalized());
        Transform3D transform = new Transform3D(flow, new Vector3(0.0f, _entryField.Centre, 0.0f));

        _sheath.Transform = transform;
        _wake.Transform = transform;
        _projectionAge += _effectDelta;

        float angleChange = Mathf.Abs(Mathf.Acos(cosine) - Mathf.Acos(Mathf.Clamp(_entryField.Cosine, -1.0f, 1.0f)));

        if (_entryField.Footprint == null || angleChange > 0.06f || (angleChange > 0.008f && _projectionAge > 0.05f)) {

            _entryField.Project(cosine);
            _projectionAge = 0.0f;
            _wakeMaterial.SetShaderParameter("footprint", _entryField.Footprint);
            _wakeMaterial.SetShaderParameter("footprint_extent", _entryField.FootprintExtent);
            _sheathMaterial.SetShaderParameter("footprint", _entryField.Footprint);
            _sheathMaterial.SetShaderParameter("footprint_extent", _entryField.FootprintExtent);

        }

        float radius = _entryField.Radius;
        AeroProfile profile = _vessel.Profile;
        float curvature = (float)(cosine < 0.0f ? profile.BaseCurvature : profile.TipCurvature);
        curvature = Mathf.Lerp(curvature, radius, sine * sine);
        float standoff = Mathf.Clamp(curvature * 0.07f, radius * 0.06f, radius * 0.20f);
        float densityLength = Mathf.Lerp(1.0f, 0.4f, Mathf.SmoothStep(0.025f, 0.18f, (float)air.Density));
        float wake = radius * (6.0f + 5.0f * Mathf.Min(_sheathHeat, 1.0f)) * 1.5f * densityLength;
        float ablation = HasShield(_vessel.Leading) ? Mathf.Clamp(((float)_vessel.SkinTemperature - 900.0f) / 600.0f, 0.0f, 1.0f) : 0.0f;
        double ambient = body.Atmosphere.TemperatureAt(altitude);
        EntrySpectrum spectrum = EntrySpectrum.For(air, ambient, _vessel.SkinTemperature);

        foreach (ShaderMaterial material in new[] { _sheathMaterial, _wakeMaterial }) {

            material.SetShaderParameter("flow_to_body", transform);
            material.SetShaderParameter("intensity", _sheathHeat);
            material.SetShaderParameter("heat", Mathf.Clamp((float)air.AirSpeed / 5000.0f, 0.0f, 1.0f));
            material.SetShaderParameter("standoff", standoff);
            material.SetShaderParameter("wake_length", wake);
            material.SetShaderParameter("ablation", ablation);
            material.SetShaderParameter("effect_time", _effectTime);
        material.SetShaderParameter("sample_phase", (float)(Godot.Engine.GetProcessFrames() % 8) * 0.61803399f);
            material.SetShaderParameter("air_hot_colour", spectrum.Hot);
            material.SetShaderParameter("air_cool_colour", spectrum.Cool);
            material.SetShaderParameter("ablation_colour", spectrum.Ablation);
            material.SetShaderParameter("cooling_rate", spectrum.CoolingRate);

        }

        float padding = standoff * 4.0f + radius * 0.08f;
        float half = (_entryField.Tip - _entryField.Base) * 0.5f;
        float lateral = radius * Mathf.Abs(cosine) + half * sine;
        float axial = half * Mathf.Abs(cosine) + radius * sine;
        Vector2 bowExtent = new Vector2(Mathf.Max(lateral, radius * 0.25f), radius);
        float bowOffset = radius * 0.16f + standoff * 0.35f;
        float bowBend = radius * 0.22f;
        _sheathMaterial.SetShaderParameter("bow_extent", bowExtent);
        _sheathMaterial.SetShaderParameter("bow_front", _entryField.Ahead);
        _sheathMaterial.SetShaderParameter("bow_offset", bowOffset);
        _sheathMaterial.SetShaderParameter("bow_bend", bowBend);

        // Bounds include the bow's full curved support, including its diffuse wings.
        float bowSlope = 2.0f * bowBend * 1.65f / Mathf.Min(bowExtent.X, bowExtent.Y);
        float bowPadding = radius * 0.5f * Mathf.Sqrt(1.0f + bowSlope * bowSlope);
        float bowTip = _entryField.Ahead + bowOffset;
        Vector3 bowLow = new Vector3(-bowExtent.X * 1.7f, bowTip - bowBend * 1.65f * 1.65f - bowPadding, -bowExtent.Y * 1.7f);
        Vector3 bowHigh = new Vector3(bowExtent.X * 1.7f, bowTip + bowPadding, bowExtent.Y * 1.7f);
        Bounds(_sheath, _sheathMaterial, new Vector3(-lateral - padding, -axial - padding, -radius - padding).Min(bowLow),
            new Vector3(lateral + padding, axial + padding, radius + padding).Max(bowHigh));

        Vector2 extent = _entryField.FootprintExtent * (1.0f + (axial + wake) * 0.075f / radius) + Vector2.One * padding;
        Bounds(_wake, _wakeMaterial, new Vector3(-extent.X, -axial - wake, -extent.Y),
            new Vector3(extent.X, axial + padding, extent.Y));

        _entryLight.Position = transform.Origin + flight * (axial + standoff);
        _entryLight.OmniRange = radius * 5.0f;
        _entryLight.LightEnergy = _sheathHeat * 0.65f;
        _entryLight.LightColor = spectrum.Hot;

    }

    private static bool HasShield(Stage stage) {

        foreach (Part part in stage.Parts) {

            if (part.Kind == PartKind.Shield) {

                return true;

            }

        }

        return false;

    }

}
