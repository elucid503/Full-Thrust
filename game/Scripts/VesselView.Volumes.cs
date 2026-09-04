using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class VesselView {

    private const float NozzleMouth = 0.081f;
    private const float SheathFlux = 150_000.0f;

    private sealed class Jet {

        public MeshInstance3D Volume { get; init; }
        public ShaderMaterial Material { get; init; }
        public Vector3 Exit { get; init; }
        public Vector3 Axis { get; init; }
        public float Radius { get; init; }
        public float Duty { get; set; }

    }

    private static readonly Dictionary<(double, float), double> ExitMachCache = new Dictionary<(double, float), double>();

    private static BoxMesh _proxy;
    private static NoiseTexture3D _noise;
    private static int _seeds;

    private EntryField _entryField;
    private MeshInstance3D _wake;
    private ShaderMaterial _wakeMaterial;
    private OmniLight3D _entryLight;
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
        material.SetShaderParameter("flame_colour", chemistry.Flame);
        material.SetShaderParameter("afterburn", chemistry.Afterburn);
        material.SetShaderParameter("luminosity", chemistry.Luminosity);

    }

    private static void AttachPlume(Node3D node, Piece piece, float bellRadius, float bellPlane) {

        ShaderMaterial material = VolumeMaterial("res://Shaders/Plume.gdshader", 3);
        Colour(material, Chemistry.For(piece.Stage.Fuel));
        MeshInstance3D plume = Volume("Plume", material);
        plume.Position = new Vector3(0.0f, bellPlane, 0.0f);
        node.AddChild(plume);

        piece.Plume = plume;
        piece.PlumeMaterial = material;
        piece.BellRadius = bellRadius;

        OmniLight3D light = new OmniLight3D {

            Position = new Vector3(0.0f, bellPlane - bellRadius, 0.0f),
            OmniRange = bellRadius * 12.0f,
            OmniAttenuation = 1.3f,
            ShadowEnabled = false,
            Visible = false,

        };

        node.AddChild(light);
        piece.Lights.Add(light);

    }

    private static IEnumerable<(Vector3 Position, Vector3 Axis, Vector3 Side, float Scale)> Mounts(Stage stage, Part part) {

        float gauge = (float)part.Extent;
        float height = (float)part.Centre;

        float scale = gauge / NozzleGauge;

        float seat = part.Depth > 0.0
            ? (float)(stage.Hull.RadiusAt(height) - part.Depth)
            : (float)stage.Hull.RadiusAt(height);

        // A pocket takes the pair further apart than a pod does, because each bell has to clear the
        // sill and the lintel of the cut rather than just the skin.
        float spread = part.Depth > 0.0 ? RcsOffset : (float)part.Length * 0.28f;

        for (int index = 0; index < part.Count; index++) {

            float angle = Mathf.Tau * PortCentre(part.Count, index) / RadialSegments;

            Vector3 outward = Surface(stage, angle, height);
            Vector3 side = Vector3.Up.Cross(Radial(angle)).Normalized();

            // One nozzle canted forward and one aft: a port that only fired radially could not pitch.
            for (int sense = -1; sense <= 1; sense += 2) {

                Vector3 axis = (outward * Mathf.Cos(RcsCant) + Vector3.Up * (Mathf.Sin(RcsCant) * sense)).Normalized();

                Vector3 position = Radial(angle) * seat + axis * (NozzleBase * scale) + Vector3.Up * (height + spread * sense);

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

        ShaderMaterial material = VolumeMaterial("res://Shaders/Plume.gdshader", 3);
        Colour(material, Chemistry.Hydrazine);
        material.SetShaderParameter("sample_count", 24);
        MeshInstance3D volume = Volume("Jet", material);

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

    private void BakeProfile() {

        _entryField = new EntryField(_vessel);

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

        _effectDelta = Mathf.Min((float)GetProcessDeltaTime(), 0.1f);
        _effectTime += _effectDelta;
        _thrust = Mathf.Lerp(_thrust, (float)_vessel.ThrustSetting, 1.0f - Mathf.Exp(-_effectDelta / 0.065f));

        (float pressure, float density) = Air();

        foreach (Piece piece in _pieces) {

            if (piece.Plume == null) {

                continue;

            }

            bool burning = _thrust > 0.003f && piece.Stage == _vessel.Active;
            piece.Plume.Visible = burning;

            foreach (OmniLight3D light in piece.Lights) {

                light.Visible = burning;

            }

            if (!burning) {

                continue;

            }

            Chemistry chemistry = Chemistry.For(piece.Stage.Fuel);
            DriveVolume(piece.Plume, piece.PlumeMaterial, piece.Stage.ChamberPressure, piece.Stage.ExpansionRatio,
                chemistry, _thrust, piece.BellRadius, pressure, density, false);

            float air = Mathf.Clamp(density / 1.225f, 0.0f, 1.0f);

            foreach (OmniLight3D light in piece.Lights) {

                light.LightColor = chemistry.Core.Lerp(chemistry.Flame, air * chemistry.Afterburn);
                light.LightEnergy = _thrust * chemistry.Luminosity * 1.6f;

            }

        }

        SyncJets(pressure, density);

    }

    private void DriveVolume(MeshInstance3D volume, ShaderMaterial material, double chamberPressure, double expansionRatio,
        Chemistry chemistry, float throttle, float bellRadius, float pressure, float density, bool jet) {

        var key = (expansionRatio, chemistry.Gamma);

        if (!ExitMachCache.TryGetValue(key, out double mach)) {

            mach = Nozzle.ExitMach(expansionRatio, chemistry.Gamma);
            ExitMachCache.Add(key, mach);

        }

        Exhaust exhaust = Nozzle.ExpandFromMach(chamberPressure, mach, chemistry.Gamma, throttle, pressure, bellRadius);
        float exit = Mathf.Max(bellRadius * (float)exhaust.Contraction, bellRadius * 0.2f);
        float vacuum = pressure > 0.0f ? Mathf.Clamp((float)Math.Log10(Math.Max(exhaust.PressureRatio, 1.0)) / 3.0f, 0.0f, 1.0f) : 1.0f;
        float air = Mathf.Clamp(density / 1.225f, 0.0f, 1.0f);
        float turn = Mathf.Max((float)exhaust.TurnAngle, 0.0f);
        float spread = Mathf.Lerp(0.045f + air * 0.045f, Mathf.Tan(Mathf.Min(turn * 0.32f, 0.62f)), vacuum);
        float length = bellRadius * (jet ? 21.0f : 44.0f) * (0.45f + 0.55f * Mathf.Sqrt(throttle));
        float radius = (exit + length * spread) * 1.8f;

        float cells = pressure > 0.0f && exhaust.ShockCellLength > 0.0
            ? Mathf.Clamp((float)Math.Abs(Math.Log(exhaust.PressureRatio)), 0.0f, 1.0f) * (1.0f - vacuum) : 0.0f;

        Vector3 wind = volume.GlobalBasis.Inverse() * Frames.Direction(Flight.Active.Body.AirVelocityAt(_vessel.Position) - _vessel.Velocity);
        wind.Y = 0.0f;
        float momentum = (float)(chamberPressure * throttle * Nozzle.PressureRatio(mach, chemistry.Gamma) * chemistry.Gamma * mach * mach);
        float bend = Mathf.Clamp(density * wind.LengthSquared() / Mathf.Max(momentum, 1.0f), 0.0f, 0.6f);
        Vector3 crossflow = wind.LengthSquared() > 0.001f ? wind.Normalized() * (length * bend) : Vector3.Zero;

        material.SetShaderParameter("throttle", throttle);
        material.SetShaderParameter("exit_radius", exit);
        material.SetShaderParameter("plume_length", length);
        material.SetShaderParameter("spread", spread);
        material.SetShaderParameter("vacuum", vacuum);
        material.SetShaderParameter("air", air);
        material.SetShaderParameter("cell_length", (float)exhaust.ShockCellLength);
        material.SetShaderParameter("cell_strength", cells);
        material.SetShaderParameter("crossflow", crossflow);
        material.SetShaderParameter("effect_time", _effectTime);

        Vector3 low = new Vector3(-radius, -length, -radius) + crossflow.Min(Vector3.Zero);
        Vector3 high = new Vector3(radius, 0.0f, radius) + crossflow.Max(Vector3.Zero);

        Ground(volume, material, length, exit, ref low, ref high);
        Bounds(volume, material, low, high);

    }

    private void Ground(MeshInstance3D volume, ShaderMaterial material, float length, float exit, ref Vector3 low, ref Vector3 high) {

        CelestialBody body = Flight.Active.Body;
        Vector3d nozzle = _vessel.Position + Frames.Sim(volume.GlobalPosition - GlobalPosition);
        float altitude = (float)body.AltitudeOf(nozzle);
        Vector3 up = (volume.GlobalBasis.Inverse() * Frames.Direction(nozzle.Normalized)).Normalized();
        float impactDistance = up.Y > 0.02f ? altitude / up.Y : float.PositiveInfinity;
        float strength = impactDistance >= 0.0f && impactDistance < length ? 1.0f - impactDistance / length : 0.0f;

        material.SetShaderParameter("ground_normal", up);
        material.SetShaderParameter("ground_offset", altitude);
        material.SetShaderParameter("ground_strength", strength);

        if (strength <= 0.0f) {

            return;

        }

        Vector3 impact = Vector3.Down * impactDistance;
        float radius = exit * (5.0f + 4.0f * strength);
        float height = radius * 0.35f;
        Vector3 extent = new Vector3(
            Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - up.X * up.X)),
            Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - up.Y * up.Y)),
            Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - up.Z * up.Z))) * radius;

        low = low.Min(impact - extent + (up * height).Min(Vector3.Zero));
        high = high.Max(impact + extent + (up * height).Max(Vector3.Zero));

        material.SetShaderParameter("ground_impact", impact);
        material.SetShaderParameter("ground_radius", radius);

    }

    private void SyncJets(float pressure, float density) {

        bool armed = _vessel.HasRcs;
        double limit = _vessel.ControlTorqueLimit;
        Vector3 torque = armed && limit > 0.0 ? Frames.Direction(_vessel.ControlTorque) / (float)limit : Vector3.Zero;
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
                jet.Duty = Mathf.Lerp(jet.Duty, wanted, 1.0f - Mathf.Exp(-_effectDelta / (wanted > jet.Duty ? 0.025f : 0.07f)));
                jet.Volume.Visible = jet.Duty > 0.012f;

                if (jet.Volume.Visible) {

                    DriveVolume(jet.Volume, jet.Material, piece.Stage.RcsChamberPressure, piece.Stage.RcsExpansionRatio,
                        Chemistry.Hydrazine, 1.0f, jet.Radius, pressure, density, true);
                    jet.Material.SetShaderParameter("throttle", jet.Duty);

                }

            }

        }

    }

    private void SyncSheath() {

        AeroForces air = _vessel.Aero;
        float wanted = air.InAir ? Mathf.Clamp((float)(air.HeatFlux / SheathFlux), 0.0f, 1.4f) : 0.0f;
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
        double ambient = Flight.Active.Body.Atmosphere.TemperatureAt(Flight.Active.Body.AltitudeOf(_vessel.Position));
        EntrySpectrum spectrum = EntrySpectrum.For(air, ambient, _vessel.SkinTemperature);

        foreach (ShaderMaterial material in new[] { _sheathMaterial, _wakeMaterial }) {

            material.SetShaderParameter("flow_to_body", transform);
            material.SetShaderParameter("intensity", _sheathHeat);
            material.SetShaderParameter("heat", Mathf.Clamp((float)air.AirSpeed / 5000.0f, 0.0f, 1.0f));
            material.SetShaderParameter("standoff", standoff);
            material.SetShaderParameter("wake_length", wake);
            material.SetShaderParameter("ablation", ablation);
            material.SetShaderParameter("effect_time", _effectTime);
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
