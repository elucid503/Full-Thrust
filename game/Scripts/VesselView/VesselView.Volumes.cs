using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class VesselView {

    private static readonly EngineLimits RcsLimits = new();
    private const float NozzleMouth = 0.081f;
    private const float SheathFlux = 150_000.0f;

    // A nozzle rated for nothing is read as a typical booster bell, expanded to four tenths of an atmosphere.
    private const float DefaultExitPressure = 40_000.0f;

    private sealed class Engine {

        public int Index { get; init; }
        public string FuelName { get; set; }
        public float Power { get; set; }
        public EnginePhase Heard { get; set; }
        public Node3D Pivot { get; init; }
        public Plume Plume { get; set; }

    }

    private sealed class Jet {

        public Plume Plume { get; init; }
        public Vector3 Exit { get; init; }
        public Vector3 Axis { get; init; }
        public float Duty { get; set; }
        public bool Firing { get; set; }
        public EngineState Valve { get; } = new();

    }

    private static BoxMesh _proxy;
    private static NoiseTexture3D _noise;
    private static int _seeds;

    private EntryField _entryField;
    private MeshInstance3D _wake;
    private ShaderMaterial _wakeMaterial;
    private ShaderMaterial[] _entryMaterials;
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

        material.SetParameter("seed", (float)(_seeds++ * 7.31));
        material.SetParameter("flow_noise", _noise);

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

        material.SetParameter("bounds_min", low);
        material.SetParameter("bounds_max", high);
        volume.CustomAabb = new Aabb(low, high - low);

    }

    private static void AttachPlume(Node3D pivot, Node3D model, Piece piece, float bellRadius, float reach) {

        PlumeTemplate template = PlumeTemplate.For(piece.Stage.Fuel);
        Plume plume = Plume.Create($"Plume{piece.Engines.Count + 1}", template, bellRadius);
        plume.Position = new Vector3(0.0f, -reach, 0.0f);
        pivot.AddChild(plume);
        BellSkins(model, plume.Bell);

        piece.Engines.Add(new Engine {

            Index = piece.Engines.Count,
            FuelName = piece.Stage.Fuel?.Name,
            Pivot = pivot,
            Plume = plume,

        });

    }

    // The lower half of the engine model is the bell. Each engine gets its own copy so it heats alone.
    private static void BellSkins(Node3D model, List<StandardMaterial3D> skins) {

        Aabb whole = Bounds(model, Transform3D.Identity);
        float middle = whole.Position.Y + whole.Size.Y * 0.5f;

        foreach (MeshInstance3D mesh in Meshes(model)) {

            Aabb bounds = mesh.GetAabb();
            Transform3D transform = mesh.Transform;

            for (Node parent = mesh.GetParent(); parent != model && parent != null; parent = parent.GetParent()) {

                if (parent is Node3D spatial) {

                    transform = spatial.Transform * transform;

                }

            }

            if ((transform * bounds).GetCenter().Y > middle) {

                continue;

            }

            for (int surface = 0; surface < mesh.GetSurfaceOverrideMaterialCount(); surface++) {

                if (mesh.GetActiveMaterial(surface) is StandardMaterial3D skin) {

                    StandardMaterial3D copy = (StandardMaterial3D)skin.Duplicate();
                    mesh.SetSurfaceOverrideMaterial(surface, copy);
                    skins.Add(copy);

                }

            }

        }

    }

    private static void AttachCluster(Node3D node, Piece piece, float bellRadius, float ring, float deck, float reach) {

        if (piece.Engines.Count < 2) {

            return;

        }

        PlumeTemplate template = PlumeTemplate.For(piece.Stage.Fuel);

        if (template.ClusterLayers.Count == 0) {

            return;

        }

        piece.Cluster = Plume.Create("Cluster", template, ring + bellRadius, cluster: true);
        piece.Cluster.Position = new Vector3(0.0f, deck - reach, 0.0f);
        node.AddChild(piece.Cluster);

    }

    private void AttachImpact(Node3D node, Piece piece, float bellRadius) {

        piece.Impact = ExhaustImpact.Create(PlumeTemplate.For(piece.Stage.Fuel), bellRadius, piece.Engines.Count);
        node.AddChild(piece.Impact);

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

        Vector3 exit = position + axis * (NozzleReach * scale);

        if (surface != null) {

            // Imported hardware supplies its own exit; the generated bell's length does not apply.
            float reach = Mathf.Max(0.4f, NozzleGauge * scale * 4.0f);
            Godot.Collections.Dictionary hit = surface.IntersectSegment(position + axis * reach, position - axis * reach);
            exit = hit.Count > 0 ? hit["position"].AsVector3() : position - axis * (NozzleBase * scale);
            exit -= axis * (NozzleMouth * scale * 0.05f);

        }

        Plume plume = Plume.Create($"Jet{piece.Jets.Count + 1}", PlumeTemplate.Load("Hydrazine"), NozzleMouth * scale, jet: true);
        plume.Transform = new Transform3D(new Basis(side, -axis, side.Cross(-axis).Normalized()), exit);
        node.AddChild(plume);

        piece.Jets.Add(new Jet {

            Plume = plume,
            Exit = exit,
            Axis = axis,

        });

    }

    private void AttachSheath() {

        _sheathMaterial = VolumeMaterial("res://Shaders/Exhaust/Entry.gdshader", 4);
        _wakeMaterial = VolumeMaterial("res://Shaders/Exhaust/EntryWake.gdshader", 2);
        _entryMaterials = new[] { _sheathMaterial, _wakeMaterial };
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

        foreach (ShaderMaterial material in _entryMaterials) {

            material.SetParameter("hull_field", _entryField.Distance);
            material.SetParameter("field_domain", _entryField.Domain);
            material.SetParameter("body_radius", _entryField.Radius);

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

        CelestialBody body = Flight.Active.Body;
        double now = Flight.Active.Time;
        _effectDelta = Mathf.Max((float)(now - _lastExhaustTime), 0.0f);
        _lastExhaustTime = now;
        _effectTime += _effectDelta;

        (float pressure, float density) = Air();

        // Waterfall's atmosphere depth: density to a power that stretches the thin upper air out.
        float air = Mathf.Pow(Mathf.Clamp(density / 1.225f, 0.0f, 1.0f), 0.512f);
        float mach = _vessel.Aero.InAir ? Mathf.Clamp((float)_vessel.Aero.Mach / 6.0f, 0.0f, 1.0f) : 0.0f;

        Vector3d relativeAir = body.AirVelocityAt(_vessel.Position, now) - _vessel.Velocity;
        Vector3 wind = Basis * Frames.Direction(_vessel.Orientation.Conjugate.Rotate(relativeAir));
        float dynamicPressure = 0.5f * density * wind.LengthSquared();
        Vector3 surfaceWind = Frames.Direction(body.ToInertial(Weather.Along, now)) * (float)(body.Weather?.WindSpeedAt(now) ?? 0.0);

        foreach (Piece piece in _pieces) {

            if (piece.Engines.Count == 0) {

                continue;

            }

            Stage stage = piece.Stage;
            float exitPressure = stage.RatingPressurePascals > 0.0 ? (float)stage.RatingPressurePascals : DefaultExitPressure;
            float thrustEach = (float)stage.ThrustNewtons / Math.Max(stage.EngineCount, 1);
            float jetPressure = thrustEach / (Mathf.Pi * piece.BellRadius * piece.BellRadius);
            float lightShare = 1.0f / Mathf.Sqrt(piece.Engines.Count);

            int lit = 0;
            float powerSum = 0.0f;
            Vector3d exitSum = Vector3d.Zero;
            Vector3d axisSum = Vector3d.Zero;

            if (piece.Engines[0].FuelName != stage.Fuel?.Name) {

                Retemplate(piece);

            }

            foreach (Engine engine in piece.Engines) {

                EngineState state = stage.EngineStates[engine.Index];
                engine.Power = _vessel.Intact ? (float)state.Power : 0.0f;

                PlumeInputs inputs = new PlumeInputs {

                    Lit = _vessel.Intact && state.Phase is EnginePhase.Igniting or EnginePhase.Running,
                    Throttle = engine.Power,
                    Air = air,
                    Mach = mach,
                    Purge = _vessel.Intact ? (float)state.Purge : 0.0f,
                    Burnoff = _vessel.Intact ? (float)state.Burnoff : 0.0f,
                    Cluster = piece.Engines.Count > 1 ? (float)stage.EnginesLit / piece.Engines.Count : 0.0f,
                    LightShare = lightShare,

                    AmbientPressure = pressure,
                    ExitPressure = exitPressure,

                    EffectTime = _effectTime,
                    Delta = _effectDelta,

                };

                Flow(engine.Plume, wind, dynamicPressure, jetPressure, ref inputs);
                engine.Plume.Source = _vessel;
                engine.Plume.Drive(inputs);

                if (engine.Power > 0.001f) {

                    lit++;
                    powerSum += engine.Power;
                    Transform3D nozzle = engine.Plume.GlobalTransform;
                    exitSum += Frames.Sim(nozzle.Origin) * engine.Power;
                    axisSum += Frames.Sim(-nozzle.Basis.Y) * engine.Power;

                }

            }

            if (piece.Cluster != null) {

                // The shared tail starts at the live nozzles and follows their thrust-weighted
                // direction. Individual stream axes retain differential gimbal through the merge.
                if (lit > 0 && axisSum.LengthSquared > 0.000001) {
                    Vector3 up = Frames.Direction(-axisSum.Normalized);
                    piece.Cluster.GlobalTransform = new Transform3D(new Basis(new Quaternion(Vector3.Up, up)), Frames.Direction(exitSum / powerSum));
                }
                piece.Cluster.BeginStreams();
                foreach (Engine engine in piece.Engines) { piece.Cluster.AddStream(engine.Plume, engine.Power); }

                PlumeInputs shared = new PlumeInputs {

                    Lit = lit > 0,
                    Throttle = lit > 1 ? powerSum / piece.Engines.Count : 0.0f,
                    Air = air,
                    Mach = mach,
                    Cluster = (float)lit / piece.Engines.Count,
                    LightShare = lightShare,
                    AmbientPressure = pressure,
                    ExitPressure = exitPressure,
                    EffectTime = _effectTime,
                    Delta = _effectDelta,

                };

                Flow(piece.Cluster, wind, dynamicPressure, jetPressure, ref shared);
                piece.Cluster.Source = _vessel;
                piece.Cluster.Drive(shared);

            }

            if (piece.Impact != null) {

                Vector3d exit = lit > 0 ? Frames.Origin + exitSum / powerSum : Vector3d.Zero;
                Vector3d axis = lit > 0 ? axisSum.Normalized : Vector3d.UnitZ;
                piece.Impact.Sync(body, now, exit, axis, powerSum / piece.Engines.Count, surfaceWind, Main.SunDirection);

            }

        }

        SyncJets(air, pressure);

    }

    // Ambient air bends the tail sideways and stretches or stops it along the axis, in proportion
    // to how its dynamic pressure compares with the jet's own.
    private static void Flow(Plume plume, Vector3 wind, float dynamicPressure, float jetPressure, ref PlumeInputs inputs) {

        inputs.Stretch = 1.0f;

        if (dynamicPressure <= 0.0f || jetPressure <= 0.0f) {

            return;

        }

        Vector3 local = plume.GlobalBasis.Inverse() * wind;
        float speed = local.Length();

        if (speed < 0.001f) {

            return;

        }

        float ratio = Mathf.Clamp(dynamicPressure / jetPressure * 2.0f, 0.0f, 1.5f);
        Vector3 direction = local / speed;
        float downstream = -direction.Y;
        Vector3 lateral = new Vector3(direction.X, 0.0f, direction.Z);

        inputs.Bend = lateral * ratio;
        inputs.Stretch = downstream >= 0.0f ? 1.0f + 0.25f * ratio * downstream : 1.0f / (1.0f + 2.0f * ratio * -downstream);

    }

    // The fuel a stage burns can change under it; the plume is rebuilt from the new template.
    private void Retemplate(Piece piece) {

        PlumeTemplate template = PlumeTemplate.For(piece.Stage.Fuel);

        foreach (Engine engine in piece.Engines) {

            Plume replacement = Plume.Create(engine.Plume.Name, template, engine.Plume.ExitRadius);
            replacement.Position = engine.Plume.Position;
            replacement.Bell.AddRange(engine.Plume.Bell);
            engine.Pivot.AddChild(replacement);
            engine.Plume.QueueFree();
            engine.Plume = replacement;
            engine.FuelName = piece.Stage.Fuel?.Name;

        }

    }

    private void SyncJets(float air, float pressure) {

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

                jet.Plume.Source = _vessel;
                jet.Plume.Drive(new PlumeInputs {

                    Lit = wanted > 0.0f,
                    Throttle = jet.Duty,
                    Air = air,
                    Purge = (float)jet.Valve.Purge * 0.18f,
                    LightShare = 1.0f,
                    AmbientPressure = pressure,
                    ExitPressure = DefaultExitPressure,
                    Stretch = 1.0f,
                    EffectTime = _effectTime,
                    Delta = _effectDelta,

                });

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
            _wakeMaterial.SetParameter("footprint", _entryField.Footprint);
            _wakeMaterial.SetParameter("footprint_extent", _entryField.FootprintExtent);
            _sheathMaterial.SetParameter("footprint", _entryField.Footprint);
            _sheathMaterial.SetParameter("footprint_extent", _entryField.FootprintExtent);

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

        foreach (ShaderMaterial material in _entryMaterials) {

            material.SetParameter("flow_to_body", transform);
            material.SetParameter("intensity", _sheathHeat);
            material.SetParameter("heat", Mathf.Clamp((float)air.AirSpeed / 5000.0f, 0.0f, 1.0f));
            material.SetParameter("standoff", standoff);
            material.SetParameter("wake_length", wake);
            material.SetParameter("ablation", ablation);
            material.SetParameter("effect_time", _effectTime);
            material.SetParameter("air_hot_colour", spectrum.Hot);
            material.SetParameter("air_cool_colour", spectrum.Cool);
            material.SetParameter("ablation_colour", spectrum.Ablation);
            material.SetParameter("cooling_rate", spectrum.CoolingRate);

        }

        float padding = standoff * 4.0f + radius * 0.08f;
        float half = (_entryField.Tip - _entryField.Base) * 0.5f;
        float lateral = radius * Mathf.Abs(cosine) + half * sine;
        float axial = half * Mathf.Abs(cosine) + radius * sine;
        Vector2 bowExtent = new Vector2(Mathf.Max(lateral, radius * 0.25f), radius);
        float bowOffset = radius * 0.16f + standoff * 0.35f;
        float bowBend = radius * 0.22f;
        _sheathMaterial.SetParameter("bow_extent", bowExtent);
        _sheathMaterial.SetParameter("bow_front", _entryField.Ahead);
        _sheathMaterial.SetParameter("bow_offset", bowOffset);
        _sheathMaterial.SetParameter("bow_bend", bowBend);

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
