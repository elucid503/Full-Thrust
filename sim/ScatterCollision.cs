namespace FullThrust.Sim;

/// <summary>Continuous sphere/capsule contact in body-fixed metres; no engine physics objects.</summary>
public static class ScatterCollision {

    public static bool Sweep(Vector3d start, Vector3d end, Vector3d a, Vector3d b,
        double radius, out double fraction, out Vector3d normal) {
        Vector3d axis = b - a;
        double length2 = axis.LengthSquared;
        Vector3d motion = end - start;
        Vector3d offset = start - a;
        Vector3d nearest = a + axis * (length2 > 1e-12
            ? Math.Clamp(Vector3d.Dot(offset, axis) / length2, 0.0, 1.0) : 0.0);
        Vector3d separation = start - nearest;
        fraction = double.PositiveInfinity;
        normal = Vector3d.Zero;
        if (separation.LengthSquared <= radius * radius) {
            fraction = 0.0;
            normal = separation.LengthSquared > 1e-12 ? separation.Normalized
                : motion.LengthSquared > 1e-12 ? -motion.Normalized : Vector3d.UnitX;
            return true;
        }
        double motion2 = motion.LengthSquared;
        if (motion2 < 1e-18) { return false; }
        double best = double.PositiveInfinity;
        void Sphere(Vector3d centre) {
            Vector3d delta = start - centre;
            double halfB = Vector3d.Dot(delta, motion);
            double discriminant = halfB * halfB - motion2 * (delta.LengthSquared - radius * radius);
            if (discriminant < 0.0) { return; }
            double t = (-halfB - Math.Sqrt(discriminant)) / motion2;
            if (t >= 0.0 && t <= 1.0) { best = Math.Min(best, t); }
        }
        Sphere(a);
        Sphere(b);
        if (length2 > 1e-12) {
            double along = Vector3d.Dot(motion, axis) / length2;
            double origin = Vector3d.Dot(offset, axis) / length2;
            Vector3d perpendicular = motion - axis * along;
            Vector3d radial = offset - axis * origin;
            double aa = perpendicular.LengthSquared;
            double bb = Vector3d.Dot(perpendicular, radial);
            double dd = bb * bb - aa * (radial.LengthSquared - radius * radius);
            if (aa > 1e-18 && dd >= 0.0) {
                double t = (-bb - Math.Sqrt(dd)) / aa;
                double height = origin + along * t;
                if (t >= 0.0 && t <= 1.0 && height >= 0.0 && height <= 1.0) { best = Math.Min(best, t); }
            }
        }
        if (!double.IsFinite(best)) { return false; }
        fraction = best;
        Vector3d point = start + motion * best;
        nearest = a + axis * (length2 > 1e-12
            ? Math.Clamp(Vector3d.Dot(point - a, axis) / length2, 0.0, 1.0) : 0.0);
        normal = (point - nearest).Normalized;
        return true;
    }
}
