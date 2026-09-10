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
    private readonly System.Collections.Generic.List<SurfaceFlow> _responses = new();
    private double _environmentClock;
    private double _lastSimulationTime;
    private bool _responseBindingsDirty;
    public int SurfaceParcels { get; private set; }
    public double SurfaceWaveHeight { get; private set; }
    public double SurfaceMilliseconds { get; private set; }
    public int SurfaceFailures { get; private set; }

    public Vector3? Disturb(Vector3 nozzle, Vector3 axis, Vector3 bend, float length, float exit, float spread, float pressure, float power) {

        double time = Flight.Active.Time;
        Vector3d point = Frames.Origin + Frames.Sim(nozzle);
        Vector3d direction = Frames.Sim(axis.Normalized());
        Vector3d crossflow = Frames.Sim(bend);
        double altitude = point.Length - _body.Radius;
        float radius = exit + length * spread;
        if (altitude > CloudBase - length && altitude < CloudTop + length && _environmentClock - _lastWake >= 0.2) {

            int index = _wakeCursor++ % 8;
            _wakePoints[index] = _body.ToBodyFixed(point, time);
            _wakeDirections[index] = _body.ToBodyFixed(direction, time);
            _wakeTimes[index] = _environmentClock;
            _wakeRadii[index] = radius * Mathf.Sqrt(power);
            _wakeLengths[index] = length;
            _lastWake = _environmentClock;

        }
        float reach = length * 3.0f;
        if (_body.HeightAboveGround(point, time) > reach + bend.Length() * 9.0f || Vector3d.Dot(point.Normalized, direction) > -0.02) { return null; }
        Vector3d At(double distance) => point + direction * distance + crossflow * (distance * distance / (length * length));
        double Clearance(double distance) {

            Vector3d position = At(distance);
            Vector3d fixedPosition = _body.ToBodyFixed(position, time);
            double height = _body.HeightAboveGround(position, time);
            foreach (SurfaceFlow flow in _responses) {

                if (flow.Contains(fixedPosition)) { height -= flow.HeightOffset(fixedPosition); }

            }
            return height;

        }
        double low = 0.0;
        double high = 0.0;
        bool contact = false;
        for (int i = 1; i <= 16; i++) {

            high = reach * i / 16.0;
            if (Clearance(high) <= 0.0) {

                contact = true;
                break;

            }
            low = high;

        }
        if (!contact) { return null; }
        for (int i = 0; i < 7; i++) {

            double middle = (low + high) * 0.5;
            if (Clearance(middle) > 0.0) { low = middle; }
            else { high = middle; }

        }
        double distance = (low + high) * 0.5;
        Vector3d hit = At(distance);
        Vector3d fixedHit = _body.ToBodyFixed(hit, time);
        Vector3d arriving = (direction + crossflow * (2.0 * distance / (length * length))).Normalized;
        float incidence = (float)Math.Max(0.0, -Vector3d.Dot(arriving, hit.Normalized));
        float width = exit + (float)distance * spread + Math.Max((float)distance - length, 0.0f) * 0.18f;
        float impactPressure = pressure * exit * exit / (width * width) * incidence * incidence
            * (1.0f - Mathf.SmoothStep(reach * 0.7f, reach, (float)distance));
        if (impactPressure < 30.0f) { return null; }
        bool water = _body.Terrain.Elevation(fixedHit.Normalized) < -0.1;
        SurfaceFlow response = _responses.Find(flow => flow.Contains(fixedHit));
        if (response == null) {

            if (_responses.Count == 3) {

                _responses[0].QueueFree();
                _responses.RemoveAt(0);

            }
            response = new SurfaceFlow();
            AddChild(response);
            response.Build(_body, fixedHit, _cloudDetail);
            _responses.Add(response);
            _responseBindingsDirty = true;

        }
        response.Strike(fixedHit, _body.ToBodyFixed(arriving, time), width, impactPressure, water);
        return Frames.Point(hit);

    }

    private void SyncEffects(double time) {

        if (time < _lastSimulationTime) {

            foreach (SurfaceFlow response in _responses) { response.QueueFree(); }
            _responses.Clear();
            _responseBindingsDirty = true;
            Array.Clear(_wakeRadii);
            _lastWake = double.NegativeInfinity;

        }
        _lastSimulationTime = time;
        float elapsed = Mathf.Min((float)GetProcessDeltaTime(), 0.1f);
        _environmentClock += elapsed;
        int count = 0;
        for (int i = 0; i < 8; i++) {

            double age = _environmentClock - _wakeTimes[i];
            if (_wakeRadii[i] <= 0.0f || age < 0.0 || age >= 4.0) { continue; }
            Vector3 point = Frames.Direction(_body.ToInertial(_wakePoints[i], time));
            Vector3 axis = Frames.Direction(_body.ToInertial(_wakeDirections[i], time));
            float recovery = (float)(1.0 - age / 4.0);
            _wakeCentres[count] = new Vector4(point.X, point.Y, point.Z, _wakeRadii[i] * recovery);
            _wakeAxes[count++] = new Vector4(axis.X, axis.Y, axis.Z, _wakeLengths[i]);

        }
        SurfaceParcels = 0;
        SurfaceWaveHeight = 0.0;
        SurfaceMilliseconds = 0.0;
        for (int i = _responses.Count - 1; i >= 0; i--) {

            SurfaceFlow response = _responses[i];
            response.Advance(elapsed, time);
            if (response.SurveyFailed) { SurfaceFailures++; }
            SurfaceParcels += response.ActiveParcels;
            SurfaceWaveHeight = Math.Max(SurfaceWaveHeight, response.PeakWave);
            SurfaceMilliseconds += response.Milliseconds;
            if (response.Expired) {

                response.QueueFree();
                _responses.RemoveAt(i);
                _responseBindingsDirty = true;

            }

        }
        foreach (ShaderMaterial face in _faces) { SetEffects(face, time, count); }
        SetEffects(_clouds, time, count);
        _responseBindingsDirty = false;

    }

    private void SetEffects(ShaderMaterial material, double time, int count) {

        material.SetShaderParameter("environment_time", (float)_environmentClock);
        material.SetShaderParameter("wake_count", count);
        material.SetShaderParameter("wake_centres", _wakeCentres);
        material.SetShaderParameter("wake_axes", _wakeAxes);
        if (_responseBindingsDirty) { material.SetShaderParameter("response_count", _responses.Count); }
        for (int i = 0; i < _responses.Count; i++) { _responses[i].Bind(material, i, time, _responseBindingsDirty); }

    }

}
