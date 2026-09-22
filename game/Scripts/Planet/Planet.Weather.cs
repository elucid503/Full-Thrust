using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class Planet {

    private readonly Vector3[] _oceanAlong = new Vector3[Ocean.WaveCount];
    private readonly Vector4[] _oceanModes = new Vector4[Ocean.WaveCount];
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
            face.SetShaderParameter("weather_coverage", coverage);

        }
        SetOcean(_water, _body, time, _oceanAlong, _oceanModes);
        _water.SetShaderParameter("weather_coverage", coverage);
        _clouds.SetShaderParameter("weather_coverage", coverage);
        _cloudShadows.SetWeather(coverage);

    }

}
