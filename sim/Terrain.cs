using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace FullThrust.Sim;

/// <summary>The ground, as one function of direction: a measured elevation grid with fractal detail
/// standing on it. The renderer builds every patch of mesh by calling this, and the physics decides
/// contact by calling this, so a pad cannot hover over the terrain it is bolted to and a vehicle
/// cannot sink into a hillside that is drawn somewhere else.</summary>
public sealed class Terrain {

    // Terra is one fifth Earth scale. Scaling the survey vertically as well as wrapping it onto a
    // fifth-scale sphere preserves real-world slopes instead of making every landform five times
    // steeper than the geography it came from.
    public const double ElevationScale = 0.2;

    /// <summary>A level circle worked into the ground, so a launch complex stands on flat ground
    /// whatever the survey says was there.</summary>
    public sealed class Plateau {

        /// <summary>Body-fixed unit direction of the centre.</summary>
        public Vector3d Centre { get; init; }

        public double Height { get; init; }

        /// <summary>Ground distance out to which the circle is dead level, and the distance beyond
        /// that at which the natural ground has fully returned.</summary>
        public double InnerRadius { get; init; }
        public double OuterRadius { get; init; }

    }

    // Longest detail wavelength, and how many times it is halved. The finest octave lands near
    // fifteen metres, which is under the closest the mesh ever samples. There is no point starting
    // any longer than this: the measured grid already carries everything above a kilometre, and
    // every octave above that is one the renderer pays for on every vertex it builds.
    private const double DetailWavelength = 6_000.0;
    private const int DetailOctaves = 12;

    // Ridges are a shape, not a texture. Below a hundred metres the rolling sum and the detail
    // material carry the surface and a ninth octave of ridge would only cost.
    private const int RidgeOctaves = 9;

    // Amplitude falls by this each octave. A half would be a fractal dimension of two - glassy,
    // nothing like ground. Real relief sits near 0.58 and keeps a metre of bump at fifteen metres.
    private const double DetailGain = 0.58;

    private const double RollingAmplitude = 6.8;
    private const double RidgeAmplitude = 124.0;

    // Slope, in metres per metre off the measured grid, over which ground goes from flat to broken.
    // A 741 m post smooths real relief badly, so the survey reads far flatter than the ground is and
    // the band has to start low or nothing outside a mountain range ever stands up at all.
    private const double SmoothSlope = 0.003;
    private const double BrokenSlope = 0.055;

    private const uint FileMagic = 0x46485446;

    private readonly ushort[] _counts;

    private readonly int _width;
    private readonly int _height;

    private readonly double _step;
    private readonly double _floor;

    private readonly double _radius;

    private Plateau[] _plateaus = Array.Empty<Plateau>();

    private Terrain(ushort[] counts, int width, int height, double step, double floor, double radius) {

        _counts = counts;
        _width = width;
        _height = height;
        _step = step;
        _floor = floor;
        _radius = radius;

    }

    /// <summary>Lowest and highest the measured grid goes, metres. The renderer sizes its patch
    /// bounding volumes off these rather than guessing.</summary>
    public double Floor => _floor;

    public double Ceiling => _floor + 65535.0 * _step + RidgeAmplitude + RollingAmplitude;

    public IReadOnlyList<Plateau> Plateaus => Array.AsReadOnly(Volatile.Read(ref _plateaus));

    public void Add(Plateau plateau) {

        ArgumentNullException.ThrowIfNull(plateau);

        Plateau[] previous;
        Plateau[] updated;

        do {

            previous = Volatile.Read(ref _plateaus);
            updated = new Plateau[previous.Length + 1];
            Array.Copy(previous, updated, previous.Length);
            updated[^1] = plateau;

        } while (Interlocked.CompareExchange(ref _plateaus, updated, previous) != previous);

    }

