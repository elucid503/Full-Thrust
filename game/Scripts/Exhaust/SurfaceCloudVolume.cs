using System;
using System.Threading.Tasks;

using Noise = FullThrust.Sim.Noise;

using Godot;

namespace FullThrust.Game;

public sealed class SurfaceCloudVolume : IDisposable {

    private const int Width = 40, Height = 24, Depth = 40;
    private readonly float[] _density = new float[Width * Height * Depth];
    private readonly float[] _wet = new float[Width * Height * Depth];
    private readonly float[] _shadow = new float[Width * Height * Depth];
    private readonly float[] _x = new float[Width], _y = new float[Height], _z = new float[Depth];
    private readonly byte[][] _pixels = new byte[Depth][];
    private readonly Godot.Collections.Array<Image> _slices = new();
    public ImageTexture3D Texture { get; } = new();
    public Vector3 Low { get; private set; }
    public Vector3 High { get; private set; }
    private bool _created;
    private Task _build;
    private Vector3 _pendingLow, _pendingHigh;
    public bool Ready => _created;
    public bool Busy => _build != null;
    public float BuildMilliseconds { get; private set; }

    public SurfaceCloudVolume() {

        for (int z = 0; z < Depth; z++) {

            _pixels[z] = new byte[Width * Height * 8];
            _slices.Add(Image.CreateFromData(Width, Height, false, Image.Format.Rgbah, _pixels[z]));

        }

    }

    private static void Kernel(float[] weights, float start, float step, float centre, float radius) {

        for (int i = 0; i < weights.Length; i++) {

            float d = (start + i * step - centre) / radius;
            float t = Math.Max(1.0f - d * d, 0.0f);
            weights[i] = t * t * t;

        }

    }

    private static void HalfValue(byte[] bytes, int offset, float value) {

        ushort bits = BitConverter.HalfToUInt16Bits((Half)value);
        bytes[offset] = (byte)bits;
        bytes[offset + 1] = (byte)(bits >> 8);

    }

    public void Request(Vector4[] centres, Vector4[] states, Vector3[] shapes, int count, Vector3 low, Vector3 high, Vector3 sun, Vector3 noiseOrigin) {

        if (_build != null) { return; }
        Vector4[] positions = new Vector4[count], values = new Vector4[count];
        Vector3[] aspects = new Vector3[count];
        Array.Copy(centres, positions, count);
        Array.Copy(states, values, count);
        Array.Copy(shapes, aspects, count);
        _pendingLow = low;
        _pendingHigh = high;
        _build = Task.Run(() => {

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            Bake(positions, values, aspects, count, low, high, sun, noiseOrigin);
            BuildMilliseconds = (float)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        });

    }

    private Vector3 Sample(Vector3 cell) {

        cell = cell.Clamp(Vector3.Zero, new Vector3(Width - 1.001f, Height - 1.001f, Depth - 1.001f));
        int x = (int)cell.X, y = (int)cell.Y, z = (int)cell.Z;
        Vector3 f = cell - new Vector3(x, y, z);
        Vector3 result = Vector3.Zero;
        for (int k = 0; k < 8; k++) {

            int dx = k & 1, dy = (k >> 1) & 1, dz = (k >> 2) & 1;
            float weight = (dx == 0 ? 1.0f - f.X : f.X) * (dy == 0 ? 1.0f - f.Y : f.Y) * (dz == 0 ? 1.0f - f.Z : f.Z);
            int at = ((z + dz) * Height + y + dy) * Width + x + dx;
            result += new Vector3(_density[at], _wet[at], _shadow[at]) * weight;

        }
        return result;

    }

