namespace FullThrust.Sim;

/// <summary>Gradient noise in three dimensions. Stateless and hash-driven rather than seeded from a
/// permutation table, so the renderer's worker threads and the physics land on identical numbers.</summary>
public static class Noise {

    // Twelve gradients on the edges of a cube, the classic Perlin set.
    private static readonly double[] Gradients = {

        1.0, 1.0, 0.0, -1.0, 1.0, 0.0, 1.0, -1.0, 0.0, -1.0, -1.0, 0.0,
        1.0, 0.0, 1.0, -1.0, 0.0, 1.0, 1.0, 0.0, -1.0, -1.0, 0.0, -1.0,
        0.0, 1.0, 1.0, 0.0, -1.0, 1.0, 0.0, 1.0, -1.0, 0.0, -1.0, -1.0,

    };

    /// <summary>Perlin noise on the unit lattice, in roughly [-1, 1].</summary>
    public static double Value(double x, double y, double z) {

        int xi = (int)Math.Floor(x);
        int yi = (int)Math.Floor(y);
        int zi = (int)Math.Floor(z);

        double xf = x - xi;
        double yf = y - yi;
        double zf = z - zi;

        double u = Fade(xf);
        double v = Fade(yf);
        double w = Fade(zf);

        double x00 = Lerp(Dot(xi, yi, zi, xf, yf, zf), Dot(xi + 1, yi, zi, xf - 1.0, yf, zf), u);
        double x10 = Lerp(Dot(xi, yi + 1, zi, xf, yf - 1.0, zf), Dot(xi + 1, yi + 1, zi, xf - 1.0, yf - 1.0, zf), u);
        double x01 = Lerp(Dot(xi, yi, zi + 1, xf, yf, zf - 1.0), Dot(xi + 1, yi, zi + 1, xf - 1.0, yf, zf - 1.0), u);
        double x11 = Lerp(Dot(xi, yi + 1, zi + 1, xf, yf - 1.0, zf - 1.0), Dot(xi + 1, yi + 1, zi + 1, xf - 1.0, yf - 1.0, zf - 1.0), u);

        return Lerp(Lerp(x00, x10, v), Lerp(x01, x11, v), w) * 1.1547;

    }

    /// <summary>Ridged fractal sum, zero in the valleys and one along the crests. Ridged octaves
    /// stack into ranges where a plain sum would only roll.</summary>
    public static double Ridged(double x, double y, double z, int octaves, double gain) {

        double sum = 0.0;
        double amplitude = 1.0;
        double frequency = 1.0;
        double weight = 1.0;
        double total = 0.0;

        for (int octave = 0; octave < octaves; octave++) {

            double signal = 1.0 - Math.Abs(Value(x * frequency, y * frequency, z * frequency));

            signal *= signal;

            // Each octave is gated by the one above it, so ridges only branch where a ridge already
            // runs. The gate has to scale the octave's contribution rather than offset it: a gated
            // octave that still adds a constant terraces the whole field where the gate closes.
            signal *= weight;

            weight = Math.Clamp(signal * 2.0, 0.0, 1.0);

            sum += signal * amplitude;
            total += amplitude;

            amplitude *= gain;
            frequency *= 2.0;

        }

        return sum / total;

    }

    /// <summary>Plain fractal sum, for ground that rolls rather than stands up.</summary>
    public static double Fractal(double x, double y, double z, int octaves, double gain) {

        double sum = 0.0;
        double amplitude = 1.0;
        double frequency = 1.0;

        for (int octave = 0; octave < octaves; octave++) {

            sum += Value(x * frequency, y * frequency, z * frequency) * amplitude;

            amplitude *= gain;
            frequency *= 2.0;

        }

        return sum;

    }

    private static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double Dot(int x, int y, int z, double dx, double dy, double dz) {

        int index = (int)(Hash(x, y, z) % 12u) * 3;

        return Gradients[index] * dx + Gradients[index + 1] * dy + Gradients[index + 2] * dz;

    }

    private static uint Hash(int x, int y, int z) {

        uint h = (uint)(x * 0x27D4EB2D) ^ (uint)(y * 0x165667B1) ^ (uint)(z * 0x9E3779B1);

        h ^= h >> 15;
        h *= 0x2C1B3C6D;
        h ^= h >> 13;
        h *= 0x297A2D39;
        h ^= h >> 16;

        return h;

    }

}
