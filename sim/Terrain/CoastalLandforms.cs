namespace FullThrust.Sim;

public static class CoastalLandforms {

    /// <summary>A continuous-slope survey segment that stays between its two central samples.</summary>
    public static double Interpolate(double a, double b, double c, double d, double t) {

        double before = b - a;
        double span = c - b;
        double after = d - c;
        double low = before * span > 0.0 ? 2.0 * before * span / (before + span) : 0.0;
        double high = span * after > 0.0 ? 2.0 * span * after / (span + after) : 0.0;
        return b + t * (low + t * (3.0 * span - 2.0 * low - high + t * (low + high - 2.0 * span)));

    }

    public static double Elevation(Vector3d unit, double radius, double surveyed) {

        double influence = 1.0 - Ocean.Smooth(8.0, 28.0, Math.Abs(surveyed));
        if (influence <= 0.0) {

            return surveyed;

        }

        double px = unit.X * radius;
        double py = unit.Z * radius;
        double pz = -unit.Y * radius;
        if (!double.IsFinite(px) || !double.IsFinite(py) || !double.IsFinite(pz)) {

            return surveyed;

        }

        double region = Value(px * (1.0 / 6000.0), py * (1.0 / 6000.0), pz * (1.0 / 6000.0));
        double qx = px * (1.0 / 700.0) + region * 3.0;
        double qy = py * (1.0 / 700.0) - region * 2.0;
        double qz = pz * (1.0 / 700.0) + region;
        double coves = (Value(qx, qy, qz) - 0.5) * 2.4 + (Value(px * (1.0 / 180.0), py * (1.0 / 180.0), pz * (1.0 / 180.0)) - 0.5) * 0.6;
        double wetland = (1.0 - Ocean.Smooth(0.38, 0.62, region)) * (1.0 - Ocean.Smooth(0.55, 0.80, Math.Abs(unit.Z)));
        // Broad marsh depressions preserve survey drainage without carving closed noise-contour canals.
        double tidal = -wetland * 0.45 * (1.0 - Ocean.Smooth(0.5, 2.0, Math.Abs(surveyed)));
        double bars = Math.Max(0.0, Math.Sin(-surveyed * 1.4 + Value(px * (1.0 / 240.0), py * (1.0 / 240.0), pz * (1.0 / 240.0)) * 4.0));
        bars *= Ocean.Smooth(-9.0, -5.0, surveyed) * (1.0 - Ocean.Smooth(-1.0, 1.0, surveyed)) * 0.35;
        return surveyed + (coves + tidal + bars) * influence;

    }

    public static double Value(Vector3d p) => Value(p.X, p.Y, p.Z);

    public static double Value(double px, double py, double pz) {

        if (!double.IsFinite(px) || !double.IsFinite(py) || !double.IsFinite(pz)) {

            return 0.0;

        }

        int x = Cell(px);
        int y = Cell(py);
        int z = Cell(pz);
        double u = Ocean.Smooth(0.0, 1.0, px - Math.Floor(px));
        double v = Ocean.Smooth(0.0, 1.0, py - Math.Floor(py));
        double w = Ocean.Smooth(0.0, 1.0, pz - Math.Floor(pz));
        return Lerp(Lerp(Lerp(Hash(x, y, z), Hash(x + 1, y, z), u),
            Lerp(Hash(x, y + 1, z), Hash(x + 1, y + 1, z), u), v),
            Lerp(Lerp(Hash(x, y, z + 1), Hash(x + 1, y, z + 1), u),
            Lerp(Hash(x, y + 1, z + 1), Hash(x + 1, y + 1, z + 1), u), v), w);

    }

    private static int Cell(double value) {

        double floor = Math.Floor(value);
        return floor >= int.MinValue && floor <= int.MaxValue ? (int)floor : 0;

    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double Hash(int x, int y, int z) {

        unchecked {

            uint hash = (uint)x * 1597334677u ^ (uint)y * 3812015801u ^ (uint)z * 2798796415u;
            hash = (hash ^ (hash >> 16)) * 2246822519u;
            hash = (hash ^ (hash >> 13)) * 3266489917u;
            hash ^= hash >> 16;
            return (hash & 16777215u) / 16777216.0;

        }

    }

}
