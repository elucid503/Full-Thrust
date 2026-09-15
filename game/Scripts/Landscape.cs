using System;

using FullThrust.Sim;

namespace FullThrust.Game;

internal static class Landscape {

    // Commissioned launch plateaus stay level beyond the apron. Only pavement excludes plants.
    public static bool OnPavement(Vector3d direction, Terrain terrain, double radius, double margin) {

        foreach (Terrain.Plateau plateau in terrain.Plateaus) {

            Vector3d offset = (direction - plateau.Centre) * radius;
            if (offset.LengthSquared > 2500.0) { continue; }
            Vector3d east = Vector3d.Cross(Vector3d.UnitZ, plateau.Centre).Normalized;
            Vector3d north = Vector3d.Cross(plateau.Centre, east);
            if (Math.Abs(Vector3d.Dot(offset, east)) <= LaunchComplex.ApronWidth * 0.5 + margin
                && Math.Abs(Vector3d.Dot(offset, north)) <= LaunchComplex.ApronDepth * 0.5 + margin) {

                return true;

            }

        }
        return false;

    }

    // Matches the Ground shader in the renderer's body-fixed coordinate system.
    public static double Mosaic(Vector3d direction, double radius) {

        Vector3d p = new(direction.X * radius, direction.Z * radius, -direction.Y * radius);
        double region = Value(p / 1800.0);
        Vector3d q = new(p.X / 650.0, p.Y / 1100.0, p.Z / 420.0);
        q += new Vector3d(region * 1.7, region * 0.8, -region * 1.3);
        return Value(q) * 0.55 + Value(q * 2.13 + new Vector3d(17.0, 3.0, 9.0)) * 0.30
            + Value(q * 5.71 + new Vector3d(2.0, 31.0, 7.0)) * 0.15;

    }

    public static double Coastal(double latitude, double elevation, double aridity) =>
        (1.0 - Smooth(0.55, 0.75, Math.Abs(latitude))) * (1.0 - Smooth(15.0, 45.0, elevation)) * (1.0 - aridity);

    public static double Woodland(double mosaic) => Smooth(0.42, 0.68, mosaic);

    public static double Smooth(double low, double high, double value) {

        double t = Math.Clamp((value - low) / (high - low), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);

    }

    private static double Value(Vector3d p) {

        double px = p.X;
        double py = p.Y;
        double pz = p.Z;
        if (!double.IsFinite(px) || !double.IsFinite(py) || !double.IsFinite(pz)) {

            return 0.0;

        }

        int x = Cell(px);
        int y = Cell(py);
        int z = Cell(pz);
        double u = Smooth(0.0, 1.0, px - Math.Floor(px));
        double v = Smooth(0.0, 1.0, py - Math.Floor(py));
        double w = Smooth(0.0, 1.0, pz - Math.Floor(pz));
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
