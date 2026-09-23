using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class Planet {

    private readonly Vector3[] _oceanAlong = new Vector3[Ocean.WaveCount];
    private readonly Vector4[] _oceanModes = new Vector4[Ocean.WaveCount];
    internal static void SetOcean(ShaderMaterial material, CelestialBody body, double time, Vector3[] along, Vector4[] modes) {

        FillOcean(body, time, along, modes);
        ApplyOcean(material, body, time, along, modes);

    }

    private static void FillOcean(CelestialBody body, double time, Vector3[] along, Vector4[] modes) {

        for (int index = 0; index < Ocean.WaveCount; index++) {

            Ocean.Wave wave = Ocean.Component(body, time, index);
            along[index] = Frames.Direction(wave.Along);
            modes[index] = new Vector4((float)wave.Cycles, (float)wave.Amplitude, (float)wave.Frequency, (float)wave.Phase);

        }

    }

    private static void ApplyOcean(ShaderMaterial material, CelestialBody body, double time, Vector3[] along, Vector4[] modes) {

        material.SetParameter("ocean_origin", Frames.Direction(Weather.Origin));
        material.SetParameter("ocean_along", along);
        material.SetParameter("ocean_modes", modes);
        material.SetParameter("ocean_radius", (float)body.Radius);
        material.SetParameter("ocean_wind", (float)(body.Weather?.SeaSpeedAt(time) ?? 10.0));
        material.SetParameter("ocean_wind_axis", Frames.Direction(Weather.Axis));

    }

    private void SyncWeather(double time) {

        float coverage = (float)(_body.Weather?.CoverageAt(time) ?? 0.0);
        FillOcean(_body, time, _oceanAlong, _oceanModes);
        foreach (ShaderMaterial face in _faces) {

            ApplyOcean(face, _body, time, _oceanAlong, _oceanModes);
            face.SetParameter("weather_coverage", coverage);

        }
        ApplyOcean(_water, _body, time, _oceanAlong, _oceanModes);
        _water.SetParameter("weather_coverage", coverage);
        _clouds.SetParameter("weather_coverage", coverage);
        _cloudShadows.SetWeather(coverage);

    }

}
