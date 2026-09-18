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

        double spread = index switch { 1 => 0.57, 2 => -0.68, 3 => 1.1, _ => 0.0 };
        Vector3d along = Weather.Along * Math.Cos(spread) + Weather.Axis * Math.Sin(spread);
        double wavelength = index switch { 1 => 79.0, 2 => 43.0, 3 => 19.0, _ => 137.0 };
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
            Vector3d across = Vector3d.Cross(Weather.Origin, wave.Along);
            double z = Vector3d.Dot(up, across);
            Vector3d acrossGradient = (across - up * z) / body.Radius;
            Vector3d alongGradient = (wave.Along - up * y) / body.Radius;
            double bendCycles = Math.Round(wave.Cycles * 0.23);
            double packetAlong = Math.Round(wave.Cycles * 0.17);
            double packetAcross = Math.Round(wave.Cycles * 0.13);
            double bend = z * bendCycles + index * 2.31;
            double packet = y * packetAlong + z * packetAcross + index * 4.17;
            double envelope = 0.6 + 0.4 * Math.Sin(packet);
            double phase = wave.Cycles * Math.Atan2(y, x) + wave.Phase + 1.8 * Math.Sin(bend);
            double sine = Math.Sin(phase);
            double cosine = Math.Cos(phase);
            double k = wave.Cycles / body.Radius;
            double wavelength = Math.Tau / k;
            double resolved = 1.0 - Smooth(wavelength * 0.12, wavelength * 0.35, spacing);
            double amplitude = Math.Min(wave.Amplitude, depth * 0.08) * Smooth(0.0025, 0.04, radiusSquared) * resolved;
            Vector3d phaseGradient = (wave.Along * x - Weather.Origin * y) * (k / Math.Max(radiusSquared, 0.0025));
            phaseGradient += acrossGradient * (1.8 * bendCycles * Math.Cos(bend));
            Vector3d envelopeGradient = (alongGradient * packetAlong + acrossGradient * packetAcross) * (0.4 * Math.Cos(packet));
            height += amplitude * envelope * sine;
            gradient += (phaseGradient * (envelope * cosine) + envelopeGradient * sine) * amplitude;
            vertical -= amplitude * envelope * wave.Frequency * cosine;
            if (wave.Amplitude < depth * 0.08) { vertical += amplitude * envelope * strengthRate / strength * sine; }
            velocity += phaseGradient.Normalized * (amplitude * envelope * wave.Frequency * sine);

        }

        return new Surface(height, gradient, velocity + up * vertical);

    }

    public static double BedElevation(CelestialBody body, Vector3d point, double time) =>
        body.Terrain?.Elevation(body.ToBodyFixed(point, time)) ?? 0.0;

}
