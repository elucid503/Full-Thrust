using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class Planet {

    private readonly Vector3[] _oceanAlong = new Vector3[Ocean.WaveCount];
    private readonly Vector4[] _oceanModes = new Vector4[Ocean.WaveCount];
    internal const int RippleCount = 12;
    private readonly Vector4[] _rippleDirections = new Vector4[RippleCount];
    private readonly Vector4[] _rippleStates = new Vector4[RippleCount];

    internal static void SetRipples(ShaderMaterial material, CelestialBody body, double time, Vector3d origin,
        Vector4[] directions, Vector4[] states) {

        Vector3d fixedOrigin = body.ToBodyFixed(origin, time);
        double wind = body.Weather?.SeaSpeedAt(time) ?? 10.0;
        for (int index = 0; index < RippleCount; index++) {

            int band = index / 3;
            double wavelength = 7.0 * Math.Pow(0.57, band) * (1.0 + 0.32 * Math.Sin(index * 7.13));
            double angle = Math.Sin(index * 2.399 + 0.4) * 1.25;
            Vector3d direction = Weather.Along * Math.Cos(angle) + Weather.Axis * Math.Sin(angle);
            Vector3d across = Vector3d.Cross(Weather.Origin, direction);
            double k = Math.Tau / wavelength;
            double frequency = Math.Sqrt(body.SurfaceGravity * k);
            double slope = 0.025 * Math.Pow(Math.Max(wind, 0.15) / 10.0, 0.65) * Math.Pow(0.91, band);
            double phase = Math.IEEERemainder(Vector3d.Dot(fixedOrigin, direction) * k - frequency * time + index * 2.718, Math.Tau);
            double bend = Math.IEEERemainder(Vector3d.Dot(fixedOrigin, across) * k * 0.19 - frequency * time * 0.21 + index, Math.Tau);
            double packet = Math.IEEERemainder(Vector3d.Dot(fixedOrigin, direction) * k * 0.13 - frequency * time * 0.13 + index * 4.13, Math.Tau);
            Vector3 axis = Frames.Direction(direction);
            directions[index] = new Vector4(axis.X, axis.Y, axis.Z, (float)k);
            states[index] = new Vector4((float)phase, (float)slope, (float)bend, (float)packet);

        }
        material.SetShaderParameter("ripple_directions", directions);
        material.SetShaderParameter("ripple_states", states);
        material.SetShaderParameter("ripple_up", Frames.Direction(Weather.Origin));
        material.SetShaderParameter("ripple_detail_strength", (float)Math.Pow(Math.Max(wind, 0.15) / 10.0, 0.55));
        for (int band = 0; band < 5; band++) {

            double scale = band switch { 3 => 6.3, 4 => 23.7, _ => 0.8 * Math.Pow(0.32, band) };
            Vector3d drift = Weather.Along * (time * (0.16 + band * 0.07)) + Weather.Axis * (time * 0.053);
            Vector3d cell = (fixedOrigin - drift) / scale;
            Vector3 offset = Frames.Direction(new Vector3d(WrapCell(cell.X), WrapCell(cell.Y), WrapCell(cell.Z)));
            material.SetShaderParameter("ripple_detail_origin" + band, new Vector4(offset.X, offset.Y, offset.Z, (float)scale));

        }

    }

    private static double WrapCell(double value) => value - Math.Floor(value / 4096.0) * 4096.0;

    internal static void SetOcean(ShaderMaterial material, CelestialBody body, double time, Vector3[] along, Vector4[] modes) {

        for (int index = 0; index < Ocean.WaveCount; index++) {

            Ocean.Wave wave = Ocean.Component(body, time, index);
            along[index] = Frames.Direction(wave.Along);
            modes[index] = new Vector4((float)wave.Cycles, (float)wave.Amplitude, (float)wave.Frequency, (float)wave.Phase);

        }
        material.SetShaderParameter("ocean_origin", Frames.Direction(Weather.Origin));
        material.SetShaderParameter("ocean_along", along);
        material.SetShaderParameter("ocean_modes", modes);
        material.SetShaderParameter("ocean_radius", (float)body.Radius);
        material.SetShaderParameter("ocean_wind", (float)(body.Weather?.SeaSpeedAt(time) ?? 10.0));

    }

    private void SyncWeather(double time) {

        float coverage = (float)(_body.Weather?.CoverageAt(time) ?? 0.0);
        foreach (ShaderMaterial face in _faces) {

            SetOcean(face, _body, time, _oceanAlong, _oceanModes);
            SetRipples(face, _body, time, Frames.Origin, _rippleDirections, _rippleStates);
            face.SetShaderParameter("weather_coverage", coverage);

        }
        _clouds.SetShaderParameter("weather_coverage", coverage);
        _cloudShadows.SetWeather(coverage);

    }

}
