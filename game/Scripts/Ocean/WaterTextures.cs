using System;
using System.Numerics;

using Godot;

namespace FullThrust.Game;

/// <summary>Tileable detail baked once from a wind-wave spectrum: slopes of the short waves the swell
/// cannot carry, the order in which they break, their height, and a bubble pattern for the foam.</summary>
internal static class WaterTextures {

    private const int Size = 256;

    // The tile carries two to thirty-two waves across: four octaves, 8 texels to the shortest.
    private const double LongestWaves = 2.0;
    private const double ShortestWaves = 32.0;

    public static Texture2D Waves { get; private set; }
    public static Texture2D Foam { get; private set; }

    /// <summary>Mean square slope, both axes together, in the texture's own encoded units.</summary>
    public static double SlopeVariance { get; private set; }

    public static void Bake() {

        if (Waves != null) {

            return;

        }

        int count = Size * Size;
        Complex[] height = new Complex[count];
        Complex[] slopeX = new Complex[count];
        Complex[] slopeZ = new Complex[count];
        Complex[] stretchX = new Complex[count];
        Complex[] stretchZ = new Complex[count];
        Complex[] shear = new Complex[count];
        Random random = new Random(1204);

        for (int row = 0; row < Size; row++) {

            for (int column = 0; column < Size; column++) {

                double kx = Math.Tau * (column < Size / 2 ? column : column - Size);
                double kz = Math.Tau * (row < Size / 2 ? row : row - Size);
                double k = Math.Sqrt(kx * kx + kz * kz);
                double across = k / Math.Tau;
                double window = FullThrust.Sim.Ocean.Smooth(LongestWaves * 0.7, LongestWaves * 1.4, across) * (1.0 - FullThrust.Sim.Ocean.Smooth(ShortestWaves * 0.7, ShortestWaves, across));

                if (window <= 0.0) {

                    continue;

                }

                // Saturation range, equal slope in every octave; spread mostly downwind with some cross-chop.
                double spreading = 0.15 + 0.85 * Math.Pow(Math.Max(kx / k, 0.0), 4.0);
                double amplitude = Math.Sqrt(window * spreading * 0.5) / (k * k);
                Complex wave = new Complex(Gaussian(random), Gaussian(random)) * amplitude;
                int index = row * Size + column;

                height[index] = wave;
                slopeX[index] = Complex.ImaginaryOne * kx * wave;
                slopeZ[index] = Complex.ImaginaryOne * kz * wave;
                stretchX[index] = kx * kx / k * wave;
                stretchZ[index] = kz * kz / k * wave;
                shear[index] = kx * kz / k * wave;

            }

        }

        foreach (Complex[] field in new[] { height, slopeX, slopeZ, stretchX, stretchZ, shear }) {

            Inverse(field);

        }

        double range = 0.0;
        double squares = 0.0;
        double compression = 0.0;
        double low = double.MaxValue;
        double high = double.MinValue;

        for (int index = 0; index < count; index++) {

            range = Math.Max(range, Math.Max(Math.Abs(slopeX[index].Real), Math.Abs(slopeZ[index].Real)));
            squares += slopeX[index].Real * slopeX[index].Real + slopeZ[index].Real * slopeZ[index].Real;
            compression = Math.Max(compression, stretchX[index].Real + stretchZ[index].Real);
            low = Math.Min(low, height[index].Real);
            high = Math.Max(high, height[index].Real);

        }

        // Choppy displacement gathers the short waves into crests; the most folded break first. Only
        // the order matters, so the Jacobian is stored as a rank that the wind thresholds directly.
        double chop = 0.9 / compression;
        double[] folds = new double[count];
        int[] order = new int[count];

        for (int index = 0; index < count; index++) {

            double stretchedX = 1.0 - chop * stretchX[index].Real;
            double stretchedZ = 1.0 - chop * stretchZ[index].Real;
            double sheared = chop * shear[index].Real;
            folds[index] = stretchedX * stretchedZ - sheared * sheared;
            order[index] = index;

        }

        Array.Sort((double[])folds.Clone(), order);

        float[] rank = new float[count];

        for (int position = 0; position < count; position++) {

            rank[order[position]] = 1.0f - position / (float)(count - 1);

        }

        using Image waves = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);

        for (int index = 0; index < count; index++) {

            waves.SetPixel(index % Size, index / Size, new Color(
                (float)(0.5 + 0.5 * slopeX[index].Real / range),
                (float)(0.5 + 0.5 * slopeZ[index].Real / range),
                rank[index],
                (float)((height[index].Real - low) / (high - low))));

        }

        waves.GenerateMipmaps();
        SlopeVariance = squares / count / (range * range);
        Waves = ImageTexture.CreateFromImage(waves);
        Foam = BakeFoam();

    }

    // Bubble rafts: the edges of a cellular field make the lace, a smooth field makes the patches.
    private static Texture2D BakeFoam() {

        FastNoiseLite cells = new FastNoiseLite {

            Seed = 1204,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular,
            CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance2Sub,
            Frequency = 0.045f,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 3,

        };

        FastNoiseLite patches = new FastNoiseLite {

            Seed = 77,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 0.012f,
            FractalOctaves = 4,

        };

        using Image lace = cells.GetSeamlessImage(Size, Size);
        using Image drifts = patches.GetSeamlessImage(Size, Size);
        using Image foam = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);

        for (int y = 0; y < Size; y++) {

            for (int x = 0; x < Size; x++) {

                foam.SetPixel(x, y, new Color(1.0f - lace.GetPixel(x, y).R, drifts.GetPixel(x, y).R, 0.0f, 1.0f));

            }

        }

        foam.GenerateMipmaps();

        return ImageTexture.CreateFromImage(foam);

    }

    private static double Gaussian(Random random) {

        double radius = Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble()));
        return radius * Math.Cos(Math.Tau * random.NextDouble());

    }

    // Inverse transform in place, rows then columns; only the real part is read back.
    private static void Inverse(Complex[] field) {

        Complex[] line = new Complex[Size];

        for (int row = 0; row < Size; row++) {

            Array.Copy(field, row * Size, line, 0, Size);
            Transform(line);
            Array.Copy(line, 0, field, row * Size, Size);

        }

        for (int column = 0; column < Size; column++) {

            for (int row = 0; row < Size; row++) {

                line[row] = field[row * Size + column];

            }

            Transform(line);

            for (int row = 0; row < Size; row++) {

                field[row * Size + column] = line[row];

            }

        }

    }

    // Radix-2 Cooley-Tukey with a positive exponent, which is the inverse up to scale.
    private static void Transform(Complex[] data) {

        int n = data.Length;

        for (int index = 1, reversed = 0; index < n; index++) {

            int bit = n >> 1;

            for (; (reversed & bit) != 0; bit >>= 1) {

                reversed ^= bit;

            }

            reversed ^= bit;

            if (index < reversed) {

                (data[index], data[reversed]) = (data[reversed], data[index]);

            }

        }

        for (int length = 2; length <= n; length <<= 1) {

            Complex step = Complex.FromPolarCoordinates(1.0, Math.Tau / length);

            for (int start = 0; start < n; start += length) {

                Complex twiddle = Complex.One;

                for (int offset = 0; offset < length / 2; offset++) {

                    Complex even = data[start + offset];
                    Complex odd = data[start + offset + length / 2] * twiddle;
                    data[start + offset] = even + odd;
                    data[start + offset + length / 2] = even - odd;
                    twiddle *= step;

                }

            }

        }

    }

}
