using System.Collections.Generic;

namespace FullThrust.Sim;

/// <summary>Finite exhaust streams transfer a small part of their momentum to the first hull
/// they meet. Operates in inertial doubles, independently of the camera and visual effects.</summary>
public static class ExhaustInteraction {

    public const double Coupling = 0.025;
    public const double MaximumAcceleration = 0.8;
    public const double MaximumAngularAcceleration = 0.12;
    public const double ReachRadii = 70.0;
    private const int Samples = 37;

    public static double Distance(Vessel vessel, Vector3d local) => VesselSurface.Distance(vessel, local);

    public static double Raycast(Vessel vessel, Vector3d origin, Vector3d direction, double reach) {
        Vector3d local = vessel.Orientation.Conjugate.Rotate(origin - vessel.Position) + Vector3d.UnitZ * vessel.CentreOfMassZ;
        Vector3d axis = vessel.Orientation.Conjugate.Rotate(direction);
        double nearest = reach;
        bool found = false;
        IReadOnlyList<Stage> stages = vessel.Stages;
        for (int i = 0; i < stages.Count; i++) {
            Stage stage = stages[i];
            Hull hull = stage.ContactHull ?? stage.Hull;
            double hit = VesselSurface.RaycastProfile(hull.Stations, local, axis, nearest, hull);
            if (hit >= 0) { nearest = hit; found = true; }
            foreach (EngineState engine in stage.EngineStates) {
                if (engine.ContactProfile == null) { continue; }
                Vector3d p = engine.ContactRotation.Conjugate.Rotate(local - engine.Mount);
                Vector3d d = engine.ContactRotation.Conjugate.Rotate(axis);
                Vector3d centre = Vector3d.UnitZ * engine.ContactCentreZ - p;
                double along = Math.Clamp(Vector3d.Dot(centre, d), 0, nearest);
                if ((centre - d * along).LengthSquared > engine.ContactBound * engine.ContactBound) { continue; }
                hit = VesselSurface.RaycastProfile(engine.ContactProfile, p, d, nearest);
                if (hit >= 0) { nearest = hit; found = true; }
            }
        }
        return found ? nearest : -1.0;
    }

    public static bool Apply(IReadOnlyList<Vessel> vessels, double pressure, double dt) {
        if (vessels.Count < 2 || !double.IsFinite(dt) || dt <= 0.0) { return false; }
        Span<Vector3d> forces = vessels.Count <= 64 ? stackalloc Vector3d[vessels.Count] : new Vector3d[vessels.Count];
        Span<Vector3d> torques = vessels.Count <= 64 ? stackalloc Vector3d[vessels.Count] : new Vector3d[vessels.Count];
        forces.Clear(); torques.Clear();
        Span<double> bounds = vessels.Count <= 64 ? stackalloc double[vessels.Count] : new double[vessels.Count];
        for (int i = 0; i < vessels.Count; i++) { bounds[i] = VesselCollision.Radius(vessels[i]); }
        bool changed = false;
        foreach (Vessel source in vessels) {
            if (!source.Intact || source.CurrentThrust <= 0.0) { continue; }
            Stage stage = source.Active;
            double power = 0.0;
            foreach (EngineState engine in stage.EngineStates) { power += engine.Power; }
            double rating = stage.RatingPressurePascals > 0.0 ? stage.RatingPressurePascals : 40000.0;
            double opening = 0.035 + 0.55 * Math.Clamp((rating - pressure) / Math.Max(rating + pressure, 1.0), 0.0, 1.0);
            foreach (EngineState engine in stage.EngineStates) {
                if (engine.Power <= 0.0001 || engine.ExitRadius <= 0.0) { continue; }
                Vector3d axis = source.Orientation.Rotate(-engine.Direction);
                Vector3d exit = source.Position + source.Orientation.Rotate(engine.Mount - engine.Direction * engine.ExitDistance - Vector3d.UnitZ * source.CentreOfMassZ);
                Vector3d localAxis = -engine.Direction;
                Vector3d side = source.Orientation.Rotate(Vector3d.Cross(localAxis, Math.Abs(localAxis.Z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX).Normalized);
                Vector3d up = Vector3d.Cross(axis, side);
                double reach = engine.ExitRadius * ReachRadii;
                for (int sample = 0; sample < Samples; sample++) {
                    double radius = Math.Sqrt((double)sample / Samples);
                    double angle = sample * 2.399963229728653;
                    Vector3d offset = (side * Math.Cos(angle) + up * Math.Sin(angle)) * radius;
                    Vector3d direction = (axis + offset * opening).Normalized;
                    Vector3d start = exit + offset * engine.ExitRadius;
                    int nearest = -1;
                    double hit = reach;
                    for (int index = 0; index < vessels.Count; index++) {
                        Vessel target = vessels[index];
                        if (target == source) { continue; }
                        double bound = bounds[index];
                        Vector3d relative = target.Position - start;
                        double axial = Vector3d.Dot(relative, direction);
                        if (axial < -bound || axial > reach + bound || (relative - direction * Math.Clamp(axial, 0.0, reach)).LengthSquared > bound * bound) { continue; }
                        double distance = Raycast(target, start, direction, hit);
                        if (distance >= 0.0 && distance < hit) { nearest = index; hit = distance; }
                    }
                    if (nearest < 0 || !vessels[nearest].Intact) { continue; }
                    double fade = Math.Pow(1.0 - hit / reach, 2.0);
                    Vector3d force = direction * (source.CurrentThrust * engine.Power / Math.Max(power, 1e-9) * Coupling * fade / Samples);
                    forces[nearest] += force;
                    torques[nearest] += Vector3d.Cross(start + direction * hit - vessels[nearest].Position, force);
                }
            }
        }
        for (int i = 0; i < vessels.Count; i++) {
            Vessel target = vessels[i];
            if (forces[i].LengthSquared <= 0.0) { continue; }
            double cap = Math.Min(1.0, target.Mass * MaximumAcceleration / forces[i].Length);
            target.Velocity += forces[i] * (cap * dt / target.Mass);
            Vector3d torque = target.Orientation.Conjugate.Rotate(torques[i]) * cap;
            Vector3d spin = new Vector3d(torque.X / Math.Max(target.Inertia.X, 1.0), torque.Y / Math.Max(target.Inertia.Y, 1.0), torque.Z / Math.Max(target.Inertia.Z, 1.0));
            target.AngularVelocity += spin * (Math.Min(1.0, MaximumAngularAcceleration / Math.Max(spin.Length, 1e-9)) * dt);
            changed = true;
        }
        return changed;
    }
}
