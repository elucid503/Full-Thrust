namespace FullThrust.Sim;

public static class ExhaustInteraction {

    // Effective coupling allows most of the impinging jet to escape around an open stage.
    public const double MomentumCoupling = 0.12;

    public readonly record struct Hit(Vessel Vessel, Vector3d Point, double Distance);
    public readonly record struct Emitter(Vector3d Position, Vector3d Axis, double Radius, double Length, double Spread, double Thrust);
    public readonly record struct SurfaceHit(Vector3d Point, Vector3d Normal, double Distance, bool Water);

    public static SurfaceHit? Surface(CelestialBody body, Vector3d origin, Vector3d direction, double reach, double time) {

        direction = direction.Normalized;
        double previous = 0.0;
        if (body.HeightAboveGround(origin, time) < -0.1) { return null; }

        for (int i = 1; i <= 24; i++) {

            double distance = reach * i / 24.0;
            Vector3d point = origin + direction * distance;
            if (body.HeightAboveGround(point, time) > 0.0) { previous = distance; continue; }

            for (int iteration = 0; iteration < 12; iteration++) {

                double middle = (previous + distance) * 0.5;
                if (body.HeightAboveGround(origin + direction * middle, time) > 0.0) { previous = middle; }
                else { distance = middle; }

            }

            point = origin + direction * distance;
            Vector3d up = point.Normalized;
            Vector3d side = Vector3d.Cross(up, Math.Abs(up.Z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX).Normalized;
            Vector3d ahead = Vector3d.Cross(up, side);
            double slopeX = (body.SurfaceRadiusUnder(point + side, time) - body.SurfaceRadiusUnder(point - side, time)) * 0.5;
            double slopeY = (body.SurfaceRadiusUnder(point + ahead, time) - body.SurfaceRadiusUnder(point - ahead, time)) * 0.5;
            bool water = body.Terrain != null && body.Terrain.Elevation(body.ToBodyFixed(point, time).Normalized) < -0.5;
            return new SurfaceHit(point, (up - side * slopeX - ahead * slopeY).Normalized, distance, water);

        }

        return null;

    }

    public static void Accumulate(Emitter emitter, Func<Vector3d, Vector3d, double, Hit?> trace) {

        if (emitter.Thrust <= 0.0 || emitter.Length <= 0.0) { return; }

        Vector3d axis = emitter.Axis.Normalized;
        Vector3d side = Vector3d.Cross(axis, Math.Abs(axis.Z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX).Normalized;
        Vector3d up = Vector3d.Cross(side, axis);
        const int samples = 61;

        // Equal-area rays carry a bounded share of the engine's outgoing momentum.
        for (int i = 0; i < samples; i++) {

            double radius = Math.Sqrt((i + 0.5) / samples);
            double angle = i * 2.399963229728653;
            Vector3d radial = (side * Math.Cos(angle) + up * Math.Sin(angle)) * radius;
            Vector3d origin = emitter.Position + radial * emitter.Radius;
            Vector3d direction = (axis + radial * emitter.Spread).Normalized;
            Hit? result = trace(origin, direction, emitter.Length);

            if (result is not Hit hit || !hit.Vessel.Intact || hit.Distance < 0.0 || hit.Distance > emitter.Length) { continue; }

            Vector3d force = direction * (emitter.Thrust * MomentumCoupling / samples);
            hit.Vessel.ExhaustForce += force;
            hit.Vessel.ExhaustTorque += hit.Vessel.Orientation.Conjugate.Rotate(Vector3d.Cross(hit.Point - hit.Vessel.Position, force));

        }

    }

}
