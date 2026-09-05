namespace FullThrust.Sim;

/// <summary>Gradient noise in three dimensions. Stateless and hash-driven rather than seeded from a
/// permutation table, so the renderer's worker threads and the physics land on identical numbers.</summary>
public static class Noise {

    // A permutation of 0..255, laid down twice so an index and its successor never wrap. Built from
    // a fixed multiplier rather than from Random: the terrain has to be the same field on every
    // machine and in every runtime, because the renderer and the physics both read it.
    private static readonly byte[] Permutation = BuildPermutation();

    private static byte[] BuildPermutation() {

        byte[] order = new byte[512];

        for (int index = 0; index < 256; index++) {

            order[index] = (byte)index;

        }

        uint state = 0x9E3779B9u;

        for (int index = 255; index > 0; index--) {

            state = state * 1664525u + 1013904223u;

            int swap = (int)(state >> 16) % (index + 1);

            (order[index], order[swap]) = (order[swap], order[index]);

        }

        for (int index = 0; index < 256; index++) {

            order[index + 256] = order[index];

        }

        return order;

    }

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
    // The octave count is fractional. A caller that drops an octave as its sample spacing coarsens
    // has to fade the last one out rather than switch it off, or the ground steps every time the
    // renderer changes level.
    public static double Ridged(double x, double y, double z, double octaves, double gain) {

        double sum = 0.0;
        double amplitude = 1.0;
        double frequency = 1.0;
        double weight = 1.0;
        double total = 0.0;

        for (int octave = 0; octave < (int)Math.Ceiling(octaves); octave++) {

            double share = Math.Min(octaves - octave, 1.0);

            double signal = 1.0 - Math.Abs(Value(x * frequency, y * frequency, z * frequency));

            signal *= signal;

            // Each octave is gated by the one above it, so ridges only branch where a ridge already
            // runs. The gate has to scale the octave's contribution rather than offset it: a gated
            // octave that still adds a constant terraces the whole field where the gate closes.
            signal *= weight;

            weight = Math.Clamp(signal * 2.0, 0.0, 1.0);

            sum += signal * amplitude * share;
            total += amplitude * share;

            amplitude *= gain;
            frequency *= 2.0;

        }

        return total > 0.0 ? sum / total : 0.0;

    }

    /// <summary>Plain fractal sum, for ground that rolls rather than stands up.</summary>
    public static double Fractal(double x, double y, double z, double octaves, double gain) {

        double sum = 0.0;
        double amplitude = 1.0;
        double frequency = 1.0;

        for (int octave = 0; octave < (int)Math.Ceiling(octaves); octave++) {

            sum += Value(x * frequency, y * frequency, z * frequency) * amplitude * Math.Min(octaves - octave, 1.0);

            amplitude *= gain;
            frequency *= 2.0;

        }

        return sum;

    }

    private static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    // The twelve cube-edge gradients, chosen by four bits and evaluated as sums rather than read
    // out of a table: this runs a couple of hundred million times over a flight and the bounds
    // check on the table was a measurable part of it.
    private static double Dot(int x, int y, int z, double dx, double dy, double dz) {

        int hash = Permutation[Permutation[Permutation[x & 255] + (y & 255)] + (z & 255)] & 15;

        double u = hash < 8 ? dx : dy;
        double v = hash < 4 ? dy : hash == 12 || hash == 14 ? dx : dz;

        return ((hash & 1) == 0 ? u : -u) + ((hash & 2) == 0 ? v : -v);

    }

}
