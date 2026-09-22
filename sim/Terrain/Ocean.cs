namespace FullThrust.Sim;

/// <summary>Six trochoidal swells on great circles, shared with OceanField.gdshaderinc; all inputs are
/// body-fixed. Band amplitudes follow a Pierson-Moskowitz spectrum of the current sea state.</summary>
public static class Ocean {

    public const int WaveCount = 6;
    public const double Density = 1025.0;

    // Integer cycles round the body are only seam-free while a band keeps its wavelength, so the sea
    // state moves energy between fixed bands rather than moving the bands.
    private static readonly double[] Wavelengths = { 151.0, 97.0, 61.0, 37.0, 23.0, 14.0 };
    private static readonly double[] Spreads = { 0.0, 0.57, -0.68, 1.1, -0.31, 0.83 };

    // Swell from distant weather: open water is never glass, whatever the local wind.
    private static readonly double[] Swell = { 0.22, 0.12, 0.0, 0.0, 0.0, 0.0 };

    private const double PhillipsConstant = 0.0081;
    private const double PeakShape = 0.74;

    public readonly record struct Wave(Vector3d Along, double Cycles, double Amplitude, double Frequency, double Phase, double Growth);
    public readonly record struct Surface(double Height, Vector3d Gradient, Vector3d Velocity);

    /// <summary>The water particle that rests at a point: its rise and horizontal shift, the slope of
    /// the height field over rest positions, and the time derivatives of rise and shift.</summary>
    public readonly record struct Particle(double Height, Vector3d Shift, Vector3d Slope, double Rise, Vector3d Drift);

    public static double Smooth(double low, double high, double value) {

        double t = Math.Clamp((value - low) / (high - low), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);

    }

    public static Wave Component(CelestialBody body, double time, int index) {

        Vector3d along = Weather.Along * Math.Cos(Spreads[index]) + Weather.Axis * Math.Sin(Spreads[index]);
        double cycles = Math.Round(Math.Tau * body.Radius / Wavelengths[index]);
        double gravity = body.SurfaceGravity;
        double frequency = Math.Sqrt(gravity * cycles / body.Radius);
        double speed = body.Weather?.SeaSpeedAt(time) ?? 10.0;
        double speedRate = body.Weather?.SeaSpeedRateAt(time) ?? 0.0;
        double wind = 0.0;
        double windGrowth = 0.0;

        if (speed > 0.1) {

            double ratio = Math.Pow(gravity / (speed * frequency), 4.0);
            double spectrum = PhillipsConstant * gravity * gravity / Math.Pow(frequency, 5.0) * Math.Exp(-PeakShape * ratio);
            wind = Math.Sqrt(2.0 * spectrum * Bandwidth(gravity, index));
            windGrowth = wind * 2.0 * PeakShape * ratio / speed * speedRate;

        }

        double amplitude = Math.Sqrt(wind * wind + Swell[index] * Swell[index]);
        double growth = amplitude > 0.0 ? wind * windGrowth / amplitude : 0.0;

        return new Wave(along, cycles, amplitude, frequency, Math.IEEERemainder(index * 1.7 - frequency * time, Math.Tau), growth);

    }

    // Each band reaches halfway, in log frequency, to its neighbours.
    private static double Bandwidth(double gravity, int index) {

        double Omega(int band) => Math.Sqrt(gravity * Math.Tau / Wavelengths[band]);

        double centre = Omega(index);
        double below = index > 0 ? Omega(index - 1) : centre * centre / Omega(1);
        double above = index < WaveCount - 1 ? Omega(index + 1) : centre * centre / Omega(WaveCount - 2);

        return Math.Sqrt(centre * above) - Math.Sqrt(centre * below);

    }

    public static void Components(CelestialBody body, double time, Span<Wave> waves) {

        for (int index = 0; index < WaveCount; index++) {

            waves[index] = Component(body, time, index);

        }

    }

    /// <summary>The particle resting at a point, before the trochoid moves it; what the GPU evaluates per vertex.</summary>
    public static Particle Displace(CelestialBody body, Vector3d point, double elevation, double time, double spacing = 0.0) {

        Span<Wave> waves = stackalloc Wave[WaveCount];
        Components(body, time, waves);

        return Evaluate(body, waves, point.Normalized, Math.Max(-elevation, 0.0), spacing, Vector3d.UnitX, Vector3d.UnitY, out _);

    }

    public static Surface Sample(CelestialBody body, Vector3d point, double elevation, double time, double spacing = 0.0) {

        Span<Wave> waves = stackalloc Wave[WaveCount];
        Components(body, time, waves);

        return Sample(body, waves, point, elevation, spacing);

    }

