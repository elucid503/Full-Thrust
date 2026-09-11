namespace FullThrust.Sim;

/// <summary>Hull contact against the same body-fixed height field that builds the terrain mesh.</summary>
public static class GroundCollision {
    public readonly record struct Contact(double Clearance, Vector3d Point, Vector3d Normal);

    private static Vector3d Normal(CelestialBody body, Vector3d point, double time) {
        Vector3d up = point.Normalized;
        Vector3d east = Vector3d.Cross(Math.Abs(up.Z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX, up).Normalized;
        Vector3d north = Vector3d.Cross(up, east);
        double height = body.SurfaceRadiusUnder(point, time);
        double e = body.SurfaceRadiusUnder(point + east * 0.5, time) - height;
        double n = body.SurfaceRadiusUnder(point + north * 0.5, time) - height;
        return (up - east * (e * 2.0) - north * (n * 2.0)).Normalized;
    }

    public static Contact Measure(CelestialBody body, Vessel vessel, Vector3d position, QuaternionD attitude, double time) {
        Vector3d normal = Normal(body, position, time);
        Vector3d local = attitude.Conjugate.Rotate(-normal);
        double across = Math.Sqrt(local.X * local.X + local.Y * local.Y);
        Vector3d radial = across > 1e-10 ? new Vector3d(local.X / across, local.Y / across, 0) : Vector3d.UnitX;
        double clearance = double.PositiveInfinity;
        Vector3d point = position;
        var stations = VesselCollision.Stations(vessel);
        for (int index = 0; index < stations.Count; index++) {
            Hull.Station ring = stations[index];
            Vector3d sample = position + attitude.Rotate(radial * ring.Radius + Vector3d.UnitZ * (ring.Z - vessel.CentreOfMassZ));
            double height = body.HeightAboveGround(sample, time);
            if (height < clearance) { clearance = height; point = sample; }
        }
        return new Contact(clearance, point, Normal(body, point, time));
    }

    private static QuaternionD Blend(QuaternionD a, QuaternionD b, double t) {
        double sign = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W < 0 ? -1.0 : 1.0;
        return new QuaternionD(a.X * (1 - t) + b.X * t * sign, a.Y * (1 - t) + b.Y * t * sign,
            a.Z * (1 - t) + b.Z * t * sign, a.W * (1 - t) + b.W * t * sign).Normalized;
    }

    public static bool Resolve(CelestialBody body, Vessel vessel, Vector3d previous, QuaternionD attitude,
        double startTime, double time, bool damage = true) {
        if (!vessel.Intact) { return false; }
        Vector3d end = vessel.Position;
        QuaternionD endAttitude = vessel.Orientation;
        double bound = VesselCollision.Radius(vessel);
        double distance = (end - previous).Length;
        if (Math.Min(body.HeightAboveGround(previous, startTime), body.HeightAboveGround(end, time)) > bound + distance + 2.0) {
            return false;
        }
        Contact At(double t) => Measure(body, vessel, previous + (end - previous) * t,
            Blend(attitude, endAttitude, t), startTime + (time - startTime) * t);
        // Integration normally bounds this to a few metres. Also sweep debug/high-speed steps.
        int steps = Math.Clamp((int)Math.Ceiling((distance + vessel.AngularVelocity.Length * bound * (time - startTime)) / 2.0), 1, 256);
        double before = 0;
        double hitTime = -1;
        Contact hit = At(0);
        if (hit.Clearance <= 0.015) {
            // A supported hull must be able to lift off; a separating touch is not a new impact.
            if (hit.Clearance < -0.001 || At(1).Clearance <= hit.Clearance + 0.001) { hitTime = 0; }
        }
        for (int step = 1; hitTime < 0 && step <= steps; step++) {
            double t = (double)step / steps;
            Contact sample = At(t);
            if (sample.Clearance <= 0.015) {
                double low = before, high = t;
                for (int iteration = 0; iteration < 14; iteration++) {
                    double middle = (low + high) * 0.5;
                    if (At(middle).Clearance <= 0.015) { high = middle; } else { low = middle; }
                }
                hitTime = high;
                hit = At(high);
            }
            before = t;
        }
        if (hitTime < 0) { return false; }
        vessel.Position = previous + (end - previous) * hitTime;
        vessel.Orientation = Blend(attitude, endAttitude, hitTime);
        Vector3d lever = hit.Point - vessel.Position;
        Vector3d relative = vessel.Velocity - body.AirVelocityAt(hit.Point)
            + Vector3d.Cross(vessel.Orientation.Rotate(vessel.AngularVelocity), lever);
        double closing = Math.Max(0.0, -Vector3d.Dot(relative, hit.Normal));
        Vector3d torque = vessel.Orientation.Conjugate.Rotate(Vector3d.Cross(lever, hit.Normal));
        Vector3d inverseTorque = new(torque.X / vessel.Inertia.X, torque.Y / vessel.Inertia.Y, torque.Z / vessel.Inertia.Z);
        double inverseMass = 1.0 / vessel.Mass + Vector3d.Dot(torque, inverseTorque);
        double impulse = closing / inverseMass;
        vessel.Velocity += hit.Normal * (impulse / vessel.Mass);
        vessel.AngularVelocity += inverseTorque * impulse;
        // Resolve initial overlap as well as first crossing, including invulnerable/debug placements.
        for (int iteration = 0; iteration < 4; iteration++) {
            Contact overlap = Measure(body, vessel, vessel.Position, vessel.Orientation, time);
            if (overlap.Clearance >= 0.019) { break; }
            vessel.Position += overlap.Normal * ((0.02 - overlap.Clearance)
                / Math.Max(0.15, Vector3d.Dot(overlap.Normal, overlap.Point.Normalized)));
        }
        if (damage && closing > 8.0) { vessel.Fate = VesselFate.Impacted; }
        return true;
    }
}
