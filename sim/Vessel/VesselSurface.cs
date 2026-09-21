using System.Collections.Generic;

namespace FullThrust.Sim;

/// <summary>Shared surface geometry for gas contact and the rigid-body collider.</summary>
public static class VesselSurface {
    public const double ProfileTolerance = 0.005;

    // Keep contour changes, not uniform mesh-sampling density. Radial error is bounded at
    // every original ring (and therefore along every original straight segment).
    public static Hull.Station[] SimplifyProfile(IReadOnlyList<Hull.Station> rings, double tolerance = ProfileTolerance) {
        if (rings.Count < 3) { return new List<Hull.Station>(rings).ToArray(); }
        bool[] keep = new bool[rings.Count];
        keep[0] = keep[^1] = true;
        void Split(int first, int last) {
            Hull.Station a = rings[first], b = rings[last];
            double worst = tolerance;
            int selected = -1;
            for (int i = first + 1; i < last; i++) {
                double t = (rings[i].Z - a.Z) / Math.Max(b.Z - a.Z, 1e-12);
                double error = Math.Abs(rings[i].Radius - (a.Radius + (b.Radius - a.Radius) * t));
                if (error > worst) { worst = error; selected = i; }
            }
            if (selected < 0) { return; }
            keep[selected] = true;
            Split(first, selected); Split(selected, last);
        }
        Split(0, rings.Count - 1);
        List<Hull.Station> result = new();
        for (int i = 0; i < rings.Count; i++) { if (keep[i]) { result.Add(rings[i]); } }
        return result.ToArray();
    }

    public static int Revision(Vessel vessel) {
        int revision = vessel.StageCount;
        foreach (Stage stage in vessel.Stages) { revision = unchecked(revision * 31 + stage.ContactRevision); }
        return revision;
    }

    public static double ProfileDistance(IReadOnlyList<Hull.Station> rings, Vector3d p) {
        if (rings.Count < 2) { return double.MaxValue; }
        double radial = Math.Sqrt(p.X * p.X + p.Y * p.Y);
        double distance = double.MaxValue;
        bool inside = false;
        for (int i = -1; i < rings.Count; i++) {
            Hull.Station a = i < 0 ? new Hull.Station(rings[0].Z, 0.0) : rings[i];
            Hull.Station b = i + 1 == rings.Count ? new Hull.Station(rings[^1].Z, 0.0) : rings[i + 1];
            double dr = b.Radius - a.Radius, dz = b.Z - a.Z;
            double t = Math.Clamp(((radial - a.Radius) * dr + (p.Z - a.Z) * dz) / Math.Max(dr * dr + dz * dz, 1e-12), 0.0, 1.0);
            double r = radial - a.Radius - dr * t, z = p.Z - a.Z - dz * t;
            distance = Math.Min(distance, Math.Sqrt(r * r + z * z));
            if ((a.Z > p.Z) != (b.Z > p.Z) && radial < a.Radius + dr * (p.Z - a.Z) / dz) { inside = !inside; }
        }
        return inside ? -distance : distance;
    }

    public static double HullDistance(Vessel vessel, Vector3d local) {
        double radial = Math.Sqrt(local.X * local.X + local.Y * local.Y);
        double result = double.MaxValue;
        foreach (Stage stage in vessel.Stages) {
            Hull hull = stage.ContactHull ?? stage.Hull;
            double distance = ProfileDistance(hull.Stations, local);
            if (hull.HasBay) { distance = Math.Max(distance, -Math.Max(radial - hull.BayRadius, hull.BayFloor - local.Z)); }
            result = Math.Min(result, distance);
        }
        return result;
    }

    public static double Distance(Vessel vessel, Vector3d local) {
        double distance = HullDistance(vessel, local);
        foreach (Stage stage in vessel.Stages) {
            foreach (EngineState engine in stage.EngineStates) {
                if (engine.ContactProfile == null) { continue; }
                Vector3d p = engine.ContactRotation.Conjugate.Rotate(local - engine.Mount);
                if ((p - Vector3d.UnitZ * engine.ContactCentreZ).Length - engine.ContactBound > distance) { continue; }
                distance = Math.Min(distance, ProfileDistance(engine.ContactProfile, p));
            }
        }
        return distance;
    }

    // Exact intersections with the same piecewise conical surface used by the distance query.
    // Exhaust rays no longer repeatedly scan every ring while sphere-marching toward it.
    public static double RaycastProfile(IReadOnlyList<Hull.Station> rings, Vector3d origin, Vector3d direction, double reach, Hull bay = null) {
        if (rings.Count < 2) { return -1; }
        double best = reach;
        bool found = false;
        void Candidate(double t, double bottom, double top) {
            if (!double.IsFinite(t) || t < 0 || t > best) { return; }
            double z = origin.Z + direction.Z * t;
            if (z < bottom - 1e-8 || z > top + 1e-8) { return; }
            best = t; found = true;
        }
        void Side(double z, double radius, double slope, double bottom, double top) {
            double r = radius + slope * (origin.Z - z);
            double dr = slope * direction.Z;
            double a = direction.X * direction.X + direction.Y * direction.Y - dr * dr;
            double b = 2 * (origin.X * direction.X + origin.Y * direction.Y - r * dr);
            double c = origin.X * origin.X + origin.Y * origin.Y - r * r;
            if (Math.Abs(a) < 1e-14) { if (Math.Abs(b) > 1e-14) { Candidate(-c / b, bottom, top); } return; }
            double discriminant = b * b - 4 * a * c;
            if (discriminant < 0) { return; }
            double root = Math.Sqrt(discriminant);
            double q = -0.5 * (b + Math.CopySign(root, b));
            if (Math.Abs(q) < 1e-14) { Candidate(-b / (2 * a), bottom, top); }
            else { Candidate(q / a, bottom, top); Candidate(c / q, bottom, top); }
        }
        void Cap(double z, double radius, double hole = 0) {
            if (Math.Abs(direction.Z) < 1e-14) { return; }
            double t = (z - origin.Z) / direction.Z;
            if (t < 0 || t > best) { return; }
            Vector3d p = origin + direction * t;
            double square = p.X * p.X + p.Y * p.Y;
            if (square <= radius * radius + 1e-10 && square >= hole * hole - 1e-10) { Candidate(t, z, z); }
        }
        double initial = ProfileDistance(rings, origin);
        if (bay?.HasBay == true) {
            initial = Math.Max(initial, -Math.Max(Math.Sqrt(origin.X * origin.X + origin.Y * origin.Y) - bay.BayRadius, bay.BayFloor - origin.Z));
        }
        if (initial <= 0) { return 0; }
        for (int i = 1; i < rings.Count; i++) {
            Hull.Station a = rings[i - 1], b = rings[i];
            double height = b.Z - a.Z;
            if (height <= 1e-12) { Cap(a.Z, Math.Max(a.Radius, b.Radius), Math.Min(a.Radius, b.Radius)); continue; }
            Side(a.Z, a.Radius, (b.Radius - a.Radius) / height, a.Z, b.Z);
        }
        Cap(rings[0].Z, rings[0].Radius);
        Cap(rings[^1].Z, rings[^1].Radius, bay?.HasBay == true ? bay.BayRadius : 0);
        if (bay?.HasBay == true) {
            Side(bay.BayFloor, bay.BayRadius, 0, bay.BayFloor, bay.Tip);
            Cap(bay.BayFloor, bay.BayRadius);
        }
        return found ? best : -1;
    }
}