    /// <summary>Reads the packed grid written by tools/planet_maps.py.</summary>
    public static Terrain Load(Stream stream, double radius) {

        byte[] header = new byte[32];

        stream.ReadExactly(header);

        if (BitConverter.ToUInt32(header, 0) != FileMagic || BitConverter.ToUInt32(header, 4) != 1u) {

            throw new InvalidDataException("not a Full-Thrust heightfield");

        }

        int width = (int)BitConverter.ToUInt32(header, 8);
        int height = (int)BitConverter.ToUInt32(header, 12);

        double step = BitConverter.ToDouble(header, 16) * ElevationScale;
        double floor = BitConverter.ToDouble(header, 24) * ElevationScale;

        byte[] planes = new byte[width * height * 2];

        using (GZipStream unpacked = new GZipStream(stream, CompressionMode.Decompress)) {

            unpacked.ReadExactly(planes);

        }

        return new Terrain(Recombine(planes, width, height), width, height, step, floor, radius);

    }

    // The file carries the two bytes of each sample as separate planes, each row differenced against
    // itself, because deflate finds nothing in an interleaved 16-bit field.
    private static ushort[] Recombine(byte[] planes, int width, int height) {

        ushort[] counts = new ushort[width * height];

        int low = width * height;

        for (int row = 0; row < height; row++) {

            int start = row * width;

            byte high = 0;
            byte rest = 0;

            for (int column = 0; column < width; column++) {

                high += planes[start + column];
                rest += planes[low + start + column];

                counts[start + column] = (ushort)((high << 8) | rest);

            }

        }

        return counts;

    }

    /// <summary>Height of the ground above the datum at a body-fixed direction, metres. Negative
    /// under the sea, where the datum is the water's own surface.</summary>
    public double Elevation(Vector3d direction) => Elevation(direction, 0.0);

    /// <summary>Ground elevation with every detail octave finer than a sample spacing left off.
    /// A mesh built at kilometre spacing cannot carry a fifteen-metre octave and pays for it on
    /// every vertex it places; pass no spacing, as the physics does, to read the whole field.</summary>
    public double Elevation(Vector3d direction, double spacing) {

        double length = direction.Length;

        if (length <= 0.0) {

            return 0.0;

        }

        Vector3d unit = direction / length;

        double latitude = Math.Asin(Math.Clamp(unit.Z, -1.0, 1.0));
        double longitude = Math.Atan2(unit.Y, unit.X);

        double u = longitude / (Math.PI * 2.0) + 0.5;
        double v = 0.5 - latitude / Math.PI;

        double measured = Sample(u, v);

        double natural = PreserveCoast(measured, measured + Detail(unit, Ruggedness(u, v, latitude, measured), spacing));

        return Level(unit, natural);

    }

    /// <summary>Distance from the body's centre to the surface a vehicle can touch: the ground where
    /// it stands proud of the datum, and the water's surface where it does not.</summary>
    public double SurfaceRadius(Vector3d direction) => _radius + Math.Max(Elevation(direction), 0.0);

    /// <summary>Ground elevation with no plateau worked into it, which is what a plateau's own
    /// height is chosen against.</summary>
    public double NaturalElevation(Vector3d direction) {

        Vector3d unit = direction.Normalized;

        double latitude = Math.Asin(Math.Clamp(unit.Z, -1.0, 1.0));
        double longitude = Math.Atan2(unit.Y, unit.X);

        double u = longitude / (Math.PI * 2.0) + 0.5;
        double v = 0.5 - latitude / Math.PI;

        double measured = Sample(u, v);

        return PreserveCoast(measured, measured + Detail(unit, Ruggedness(u, v, latitude, measured), 0.0));

    }

    private double Level(Vector3d unit, double natural) {

        double height = natural;

        foreach (Plateau plateau in Volatile.Read(ref _plateaus)) {

            double angle = Math.Acos(Math.Clamp(Vector3d.Dot(unit, plateau.Centre), -1.0, 1.0));

            double distance = angle * _radius;

            if (distance >= plateau.OuterRadius) {

                continue;

            }

            double blend = Smoothstep(plateau.OuterRadius, plateau.InnerRadius, distance);

            height = height + (plateau.Height - height) * blend;

        }

        return height;

    }

