using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class Planet {

    private const int PuffBudget = 56;
    private const double PuffLife = 12.0;

    private readonly Vector3d[] _wakePoints = new Vector3d[8];
    private readonly Vector3d[] _wakeDirections = new Vector3d[8];
    private readonly double[] _wakeTimes = new double[8];
    private readonly float[] _wakeRadii = new float[8];
    private readonly float[] _wakeLengths = new float[8];
    private readonly float[] _wakePowers = new float[8];
    private readonly float[] _wakeSpreads = new float[8];
    private readonly Vector4[] _wakeCentres = new Vector4[8];
    private readonly Vector4[] _wakeAxes = new Vector4[8];
    private readonly Vector4[] _wakeStates = new Vector4[8];
    private readonly Vector4[] _wakeShadowCentres = new Vector4[8];
    private readonly Vector4[] _wakeShadowAxes = new Vector4[8];
    public int CloudWakeCount { get; private set; }
    private int _wakeCursor;
    private double _lastWake = double.NegativeInfinity;
    private sealed class SurfacePuff {

        public Vector3d Point;
        public Vector3d Velocity;
        public Vector3d Up;
        public Vector3d Drift;
        public float Expansion;
        public double Born;
        public float Radius;
        public float Power;
        public bool Water;

    }

    private sealed class SurfaceWake {

        public Vector3d Point;
        public Vector3d Normal;
        public double Time;
        public double Started;
        public double NextEmission;
        public float Radius;
        public float Power;
        public bool Water;
        public readonly List<SurfacePuff> Puffs = new();
        public bool Deluge;
        public ulong SampleFrame = ulong.MaxValue;
        public Vector3d Outlet;
        public readonly Vector4[] Centres = new Vector4[PuffBudget];
        public readonly Vector4[] States = new Vector4[PuffBudget];
        public ShaderMaterial Material;
        public MeshInstance3D Volume;

    }

    private readonly Random _smokeRandom = new();
    private double _surfaceClock;
    private readonly List<SurfaceWake> _surfaceWakes = new();
    private readonly Stack<(MeshInstance3D Volume, ShaderMaterial Material)> _surfaceVolumePool = new();
    private int _surfaceWarmupFrames = 3;
    private Shader _steamShader;
    private Shader _waterSprayShader;

    private void BuildSurfaceVolumes() {

        BoxMesh mesh = new() { Size = Vector3.One };
        Shader shader = GD.Load<Shader>("res://Shaders/Steam.gdshader");
        _steamShader = shader;
        _waterSprayShader = GD.Load<Shader>("res://Shaders/WaterSpray.gdshader");
        for (int index = 0; index < 8; index++) {

            ShaderMaterial material = new() { Shader = shader, RenderPriority = 3 };
            material.SetShaderParameter("flow_noise", _smokeNoise);
            material.SetShaderParameter("bounds_min", -Vector3.One * 2.0f);
            material.SetShaderParameter("bounds_max", Vector3.One * 2.0f);
            MeshInstance3D volume = new() {

                Mesh = mesh,
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Layers = 2,
                CustomAabb = new Aabb(-Vector3.One * 100.0f, Vector3.One * 200.0f),

            };
            AddChild(volume);
            _surfaceVolumePool.Push((volume, material));

        }

    }

    private void RecycleSurfaceVolume(SurfaceWake wake) {

        if (wake.Volume == null) { return; }
        wake.Volume.Visible = false;
        wake.Material.SetShaderParameter("puff_count", 0);
        _surfaceVolumePool.Push((wake.Volume, wake.Material));
        wake.Volume = null;
        wake.Material = null;

    }

    public void Disturb(Vector3 nozzle, Vector3 axis, float length, float radius, float power, float spread) {

        double time = Flight.Active.Time;
        _surfaceClock = time;
        Vector3d point = Frames.Origin + Frames.Sim(nozzle);
        Vector3d direction = Frames.Sim(axis.Normalized());
        double altitude = point.Length - _body.Radius;
        if (power > 0.01f && altitude > CloudBase - length && altitude < CloudTop + length && time - _lastWake >= 0.5) {

            int index = _wakeCursor++ % 8;
            _wakePoints[index] = _body.ToBodyFixed(point, time);
            _wakeDirections[index] = _body.ToBodyFixed(direction, time);
            _wakeTimes[index] = time;
            _wakeRadii[index] = Math.Max(radius, 6.0f);
            _wakeLengths[index] = length;
            _wakePowers[index] = Math.Min(Mathf.Sqrt(power), 1.0f);
            _wakeSpreads[index] = Mathf.Clamp(spread, 0.04f, 0.3f);
            _lastWake = time;

        }

        var contact = ExhaustInteraction.Surface(_body, point, direction, length, time);
        if (contact is not ExhaustInteraction.SurfaceHit hit) { return; }

        Vector3d fixedPoint = _body.ToBodyFixed(hit.Point, time);
        float reach = Math.Max(radius + (float)hit.Distance * spread, 0.5f);
        if (hit.Water) { reach = Math.Min(reach, 12.0f); }
        float strength = power * (1.0f - (float)hit.Distance / length);
        LaunchSite site = Flight.Active.Site;
        Vector3d pad = site.Up * (_body.Radius + site.Height);
        bool deluge = !hit.Water && (fixedPoint - pad).Length < 7.0;
        Vector3d outlet = Vector3d.Zero;

        if (deluge) {

            Vector3d nozzleFixed = _body.ToBodyFixed(point, time);
            outlet = site.East * (Vector3d.Dot(nozzleFixed - pad, site.East) < 0.0 ? -1.0 : 1.0);
            fixedPoint = pad + outlet * 14.0 + site.Up * 0.3;
            reach = 3.2f;

        }

        // A cluster scours one patch of ground, so nearby nozzles share a wake instead of stacking volumes.
        SurfaceWake wake = null;
        foreach (SurfaceWake candidate in _surfaceWakes) {

            if (candidate.Water == hit.Water && (fixedPoint - candidate.Point).Length < Math.Max(candidate.Radius + reach, 12.0)) {

                wake = candidate;
                break;

            }

        }

        if (wake == null) {

            wake = new SurfaceWake { Started = _surfaceClock, Point = fixedPoint, Time = double.NegativeInfinity };
            _surfaceWakes.Add(wake);

            if (_surfaceWakes.Count > 8) {

                RecycleSurfaceVolume(_surfaceWakes[0]);
                _surfaceWakes.RemoveAt(0);

            }

        }

        if (wake.SampleFrame == Engine.GetProcessFrames()) {

            wake.Radius = Math.Max(wake.Radius, (float)(fixedPoint - wake.Point).Length + reach);
            wake.Power = Math.Min(wake.Power + strength, 3.0f);
            wake.Water |= hit.Water;

            return;

        }

        if (_surfaceClock - wake.Time > 0.2) { wake.Started = _surfaceClock; }
        wake.Point = fixedPoint;
        wake.SampleFrame = Engine.GetProcessFrames();
        wake.Normal = _body.ToBodyFixed(hit.Normal, time);
        wake.Time = _surfaceClock;
        wake.Radius = reach;
        wake.Power = strength;
        wake.Water = hit.Water;
        wake.Deluge = deluge;
        wake.Outlet = outlet;

    }

    private void SyncEffects(double time) {

        if (_surfaceWarmupFrames > 0 && --_surfaceWarmupFrames == 0) {

            foreach (var item in _surfaceVolumePool) { item.Volume.Visible = false; }

        }

        _surfaceClock = time;

        int count = 0;
        for (int i = 0; i < 8; i++) {

            double age = time - _wakeTimes[i];
            if (_wakeRadii[i] <= 0.0f || age < 0.0 || age >= 4.0) { continue; }

            Vector3d fixedPoint = CloudWind.Advect(_wakePoints[i] + _wakeDirections[i] * (age * 12.0), age, _body.Radius, _body.Weather, time - age);
            Vector3d fixedAxis = CloudWind.Advect(_wakeDirections[i], age, _body.Radius, _body.Weather, time - age);
            Vector3 point = Frames.Direction(_body.ToInertial(fixedPoint, time));
            Vector3 axis = Frames.Direction(_body.ToInertial(fixedAxis, time));
            float strength = _wakePowers[i] * (float)(Landscape.Smooth(0.0, 0.15, age) * (1.0 - Landscape.Smooth(2.0, 4.0, age)));
            _wakeStates[count] = new Vector4(strength, _wakeSpreads[i], (float)age, 0.0f);
            _wakeCentres[count] = new Vector4(point.X, point.Y, point.Z, _wakeRadii[i] + (float)age * 4.0f);
            Vector3 shadowPoint = Frames.Direction(fixedPoint);
            Vector3 shadowAxis = Frames.Direction(fixedAxis);
            _wakeShadowCentres[count] = new Vector4(shadowPoint.X, shadowPoint.Y, shadowPoint.Z, _wakeCentres[count].W);
            _wakeShadowAxes[count] = new Vector4(shadowAxis.X, shadowAxis.Y, shadowAxis.Z, _wakeLengths[i]);
            _wakeAxes[count++] = new Vector4(axis.X, axis.Y, axis.Z, _wakeLengths[i]);

        }

        Vector4 water = Vector4.Zero;
        float waterStrength = 0.0f;

        for (int w = _surfaceWakes.Count - 1; w >= 0; w--) {

            SurfaceWake wake = _surfaceWakes[w];
            double idle = _surfaceClock - wake.Time;
            float strength = wake.Power * (float)Math.Exp(-idle / (wake.Water ? 0.25 : 1.0));
            wake.Puffs.RemoveAll(puff => _surfaceClock - puff.Born >= PuffLife);
            if (!wake.Water && idle < 0.15 && _surfaceClock >= wake.NextEmission && wake.Puffs.Count < PuffBudget) {

                bool steam = wake.Water || wake.Deluge;
                Vector3d up = wake.Normal;
                Vector3d side = Vector3d.Cross(up, Math.Abs(up.Z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX).Normalized;
                Vector3d ahead = Vector3d.Cross(up, side);
                double angle = _smokeRandom.NextDouble() * Math.Tau;
                Vector3d radial = side * Math.Cos(angle) + ahead * Math.Sin(angle);
                float initialRadius = Math.Clamp(wake.Radius * (steam ? 0.8f : 1.25f), steam ? 2.0f : 3.0f, 6.5f);
                initialRadius *= 0.85f + _smokeRandom.NextSingle() * 0.35f;
                float formation = (float)(1.0 - Math.Exp(-(_surfaceClock - wake.Started) / 0.6));
                wake.Puffs.Add(new SurfacePuff {

                    Point = wake.Point + radial * (wake.Radius * 0.65) + up * (initialRadius * 0.35),
                    Velocity = radial * (0.75 + _smokeRandom.NextDouble() * 0.5) * (steam ? 1.2 + Math.Sqrt(wake.Radius) * 0.5 : 3.2 + Math.Sqrt(wake.Radius) * 0.7)
                        + up * (steam ? 3.4 : 3.0) + wake.Outlet * 9.0,
                    Up = up,
                    Drift = (side * (_smokeRandom.NextDouble() - 0.5) + ahead * (_smokeRandom.NextDouble() - 0.5)) * 1.2,
                    Expansion = 0.8f + _smokeRandom.NextSingle() * 0.4f,
                    Born = _surfaceClock,
                    Radius = initialRadius,
                    Power = wake.Power * formation * (steam ? 0.55f : 1.1f) / initialRadius,
                    Water = steam,

                });
                wake.NextEmission = _surfaceClock + 0.15 + _smokeRandom.NextDouble() * 0.07;

            }

            if (idle > (wake.Water ? 1.5 : PuffLife) && wake.Puffs.Count == 0) {

                RecycleSurfaceVolume(wake);
                _surfaceWakes.RemoveAt(w);

                continue;

            }

            Vector3 impact = Frames.Point(_body.ToInertial(wake.Point, time));
            if (wake.Water && strength > waterStrength) {

                water = new Vector4(impact.X, impact.Y, impact.Z, wake.Radius);
                waterStrength = strength;

            }

            if (wake.Volume == null) {

                (wake.Volume, wake.Material) = _surfaceVolumePool.Pop();
                wake.Material.Shader = wake.Water ? _waterSprayShader : _steamShader;
                wake.Material.SetShaderParameter("flow_noise", _smokeNoise);

            }

            if (wake.Water) {

                SyncWaterSpray(wake, time, Math.Min(strength, 1.0f));
                continue;

            }

            wake.Volume.Visible = wake.Puffs.Count > 0;
            if (!wake.Volume.Visible) { continue; }
            Vector3d anchor = wake.Puffs[0].Point;
            Vector3 upAxis = Frames.Direction(_body.ToInertial(anchor.Normalized, time));
            Frames.Horizon(upAxis, out Vector3 right, out Vector3 forward);
            Basis basis = new Basis(right, upAxis, forward);
            Basis local = basis.Inverse();
            wake.Volume.Transform = new Transform3D(basis, Frames.Point(_body.ToInertial(anchor, time)));
            Vector3 origin = local * Frames.Direction(_body.ToInertial(wake.Point - anchor, time));
            Vector3 normal = local * Frames.Direction(_body.ToInertial(wake.Normal, time));
            Vector3 low = Vector3.One * float.PositiveInfinity;
            Vector3 high = Vector3.One * float.NegativeInfinity;
            float column = 1.0f;
            for (int i = 0; i < wake.Puffs.Count; i++) {

                SurfacePuff puff = wake.Puffs[i];
                float age = (float)(_surfaceClock - puff.Born);
                float expansion = puff.Water ? 1.1f : 1.3f;
                float radius = puff.Radius + expansion * puff.Expansion * age;
                Vector3d displacement = puff.Velocity * (2.0 * (1.0 - Math.Exp(-age / 2.0)))
                    + puff.Up * ((puff.Water ? 0.95 : 0.8) * age * age)
                    + puff.Drift * (age * (1.0 - Math.Exp(-age / 2.0)));
                Vector3 centre = local * Frames.Direction(_body.ToInertial(puff.Point + displacement - anchor, time));
                // Constant mass would thin as the cube of the radius; an entraining column holds column density.
                float dilution = Mathf.Pow(puff.Radius / radius, 2.0f);
                float fade = Mathf.SmoothStep(0.0f, 0.35f, age) * (1.0f - Mathf.SmoothStep((float)PuffLife - 5.0f, (float)PuffLife, age));
                wake.Centres[i] = new Vector4(centre.X, centre.Y, centre.Z, radius);
                wake.States[i] = new Vector4(puff.Power * dilution * fade, puff.Water ? 1.0f : 0.0f, age, 0.0f);
                column = Mathf.Max(column, normal.Dot(centre - origin) + radius);
                low = low.Min(centre - Vector3.One * radius);
                high = high.Max(centre + Vector3.One * radius);

            }

            wake.Volume.CustomAabb = new Aabb(low, high - low);
            wake.Material.SetShaderParameter("bounds_min", low);
            wake.Material.SetShaderParameter("bounds_max", high);
            wake.Material.SetShaderParameter("puff_count", wake.Puffs.Count);
            wake.Material.SetShaderParameter("puff_centres", wake.Centres);
            wake.Material.SetShaderParameter("puff_states", wake.States);
            wake.Material.SetShaderParameter("ground_plane", new Vector4(normal.X, normal.Y, normal.Z, -normal.Dot(origin)));
            wake.Material.SetShaderParameter("field_origin", origin);
            wake.Material.SetShaderParameter("detail_scale", 1.0f / Math.Clamp(wake.Radius * 1.5f, 20.0f, 50.0f));
            wake.Material.SetShaderParameter("column_height", column);
            wake.Material.SetShaderParameter("effect_time", (float)_surfaceClock);
            wake.Material.SetShaderParameter("sun_direction", local * Main.SunDirection);
            wake.Material.SetShaderParameter("daylight", Math.Max(upAxis.Dot(Main.SunDirection), 0.0f));

        }

        // Merged wakes accumulate power, but the surface shaders were authored against a single nozzle.
        float splash = Math.Min(waterStrength, 1.0f);

        foreach (ShaderMaterial face in _faces) { SetEffects(face, time, count, water, splash); }
        SetEffects(_clouds, time, count, water, splash);
        _cloudShadows.SetWakes(count, _wakeShadowCentres, _wakeShadowAxes, _wakeStates);
        CloudWakeCount = count;

    }

    private void SetEffects(ShaderMaterial material, double time, int count, Vector4 water, float strength) {

        material.SetShaderParameter("environment_time", (float)time);
        material.SetShaderParameter("wake_count", count);
        material.SetShaderParameter("wake_centres", _wakeCentres);
        material.SetShaderParameter("wake_axes", _wakeAxes);
        material.SetShaderParameter("wake_states", _wakeStates);
        material.SetShaderParameter("water_impact", water);
        material.SetShaderParameter("impact_strength", strength);

    }

    private void SyncWaterSpray(SurfaceWake wake, double time, float strength) {

        Vector3d point = _body.ToInertial(wake.Point, time);
        double elevation = _body.Terrain.Elevation(wake.Point);
        double level = Ocean.Sample(_body, wake.Point, elevation, time).Height;
        point += point.Normalized * level;
        Vector3 up = Frames.Direction(point.Normalized);
        Frames.Horizon(up, out Vector3 right, out Vector3 forward);
        Basis basis = new(right, up, forward);
        Vector3 wind = basis.Inverse() * Frames.Direction(_body.Weather?.VelocityAt(_body, point, time) ?? Vector3d.Zero);
        float height = Math.Clamp(wake.Radius * 0.25f, 0.6f, 2.5f);
        float width = wake.Radius * 3.0f + wind.Length() * 0.3f;
        Vector3 low = new(-width, 0.0f, -width);
        Vector3 high = new(width, height * 2.5f, width);
        wake.Volume.Visible = strength > 0.002f;
        wake.Volume.Transform = new Transform3D(basis, Frames.Point(point));
        wake.Volume.CustomAabb = new Aabb(low, high - low);
        wake.Material.SetShaderParameter("bounds_min", low);
        wake.Material.SetShaderParameter("bounds_max", high);
        wake.Material.SetShaderParameter("spray_radius", wake.Radius);
        wake.Material.SetShaderParameter("spray_height", height);
        wake.Material.SetShaderParameter("spray_strength", strength);
        wake.Material.SetShaderParameter("spray_wind", wind);
        wake.Material.SetShaderParameter("effect_time", (float)time);
        wake.Material.SetShaderParameter("daylight", Math.Max(up.Dot(Main.SunDirection), 0.0f));

    }

}
