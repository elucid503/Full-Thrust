namespace FullThrust.Sim;

/// <summary>A bounded linear gravity-wave response to exhaust pressure, with transported foam.</summary>
public sealed class SurfaceResponse {

    public const int Size = 65;
    public const double Spacing = 2.0;
    public const double HalfExtent = (Size - 1) * Spacing * 0.5;
    public const double StepSeconds = 1.0 / 60.0;
    private const double LayerDepth = 2.0;

    private readonly double[] _height = new double[Size * Size];
    private readonly double[] _x = new double[Size * Size];
    private readonly double[] _z = new double[Size * Size];
    private readonly double[] _pressure = new double[Size * Size];
    private readonly double[] _foam = new double[Size * Size];
    private readonly double[] _nextFoam = new double[Size * Size];
    private readonly double[] _damping = new double[Size * Size];
    private readonly double[] _absorption = new double[Size * Size];
    private static readonly double FoamDecay = Math.Exp(-StepSeconds / 4.0);
    private readonly double[] _wet = new double[Size * Size];

    public double DisplacementAt(double x, double z) => Sample(_height, (x + HalfExtent) / Spacing, (z + HalfExtent) / Spacing);

    public double Height(int x, int z) => _height[z * Size + x];
    public double Foam(int x, int z) => _foam[z * Size + x];
    public double VelocityX(int x, int z) => _x[z * Size + x];
    public double VelocityZ(int x, int z) => _z[z * Size + x];
    public double PeakHeight { get; private set; }

    public SurfaceResponse() {

        Array.Fill(_wet, 1.0);
        for (int z = 0; z < Size; z++) {

            for (int x = 0; x < Size; x++) {

                int i = z * Size + x;
                _damping[i] = Math.Exp(-StepSeconds * (0.16 + Sponge(x, z)));
                _absorption[i] = Math.Exp(-StepSeconds * Sponge(x, z));

            }

        }

    }

    public void SetWet(int x, int z, bool wet) => _wet[z * Size + x] = wet ? 1.0 : 0.0;
    public void ClearForcing() => Array.Clear(_pressure);

    public void Clear() {

        Array.Clear(_height);
        Array.Clear(_x);
        Array.Clear(_z);
        Array.Clear(_foam);
        Array.Clear(_pressure);
        PeakHeight = 0.0;

    }

    public void Press(double x, double z, double radius, double pressurePascals) {

        if (!double.IsFinite(x + z + radius + pressurePascals) || radius <= 0.0 || pressurePascals <= 0.0) { return; }
        double hydrostaticHead = pressurePascals / (1000.0 * 9.81);
        double head = 1.5 * hydrostaticHead / (1.5 + hydrostaticHead);
        double width = Math.Max(radius, Spacing);
        int left = Math.Clamp((int)((x - width * 3.0 + HalfExtent) / Spacing), 1, Size - 2);
        int right = Math.Clamp((int)((x + width * 3.0 + HalfExtent) / Spacing), 1, Size - 2);
        int back = Math.Clamp((int)((z - width * 3.0 + HalfExtent) / Spacing), 1, Size - 2);
        int front = Math.Clamp((int)((z + width * 3.0 + HalfExtent) / Spacing), 1, Size - 2);
        for (int row = back; row <= front; row++) {

            for (int column = left; column <= right; column++) {

                double dx = column * Spacing - HalfExtent - x;
                double dz = row * Spacing - HalfExtent - z;
                int i = row * Size + column;
                _pressure[i] += head * Math.Exp(-(dx * dx + dz * dz) / (width * width)) * _wet[i];

            }

        }

    }

    public void Deposit(double x, double z, double amount) {

        if (!double.IsFinite(x + z + amount) || amount <= 0.0) { return; }

        int column = Math.Clamp((int)Math.Round((x + HalfExtent) / Spacing), 1, Size - 2);
        int row = Math.Clamp((int)Math.Round((z + HalfExtent) / Spacing), 1, Size - 2);
        for (int dz = -1; dz <= 1; dz++) {

            for (int dx = -1; dx <= 1; dx++) {

                int i = (row + dz) * Size + column + dx;
                double weight = (dx == 0 ? 0.5 : 0.25) * (dz == 0 ? 0.5 : 0.25);
                double deposit = Math.Min(Math.Max(amount, 0.0), 1.0) * weight * _wet[i];
                _foam[i] = Math.Min(1.0, _foam[i] + deposit);
                _x[i] += dx * deposit * 0.2;
                _z[i] += dz * deposit * 0.2;

            }

        }

    }

    public void Step() {

        // Staggered face velocities avoid the checkerboard mode of centred collocated differences.
        for (int z = 1; z < Size - 1; z++) {

            for (int x = 1; x < Size - 1; x++) {

                int i = z * Size + x;
                double head = _height[i] + _pressure[i];
                double damping = _damping[i];
                _x[i] = (_x[i] - 9.81 * StepSeconds / Spacing * (_height[i + 1] + _pressure[i + 1] - head))
                    * damping * _wet[i] * _wet[i + 1];
                _z[i] = (_z[i] - 9.81 * StepSeconds / Spacing * (_height[i + Size] + _pressure[i + Size] - head))
                    * damping * _wet[i] * _wet[i + Size];

            }

        }
        PeakHeight = 0.0;
        for (int z = 1; z < Size - 1; z++) {

            for (int x = 1; x < Size - 1; x++) {

                int i = z * Size + x;
                double divergence = (_x[i] - _x[i - 1] + _z[i] - _z[i - Size]) / Spacing;
                _height[i] = (_height[i] - LayerDepth * StepSeconds * divergence)
                    * _absorption[i] * _wet[i];
                PeakHeight = Math.Max(PeakHeight, Math.Abs(_height[i]));
                double vx = (_x[i] + _x[i - 1]) * 0.5;
                double vz = (_z[i] + _z[i - Size]) * 0.5;
                double foam = Sample(_foam, x - vx * StepSeconds / Spacing, z - vz * StepSeconds / Spacing);
                double breaking = Math.Max(0.0, -divergence - 0.08) * 1.5;
                double aeration = _pressure[i] * Math.Sqrt(vx * vx + vz * vz) * 0.12;
                _nextFoam[i] = Math.Clamp(foam * FoamDecay
                    + (breaking + aeration) * StepSeconds, 0.0, 1.0) * _wet[i];

            }

        }
        Array.Copy(_nextFoam, _foam, _foam.Length);

    }

    private static double Sponge(int x, int z) {

        double edge = Math.Min(Math.Min(x, z), Math.Min(Size - 1 - x, Size - 1 - z));
        double fade = Math.Max(0.0, 1.0 - edge / 8.0);
        return fade * fade * 5.0;

    }

    private static double Sample(double[] data, double x, double z) {

        x = Math.Clamp(x, 0.0, Size - 1.001);
        z = Math.Clamp(z, 0.0, Size - 1.001);
        int ix = (int)x;
        int iz = (int)z;
        double fx = x - ix;
        double fz = z - iz;
        int i = iz * Size + ix;
        return (data[i] * (1.0 - fx) + data[i + 1] * fx) * (1.0 - fz)
            + (data[i + Size] * (1.0 - fx) + data[i + Size + 1] * fx) * fz;

    }

}