    private void Bake(Vector4[] centres, Vector4[] states, Vector3[] shapes, int count, Vector3 low, Vector3 high, Vector3 sun, Vector3 noiseOrigin) {

        for (int i = 0; i < count; i++) {

            Vector4 centre = centres[i];
            float depth = 0.0f;
            for (int j = 0; j < count; j++) {

                if (i == j) { continue; }
                Vector4 other = centres[j];
                Vector3 offset = new Vector3(other.X - centre.X, other.Y - centre.Y, other.Z - centre.Z) - sun * centre.W * 0.55f;
                float along = offset.Dot(sun);
                float across = offset.LengthSquared() - along * along;
                float support = other.W * other.W;
                if (along <= 0.0f || across >= support) { continue; }
                depth += 1.333f * Mathf.Sqrt(support - across) * states[j].X * (1.0f - across / support);

            }
            states[i].W = Math.Min(depth, 8.0f);

        }
        Array.Clear(_density);
        Array.Clear(_wet);
        Array.Clear(_shadow);
        Vector3 step = (high - low) / new Vector3(Width - 1, Height - 1, Depth - 1);
        float voxelLength = step.Length();
        for (int i = 0; i < count; i++) {

            Vector4 centre = centres[i], state = states[i];
            if (state.X < 0.00001f) { continue; }
            float radius = centre.W;
            Kernel(_x, low.X, step.X, centre.X, radius * shapes[i].X);
            Kernel(_y, low.Y, step.Y, centre.Y, radius * shapes[i].Y * Mathf.Lerp(0.75f, 1.0f, state.Y));
            Kernel(_z, low.Z, step.Z, centre.Z, radius * shapes[i].Z);
            for (int z = 0; z < Depth; z++) {

                if (_z[z] == 0.0f) { continue; }
                for (int y = 0; y < Height; y++) {

                    float row = _z[z] * _y[y] * state.X;
                    if (row == 0.0f) { continue; }
                    int at = (z * Height + y) * Width;
                    for (int x = 0; x < Width; x++) {

                        float mass = row * _x[x];
                        _density[at + x] += mass;
                        _wet[at + x] += mass * state.Y;
                        _shadow[at + x] += mass * state.W;

                    }

                }

            }

        }
        for (int z = 0; z < Depth; z++) {

            byte[] pixels = _pixels[z];
            for (int i = 0; i < Width * Height; i++) {

                int at = z * Width * Height + i;
                Vector3 field = Vector3.Zero;
                float direct = 0.0f, scattered = 0.0f;
                if (_density[at] > 0.00001f) {

                    Vector3 cell = new(i % Width, i / Width, z);
                    Vector3 p = low + cell * step;
                    Vector3 q = p * 0.045f + noiseOrigin;
                    float broad = (float)Noise.Value(q.X, q.Y, q.Z);
                    float fine = (float)Noise.Value(q.Z * 2.371 + 19.3, q.X * 2.371 + 4.7, q.Y * 2.371 + 31.1);
                    Vector3 warp = new Vector3(broad, fine, broad - fine) * Math.Clamp(p.Y * 0.15f, 0.0f, 1.5f);
                    field = Sample(cell + warp / step);
                    float mass = field.X;
                    float detail = Math.Max(0.15f, 1.0f + broad * 1.15f + fine * 0.55f);
                    field *= detail;
                    float shadow = mass > 0.00001f ? field.Z / Math.Max(field.X, 0.00001f) * 0.6f : 0.0f;
                    shadow += field.X * voxelLength * 0.3f;
                    direct = field.X * MathF.Exp(-shadow);
                    scattered = field.X * (0.56f * MathF.Exp(-shadow * 0.2f) + 0.31f * MathF.Exp(-shadow * 0.04f));

                }
                HalfValue(pixels, i * 8, field.X);
                HalfValue(pixels, i * 8 + 2, field.Y);
                HalfValue(pixels, i * 8 + 4, direct);
                HalfValue(pixels, i * 8 + 6, scattered);

            }

        }

    }

    public bool Complete() {

        if (_build == null || !_build.IsCompleted) { return false; }
        _build.GetAwaiter().GetResult();
        _build = null;
        Low = _pendingLow;
        High = _pendingHigh;
        for (int z = 0; z < Depth; z++) {

            _slices[z].SetData(Width, Height, false, Image.Format.Rgbah, _pixels[z]);

        }
        if (_created) { Texture.Update(_slices); }
        else {

            Error error = Texture.Create(Image.Format.Rgbah, Width, Height, Depth, false, _slices);
            if (error != Error.Ok) { throw new InvalidOperationException($"Surface cloud upload failed: {error}"); }
            _created = true;

        }
        return true;

    }

    public void Dispose() {

        Texture.Dispose();
        foreach (Image slice in _slices) { slice.Dispose(); }
        _slices.Clear();

    }

}