    /// <summary>The surface over a fixed point. Trochoids gather water into their crests, so the
    /// particle standing over the point is found by Newton iteration from the point itself.</summary>
    public static Surface Sample(CelestialBody body, ReadOnlySpan<Wave> waves, Vector3d point, double elevation, double spacing = 0.0) {

        double depth = Math.Max(-elevation, 0.0);

        if (depth <= 0.0) {

            return new Surface(0.0, Vector3d.Zero, Vector3d.Zero);

        }

        Vector3d up = point.Normalized;
        Vector3d first = Vector3d.Cross(Math.Abs(up.Z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX, up).Normalized;
        Vector3d second = Vector3d.Cross(up, first);
        Vector3d target = up * body.Radius;
        Vector3d source = up;
        Particle particle = Evaluate(body, waves, source, depth, spacing, first, second, out (double A, double B, double C, double D) squeeze);

        for (int iteration = 0; iteration < 4; iteration++) {

            Vector3d residual = target - source * body.Radius - particle.Shift;
            (double alongFirst, double alongSecond) = Solve(1.0 + squeeze.A, squeeze.B, squeeze.C, 1.0 + squeeze.D,
                Vector3d.Dot(residual, first), Vector3d.Dot(residual, second));
            source = (source * body.Radius + first * alongFirst + second * alongSecond).Normalized;
            particle = Evaluate(body, waves, source, depth, spacing, first, second, out squeeze);

        }

        // The iteration's Jacobian omits the envelope's own gradients; the slope needs the exact one.
        const double step = 0.01;
        Vector3d shiftFirst = Evaluate(body, waves, (source * body.Radius + first * step).Normalized, depth, spacing, first, second, out _).Shift - particle.Shift;
        Vector3d shiftSecond = Evaluate(body, waves, (source * body.Radius + second * step).Normalized, depth, spacing, first, second, out _).Shift - particle.Shift;
        double a = 1.0 + Vector3d.Dot(shiftFirst, first) / step;
        double b = Vector3d.Dot(shiftSecond, first) / step;
        double c = Vector3d.Dot(shiftFirst, second) / step;
        double d = 1.0 + Vector3d.Dot(shiftSecond, second) / step;
        (double slopeFirst, double slopeSecond) = Solve(a, c, b, d, Vector3d.Dot(particle.Slope, first), Vector3d.Dot(particle.Slope, second));

        return new Surface(particle.Height, first * slopeFirst + second * slopeSecond, particle.Drift + up * particle.Rise);

    }

    private static (double, double) Solve(double a, double b, double c, double d, double e, double f) {

        double determinant = a * d - b * c;

        return ((e * d - b * f) / determinant, (a * f - c * e) / determinant);

    }

    // Squeeze is the shift's Jacobian over rest positions in the (first, second) frame, less the
    // envelope's gradient: close enough for Newton to converge in a handful of steps.
    private static Particle Evaluate(CelestialBody body, ReadOnlySpan<Wave> waves, Vector3d up, double depth, double spacing,
        Vector3d first, Vector3d second, out (double A, double B, double C, double D) squeeze) {

        double height = 0.0;
        double rise = 0.0;
        Vector3d shift = Vector3d.Zero;
        Vector3d slope = Vector3d.Zero;
        Vector3d drift = Vector3d.Zero;
        squeeze = (0.0, 0.0, 0.0, 0.0);
        double x = Vector3d.Dot(up, Weather.Origin);

        for (int index = 0; index < waves.Length; index++) {

            Wave wave = waves[index];
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
            double fade = Smooth(0.0025, 0.04, radiusSquared) * (1.0 - Smooth(wavelength * 0.12, wavelength * 0.35, spacing));

            // Shoaling caps each band at a fraction of the depth, which also pins the shoreline to the datum.
            bool shoaled = wave.Amplitude >= depth * 0.08;
            double scale = (shoaled ? depth * 0.08 : wave.Amplitude) * fade;
            double amplitude = scale * envelope;
            double growth = shoaled ? 0.0 : wave.Growth * fade * envelope;

            Vector3d phaseGradient = (wave.Along * x - Weather.Origin * y) * (k / Math.Max(radiusSquared, 0.0025));
            phaseGradient += acrossGradient * (1.8 * bendCycles * Math.Cos(bend));
            Vector3d envelopeGradient = (alongGradient * packetAlong + acrossGradient * packetAcross) * (0.4 * Math.Cos(packet));
            Vector3d direction = phaseGradient.Normalized;

            height += amplitude * sine;
            slope += phaseGradient * (amplitude * cosine) + envelopeGradient * (scale * sine);
            shift += direction * (amplitude * cosine);
            rise += growth * sine - amplitude * wave.Frequency * cosine;
            drift += direction * (growth * cosine + amplitude * wave.Frequency * sine);

            double compression = -amplitude * sine;
            double directionFirst = Vector3d.Dot(direction, first);
            double directionSecond = Vector3d.Dot(direction, second);
            double gradientFirst = Vector3d.Dot(phaseGradient, first);
            double gradientSecond = Vector3d.Dot(phaseGradient, second);
            squeeze.A += compression * directionFirst * gradientFirst;
            squeeze.B += compression * directionFirst * gradientSecond;
            squeeze.C += compression * directionSecond * gradientFirst;
            squeeze.D += compression * directionSecond * gradientSecond;

        }

        return new Particle(height, shift, slope, rise, drift);

    }

    public static double BedElevation(CelestialBody body, Vector3d point, double time) =>
        body.Terrain?.Elevation(body.ToBodyFixed(point, time)) ?? 0.0;

}
