namespace FullThrust.Sim;

/// <summary>Four spherical gravity waves shared with OceanField.gdshaderinc; all inputs are body-fixed.</summary>
public static class Ocean {

    public const int WaveCount = 4;
    public const double Density = 1025.0;
    public readonly record struct Wave(Vector3d Along, double Cycles, double Amplitude, double Frequency, double Phase);
    public readonly record struct Surface(double Height, Vector3d Gradient, Vector3d Velocity);

    public static double Smooth(double low, double high, double value) {

        double t = Math.Clamp((value - low) / (high - low), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);

    }

    public static Wave Component(CelestialBody body, double time, int index) {

        double spread = index switch { 1 => 0.32, 2 => -0.4, 3 => 0.65, _ => 0.0 };
        Vector3d along = Weather.Along * Math.Cos(spread) + Weather.Axis * Math.Sin(spread);
        double wavelength = 128.0 / Math.Pow(2.0, index);
        double cycles = Math.Round(Math.Tau * body.Radius / wavelength);
        double speed = body.Weather?.SeaSpeedAt(time) ?? 10.0;
        double strength = 0.04 + 0.96 * Math.Pow(speed / 10.0, 1.5);
        double amplitude = 0.45 * strength / Math.Pow(2.0, index * 0.8);
        double frequency = Math.Sqrt(body.SurfaceGravity * cycles / body.Radius);
        return new Wave(along, cycles, amplitude, frequency, Math.IEEERemainder(index * 1.7 - frequency * time, Math.Tau));

    }

    public static Surface Sample(CelestialBody body, Vector3d point, double elevation, double time, double spacing = 0.0) {

        Vector3d up = point.Normalized;
        double height = 0.0;
        double vertical = 0.0;
        Vector3d gradient = Vector3d.Zero;
        Vector3d velocity = Vector3d.Zero;
        double depth = Math.Max(-elevation, 0.0);
        double speed = body.Weather?.SeaSpeedAt(time) ?? 10.0;
        double speedRate = body.Weather?.SeaSpeedRateAt(time) ?? 0.0;
        double strength = 0.04 + 0.96 * Math.Pow(speed / 10.0, 1.5);
        double strengthRate = 0.144 * Math.Sqrt(speed / 10.0) * speedRate;

        for (int index = 0; index < WaveCount; index++) {

            Wave wave = Component(body, time, index);
            double x = Vector3d.Dot(up, Weather.Origin);
            double y = Vector3d.Dot(up, wave.Along);
            double radiusSquared = x * x + y * y;
            double phase = wave.Cycles * Math.Atan2(y, x) + wave.Phase;
            double sine = Math.Sin(phase);
            double cosine = Math.Cos(phase);
            double k = wave.Cycles / body.Radius;
            double wavelength = Math.Tau / k;
            double resolved = 1.0 - Smooth(wavelength * 0.12, wavelength * 0.35, spacing);
            double amplitude = Math.Min(wave.Amplitude, depth * 0.08) * Smooth(0.0025, 0.04, radiusSquared) * resolved;
            Vector3d phaseGradient = (wave.Along * x - Weather.Origin * y) * (k / Math.Max(radiusSquared, 0.0025));
            height += amplitude * sine;
            gradient += phaseGradient * (amplitude * cosine);
            vertical -= amplitude * wave.Frequency * cosine;
            if (wave.Amplitude < depth * 0.08) { vertical += amplitude * strengthRate / strength * sine; }
            velocity += phaseGradient.Normalized * (amplitude * wave.Frequency * sine);

        }

        return new Surface(height, gradient, velocity + up * vertical);

    }

    public static double BedElevation(CelestialBody body, Vector3d point, double time) =>
        body.Terrain?.Elevation(body.ToBodyFixed(point, time)) ?? 0.0;

}
