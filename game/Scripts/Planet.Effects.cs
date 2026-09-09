using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class Planet {

    private readonly Vector3d[] _wakePoints = new Vector3d[8];
    private readonly Vector3d[] _wakeDirections = new Vector3d[8];
    private readonly double[] _wakeTimes = new double[8];
    private readonly float[] _wakeRadii = new float[8];
    private readonly float[] _wakeLengths = new float[8];
    private readonly Vector4[] _wakeCentres = new Vector4[8];
    private readonly Vector4[] _wakeAxes = new Vector4[8];
    private int _wakeCursor;
    private double _lastWake = double.NegativeInfinity;
    private Vector3d _impactPoint;
    private double _impactTime = double.NegativeInfinity;
    private float _impactRadius;
    private float _impactPower;
    private ShaderMaterial _steamMaterial;
    private MeshInstance3D _steam;

    public void Disturb(Vector3 nozzle, Vector3 axis, float length, float radius, float power) {

        double time = Flight.Active.Time;
        Vector3d point = Frames.Origin + Frames.Sim(nozzle);
        Vector3d direction = Frames.Sim(axis.Normalized());
        double altitude = point.Length - _body.Radius;
        if (altitude > CloudBase - length && altitude < CloudTop + length && time - _lastWake >= 0.2) {

            int index = _wakeCursor++ % 8;
            _wakePoints[index] = _body.ToBodyFixed(point, time);
            _wakeDirections[index] = _body.ToBodyFixed(direction, time);
            _wakeTimes[index] = time;
            _wakeRadii[index] = radius * Mathf.Sqrt(power);
            _wakeLengths[index] = length;
            _lastWake = time;

        }

        double b = Vector3d.Dot(point, direction);
        double discriminant = b * b - point.LengthSquared + _body.Radius * _body.Radius;
        if (b >= 0.0 || discriminant <= 0.0) { return; }

        double distance = -b - Math.Sqrt(discriminant);
        if (distance < 0.0 || distance > length * 2.0) { return; }

        Vector3d hit = point + direction * distance;
        Vector3d fixedHit = _body.ToBodyFixed(hit, time);
        if (_body.Terrain.Elevation(fixedHit.Normalized) >= -0.5) { return; }

        _impactPoint = fixedHit;
        _impactTime = time;
        _impactRadius = Math.Max(radius + (float)distance * 0.12f, 2.0f);
        _impactPower = power * (1.0f - (float)distance / (length * 2.0f));

    }

    private void SyncEffects(double time) {

        int count = 0;
        for (int i = 0; i < 8; i++) {

            double age = time - _wakeTimes[i];
            if (_wakeRadii[i] <= 0.0f || age < 0.0 || age >= 4.0) { continue; }

            Vector3 point = Frames.Direction(_body.ToInertial(_wakePoints[i], time));
            Vector3 axis = Frames.Direction(_body.ToInertial(_wakeDirections[i], time));
            float recovery = (float)(1.0 - age / 4.0);
            _wakeCentres[count] = new Vector4(point.X, point.Y, point.Z, _wakeRadii[i] * recovery);
            _wakeAxes[count++] = new Vector4(axis.X, axis.Y, axis.Z, _wakeLengths[i]);

        }

        float strength = time < _impactTime ? 0.0f : _impactPower * (float)Math.Exp(-(time - _impactTime) / 2.0);
        Vector3 impact = Frames.Direction(_body.ToInertial(_impactPoint, time));
        Vector4 water = new Vector4(impact.X, impact.Y, impact.Z, _impactRadius);
        foreach (ShaderMaterial face in _faces) {

            SetEffects(face, time, count, water, strength);

        }
        SetEffects(_clouds, time, count, water, strength);

        if (_steam == null) {

            _steamMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Steam.gdshader"), RenderPriority = 3 };
            _steamMaterial.SetShaderParameter("flow_noise", _cloudDetail);
            _steam = new MeshInstance3D {

                Mesh = new BoxMesh { Size = Vector3.One },
                MaterialOverride = _steamMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Layers = 2,

            };
            AddChild(_steam);

        }

        _steam.Visible = strength > 0.005f;
        if (!_steam.Visible) { return; }

        Vector3 up = impact.Normalized();
        Frames.Horizon(up, out Vector3 side, out Vector3 ahead);
        _steam.Transform = new Transform3D(new Basis(side, up, ahead), Frames.Point(_body.ToInertial(_impactPoint, time)));
        float reach = _impactRadius * 5.0f;
        Vector3 low = new Vector3(-reach, -1.0f, -reach);
        Vector3 high = new Vector3(reach, reach * 2.0f, reach);
        _steam.CustomAabb = new Aabb(low, high - low);
        _steamMaterial.SetShaderParameter("bounds_min", low);
        _steamMaterial.SetShaderParameter("bounds_max", high);
        _steamMaterial.SetShaderParameter("effect_time", (float)time);
        _steamMaterial.SetShaderParameter("strength", strength);
        _steamMaterial.SetShaderParameter("radius", _impactRadius);
        _steamMaterial.SetShaderParameter("daylight", Math.Max(up.Dot(Main.SunDirection), 0.0f));

    }

    private void SetEffects(ShaderMaterial material, double time, int count, Vector4 water, float strength) {

        material.SetShaderParameter("environment_time", (float)time);
        material.SetShaderParameter("wake_count", count);
        material.SetShaderParameter("wake_centres", _wakeCentres);
        material.SetShaderParameter("wake_axes", _wakeAxes);
        material.SetShaderParameter("water_impact", water);
        material.SetShaderParameter("impact_strength", strength);

    }

}
