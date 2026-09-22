namespace FullThrust.Sim;

/// <summary>Displaced hull volume supplies buoyancy; point drag supplies translation and rotational damping.</summary>
public static class WaterPhysics {

    public const double SafeEntrySpeed = 10.0;
    private const int Slices = 24;
    private readonly record struct Immersed(Vector3d Point, Vector3d Up, Vector3d Flow, double Volume, double Radius);

    private static Vector3d InverseInertia(Vessel vessel, Vector3d torque) => new(
        torque.X / vessel.Inertia.X, torque.Y / vessel.Inertia.Y, torque.Z / vessel.Inertia.Z);

    private static void Impulse(Vessel vessel, Vector3d point, Vector3d impulse) {

        vessel.Velocity += impulse / vessel.Mass;
        Vector3d torque = vessel.Orientation.Conjugate.Rotate(Vector3d.Cross(point - vessel.Position, impulse));
        vessel.AngularVelocity += InverseInertia(vessel, torque);

    }

    public static bool Apply(CelestialBody body, Vessel vessel, double time, double dt, bool damage = true) {

        vessel.SubmergedVolume = 0.0;
        if (!vessel.Intact || body.Terrain == null || dt <= 0.0
            || body.AltitudeOf(vessel.Position) > VesselCollision.Radius(vessel) + 16.0) { return false; }
        double bed = Ocean.BedElevation(body, vessel.Position, time);
        if (bed > VesselCollision.Radius(vessel) + 2.0) { return false; }

        Span<Immersed> immersed = stackalloc Immersed[Slices];
        Span<Ocean.Wave> waves = stackalloc Ocean.Wave[Ocean.WaveCount];
        Ocean.Components(body, time, waves);
        int count = 0;
        double dz = vessel.Length / Slices;
        double entrySpeed = 0.0;
        double slidingSpeed = 0.0;

        for (int index = 0; index < Slices; index++) {

            double z = vessel.Base + (index + 0.5) * dz;
            double radius = vessel.RadiusAt(z);
            if (radius <= 0.0) { continue; }
            Vector3d centre = vessel.Position + vessel.Nose * (z - vessel.CentreOfMassZ);
            double elevation = Ocean.BedElevation(body, centre, time);
            Vector3d fixedPoint = body.ToBodyFixed(centre, time);
            Ocean.Surface water = Ocean.Sample(body, waves, fixedPoint, elevation);
            if (elevation >= water.Height) { continue; }
            Vector3d up = centre.Normalized;
            Vector3d localUp = vessel.Orientation.Conjugate.Rotate(up);
            double across = Math.Sqrt(localUp.X * localUp.X + localUp.Y * localUp.Y);
            double immersion = water.Height - body.AltitudeOf(centre);
            double fraction;
            Vector3d offset = Vector3d.Zero;

            if (across * radius < dz * 0.25) {

                fraction = Math.Clamp(0.5 + immersion / Math.Max(dz * Math.Abs(localUp.Z), 0.001), 0.0, 1.0);
                offset = Vector3d.UnitZ * (-Math.Sign(localUp.Z) * dz * (1.0 - fraction) * 0.5);

            } else {

                double cut = Math.Clamp(immersion / (radius * across), -1.0, 1.0);
                double area = Math.Acos(-cut) + cut * Math.Sqrt(Math.Max(0.0, 1.0 - cut * cut));
                fraction = area / Math.PI;
                double centroid = area > 1e-9 ? -2.0 * radius * Math.Pow(Math.Max(0.0, 1.0 - cut * cut), 1.5) / (3.0 * area) : 0.0;
                offset = new Vector3d(localUp.X, localUp.Y, 0.0) * (centroid / across);

            }

            if (fraction <= 0.0) { continue; }
            double volume = Math.PI * radius * radius * dz * fraction;
            Vector3d point = centre + vessel.Orientation.Rotate(offset);
            double attenuation = Math.Exp(-Math.Max(0.0, water.Height - body.AltitudeOf(point)) / 8.0);
            Vector3d flow = body.SurfaceVelocityAt(point) + body.ToInertial(water.Velocity, time) * attenuation;
            Vector3d relative = vessel.Velocity + Vector3d.Cross(vessel.Orientation.Rotate(vessel.AngularVelocity), point - vessel.Position) - flow;
            Vector3d normal = body.ToInertial((fixedPoint.Normalized - water.Gradient).Normalized, time);
            double closing = -Vector3d.Dot(relative, normal);
            entrySpeed = Math.Max(entrySpeed, closing);
            slidingSpeed = Math.Max(slidingSpeed, (relative - normal * Vector3d.Dot(relative, normal)).Length);
            immersed[count++] = new Immersed(point, up, flow, volume, radius);
            vessel.SubmergedVolume += volume;

        }

        if (count == 0) { return false; }
        if (damage && (entrySpeed > SafeEntrySpeed || slidingSpeed > 25.0)) {

            vessel.Fate = VesselFate.Impacted;
            return true;

        }

        for (int index = 0; index < count; index++) {

            Immersed sample = immersed[index];
            Impulse(vessel, sample.Point, sample.Up * (Ocean.Density * body.SurfaceGravity * sample.Volume * dt));

        }

        for (int index = 0; index < count; index++) {

            Immersed sample = immersed[index];
            Vector3d lever = sample.Point - vessel.Position;
            Vector3d relative = vessel.Velocity + Vector3d.Cross(vessel.Orientation.Rotate(vessel.AngularVelocity), lever) - sample.Flow;
            double speed = relative.Length;
            if (speed < 1e-9) { continue; }
            Vector3d direction = relative / speed;
            Vector3d torque = vessel.Orientation.Conjugate.Rotate(Vector3d.Cross(lever, direction));
            double inverseMass = 1.0 / vessel.Mass + Vector3d.Dot(torque, InverseInertia(vessel, torque));
            double area = sample.Volume / Math.Max(sample.Radius * 0.8, 0.1);
            double coefficient = 0.5 * Ocean.Density * area * (speed + 0.5);
            // Exponential point impulses cannot reverse slip, even for a light spent tank.
            double impulse = speed * (1.0 - Math.Exp(-coefficient * inverseMass * dt)) / inverseMass;
            Impulse(vessel, sample.Point, -direction * impulse);

        }

        // Axisymmetric samples cannot see rotation about the hull axis; skin friction can.
        double spinDrag = 1.0 - Math.Exp(-Ocean.Density * vessel.SubmergedVolume / vessel.Mass * dt);
        Vector3d spin = vessel.Orientation.Conjugate.Rotate(Vector3d.UnitZ * body.SpinRate);
        vessel.AngularVelocity += (spin - vessel.AngularVelocity) * spinDrag;
        return true;

    }

}
