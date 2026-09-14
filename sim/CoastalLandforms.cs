namespace FullThrust.Sim;

public static class CoastalLandforms {

    public static double Elevation(Vector3d unit, double radius, double surveyed) {

        double influence = 1.0 - Smooth(8.0, 28.0, Math.Abs(surveyed));
        if (influence <= 0.0) {

            return surveyed;

        }

        Vector3d p = new(unit.X * radius, unit.Z * radius, -unit.Y * radius);
        double region = Value(p / 6000.0);
        Vector3d q = p / 700.0 + new Vector3d(region * 3.0, -region * 2.0, region);
        double coves = (Value(q) - 0.5) * 7.0 + (Value(p / 180.0) - 0.5) * 2.2;
        double wetland = (1.0 - Smooth(0.38, 0.62, region)) * (1.0 - Smooth(0.55, 0.80, Math.Abs(unit.Z)));
        double creek = 1.0 - Smooth(0.02, 0.10, Math.Abs(Value(p / 310.0 + new Vector3d(region * 2.0, 0.0, region)) - 0.5));
        double tidal = -creek * wetland * 2.8 * (1.0 - Smooth(3.0, 9.0, Math.Abs(surveyed)));
        double bars = Math.Max(0.0, Math.Sin(-surveyed * 1.4 + Value(p / 240.0) * 4.0));
        bars *= Smooth(-9.0, -5.0, surveyed) * (1.0 - Smooth(-1.0, 1.0, surveyed)) * 1.3;
        return surveyed + (coves + tidal + bars) * influence;

    }

    public static double Value(Vector3d p) {

        int x = (int)Math.Floor(p.X);
        int y = (int)Math.Floor(p.Y);
        int z = (int)Math.Floor(p.Z);
        double u = Smooth(0.0, 1.0, p.X - x);
        double v = Smooth(0.0, 1.0, p.Y - y);
        double w = Smooth(0.0, 1.0, p.Z - z);
        double Lerp(double a, double b, double t) => a + (b - a) * t;
        return Lerp(Lerp(Lerp(Hash(x, y, z), Hash(x + 1, y, z), u),
            Lerp(Hash(x, y + 1, z), Hash(x + 1, y + 1, z), u), v),
            Lerp(Lerp(Hash(x, y, z + 1), Hash(x + 1, y, z + 1), u),
            Lerp(Hash(x, y + 1, z + 1), Hash(x + 1, y + 1, z + 1), u), v), w);

    }

    private static double Hash(int x, int y, int z) {

        unchecked {

            uint hash = (uint)x * 1597334677u ^ (uint)y * 3812015801u ^ (uint)z * 2798796415u;
            hash = (hash ^ (hash >> 16)) * 2246822519u;
            hash = (hash ^ (hash >> 13)) * 3266489917u;
            hash ^= hash >> 16;
            return (hash & 16777215u) / 16777216.0;

        }

    }

    private static double Smooth(double low, double high, double value) {

        double t = Math.Clamp((value - low) / (high - low), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);

    }

}