    // Ruggedness off the measured grid alone: fractal detail stands up into ridges where the survey
    // already says there is relief, and stays a gentle roll where it says there is none. Taken over
    // two posts rather than the cell the sample landed in, so the mask is smooth and does not
    // terrace the detail at grid boundaries.
    private double Ruggedness(double u, double v, double latitude, double measured) {

        double du = 2.0 / _width;
        double dv = 2.0 / _height;

        double east = Sample(u + du, v);
        double north = Sample(u, v - dv);

        double metresEast = Math.PI * 2.0 * _radius * Math.Max(Math.Cos(latitude), 0.05) * du;
        double metresNorth = Math.PI * _radius * dv;

        double slope = Math.Sqrt(Square((east - measured) / metresEast) + Square((north - measured) / metresNorth));

        return Smoothstep(SmoothSlope, BrokenSlope, slope);

    }

    private double Detail(Vector3d unit, double ruggedness, double spacing) {

        // Nyquist on the detail spectrum, with two octaves of headroom: an octave well under the
        // sample spacing is noise in the mesh rather than shape in it, and on a patch an orbital view
        // is assembled from that is all but two or three of them. The headroom is what keeps the last
        // octave to arrive from arriving at a level anyone is looking closely at. Fractional, so the
        // ground changes continuously as the tree splits rather than growing relief in one step.
        double resolved = spacing > 0.0
            ? Math.Log2(DetailWavelength * 4.0 / Math.Max(spacing, 1.0))
            : DetailOctaves;

        Vector3d at = unit * (_radius / DetailWavelength);

        double rollingOctaves = Math.Clamp(resolved, 0.0, DetailOctaves);

        double rolling = rollingOctaves > 0.0
            ? Noise.Fractal(at.X, at.Y, at.Z, rollingOctaves, DetailGain) * RollingAmplitude
            : 0.0;

        double ridgeOctaves = Math.Clamp(resolved, 0.0, RidgeOctaves);

        if (ruggedness <= 0.0 || ridgeOctaves <= 0.0) {

            return rolling;

        }

        // Offset so the ridge field is not the same field as the rolling one read twice. Ridges only
        // ever stand up out of the survey's own relief; nothing here digs into it.
        double ridged = Noise.Ridged(at.X + 91.7, at.Y - 43.1, at.Z + 17.9, ridgeOctaves, DetailGain);

        // The ridged sum is normalised by its own octaves, so it does not thin as they are dropped.
        // Its amplitude is faded over the last one instead.
        return rolling + ridged * RidgeAmplitude * ruggedness * Math.Min(ridgeOctaves, 1.0);

    }

    private double Sample(double u, double v) {

        double x = (u - Math.Floor(u)) * _width - 0.5;
        double y = Math.Clamp(v, 0.0, 1.0) * _height - 0.5;

        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);

        double fx = x - x0;
        double fy = y - y0;

        int left = ((x0 % _width) + _width) % _width;
        int right = (left + 1) % _width;

        int top = Math.Clamp(y0, 0, _height - 1);
        int bottom = Math.Clamp(y0 + 1, 0, _height - 1);

        double upper = Count(left, top) + (Count(right, top) - Count(left, top)) * fx;
        double lower = Count(left, bottom) + (Count(right, bottom) - Count(left, bottom)) * fx;

        return _floor + (upper + (lower - upper) * fy) * _step;

    }

    private double Count(int x, int y) => _counts[y * _width + x];

    // Detail may roughen either side of a shoreline, but it cannot turn a surveyed land texel into
    // sea or raise surveyed seabed through the datum.
    private static double PreserveCoast(double measured, double detailed) => measured >= 0.0
        ? Math.Max(detailed, 0.01)
        : Math.Min(detailed, -0.01);

    private static double Square(double value) => value * value;

    private static double Smoothstep(double edge0, double edge1, double value) {

        double t = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);

        return t * t * (3.0 - 2.0 * t);

    }

}
