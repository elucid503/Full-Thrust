using FullThrust.Sim;

namespace FullThrust.Sim.Tests;

public static partial class Program {
    private static void ExhaustContacts() {
        Section("volumetric exhaust contacts");
        Vessel source = new("source", new[] { Meridian.BuildStage() }) { Throttle = 1.0 };
        source.Active.AdvanceEngines(1.0, 0.0, 2.0);
        EngineState engine = source.Active.EngineStates[0];
        Vector3d exit = engine.Mount - engine.Direction * engine.ExitDistance - Vector3d.UnitZ * source.CentreOfMassZ;
        Vessel target = new("target", new[] { Aegis.BuildStage(0.0) });
        target.Position = exit - Vector3d.UnitZ * 5.0;
        Vessel[] pair = { source, target };
        Expect("powered exhaust strikes receiving hull", ExhaustInteraction.Apply(pair, 0.0, 0.1), "contact");
        Expect("exhaust pushes away from nozzle", target.Velocity.Z < 0.0, $"velocity={target.Velocity}");
        Expect("receiver acceleration is restrained", target.Velocity.Length <= ExhaustInteraction.MaximumAcceleration * 0.1 + 1e-12, $"speed={target.Velocity.Length}");
        Close("source gets no duplicate engine recoil", source.Velocity, Vector3d.Zero, 0.0);
        Vector3d once = target.Velocity;
        target.Velocity = Vector3d.Zero;
        for (int i = 0; i < 10; i++) { ExhaustInteraction.Apply(pair, 0.0, 0.01); }
        Close("impulse is independent of step partition", target.Velocity, once, 1e-10);
        Vector3d translation = new Vector3d(8e8, -3e8, 5e8);
        QuaternionD rotation = QuaternionD.FromAxisAngle(new Vector3d(1.0, 2.0, 3.0).Normalized, 1.2);
        Vector3d originalTarget = target.Position;
        source.Position = translation;
        source.Orientation = target.Orientation = rotation;
        target.Position = translation + rotation.Rotate(originalTarget);
        target.Velocity = target.AngularVelocity = Vector3d.Zero;
        ExhaustInteraction.Apply(pair, 0.0, 0.1);
        Close("contacts survive rotation and large inertial positions", target.Velocity, rotation.Rotate(once), 1e-7);
        source.Position = Vector3d.Zero;
        source.Orientation = target.Orientation = QuaternionD.Identity;
        target.Position = originalTarget;
        Vector3d centred = target.Position;
        target.Position += Vector3d.UnitX * 1.8;
        target.Velocity = target.AngularVelocity = Vector3d.Zero;
        ExhaustInteraction.Apply(pair, 0.0, 0.1);
        Expect("finite plume catches a grazing hull", target.Velocity.Length > 0.0, $"speed={target.Velocity.Length}");
        Expect("off-centre gas imparts torque", target.AngularVelocity.Length > 1e-7, $"spin={target.AngularVelocity.Length}");
        Expect("spin stays restrained", target.AngularVelocity.Length <= ExhaustInteraction.MaximumAngularAcceleration * 0.1 + 1e-12, "spin cap");
        target.Position = exit + Vector3d.UnitZ * 8.0;
        target.Velocity = Vector3d.Zero;
        ExhaustInteraction.Apply(pair, 0.0, 0.1);
        Close("hull upstream of nozzle is untouched", target.Velocity, Vector3d.Zero, 0.0);
        target.Position = centred + Vector3d.UnitX * 1000.0;
        ExhaustInteraction.Apply(pair, 0.0, 0.1);
        Close("distant hull is untouched", target.Velocity, Vector3d.Zero, 0.0);
        target.Position = centred;
        source.Active.AdvanceEngines(0.0, 0.0, 2.0);
        ExhaustInteraction.Apply(pair, 0.0, 0.1);
        Close("cutoff removes pressure", target.Velocity, Vector3d.Zero, 0.0);

        source.Active.AdvanceEngines(1.0, 0.0, 2.0);
        Vessel shield = new("shield", new[] { new Stage {
            Name = "plate", ShellMass = 1000.0,
            Hull = new Hull(new[] { new Hull.Station(0.0, 10.0), new Hull.Station(1.0, 10.0) }, 0.1, 0.9)
        } });
        shield.Position = exit - Vector3d.UnitZ * 2.0;
        ExhaustInteraction.Apply(new[] { source, shield, target }, 0.0, 0.1);
        Close("first hull shields the hull behind", target.Velocity, Vector3d.Zero, 0.0);
        Expect("shield receives exhaust pressure", shield.Velocity.Z < 0.0, "shield");

        Vessel secondSource = new("second source", new[] { Meridian.BuildStage() }) { Throttle = 1.0 };
        secondSource.Active.AdvanceEngines(1.0, 0.0, 2.0);
        target.Velocity = target.AngularVelocity = Vector3d.Zero;
        ExhaustInteraction.Apply(new[] { source, secondSource, target }, 0.0, 0.1);
        Expect("multiple engines share one receiver acceleration budget", target.Velocity.Length <= ExhaustInteraction.MaximumAcceleration * 0.1 + 1e-12, $"speed={target.Velocity.Length}");
        Expect("multiple engines share one receiver spin budget", target.AngularVelocity.Length <= ExhaustInteraction.MaximumAngularAcceleration * 0.1 + 1e-12, $"spin={target.AngularVelocity.Length}");

        Vessel bay = new("bay", new[] { Zenith.BuildStage() });
        Hull hull = bay.Active.Hull;
        double distance = ExhaustInteraction.Raycast(bay, Vector3d.UnitZ * (hull.Tip + 1.0 - bay.CentreOfMassZ), -Vector3d.UnitZ, hull.Length + 2.0);
        Near("exhaust enters open bay and reaches floor", distance, hull.Tip + 1.0 - hull.BayFloor, 0.005);
        ExhaustRayGeometry();
    }

    private static void ExhaustRayGeometry() {
        Hull.Station[] cone = { new(0, 1), new(2, 0.5) };
        Near("analytic ray hits tapered side", VesselSurface.RaycastProfile(cone, new Vector3d(2, 0, 1), -Vector3d.UnitX, 10), 1.25, 1e-12);
        Near("analytic ray hits end cap", VesselSurface.RaycastProfile(cone, new Vector3d(0, 0, 3), -Vector3d.UnitZ, 10), 1, 1e-12);
        Near("analytic ray starts inside solid", VesselSurface.RaycastProfile(cone, new Vector3d(0, 0, 1), Vector3d.UnitX, 10), 0, 0);
        Hull.Station[] tube = { new(0, 1), new(2, 1) };
        Near("analytic tangent ray stays finite", VesselSurface.RaycastProfile(tube, new Vector3d(-2, 1, 1), Vector3d.UnitX, 10), 2, 1e-12);

        Hull.Station[] dense = new Hull.Station[65];
        for (int i = 0; i < dense.Length; i++) { dense[i] = new Hull.Station(i / 32.0, 0.1 + Math.Sqrt(i / 64.0)); }
        Hull.Station[] reduced = VesselSurface.SimplifyProfile(dense);
        Hull shape = new Hull(reduced, 0.1, 1.9);
        double error = 0;
        foreach (Hull.Station ring in dense) { error = Math.Max(error, Math.Abs(ring.Radius - shape.RadiusAt(ring.Z))); }
        Expect("compact contour stays within five millimetres", error <= VesselSurface.ProfileTolerance + 1e-12 && reduced.Length < dense.Length / 2, $"{reduced.Length} rings, error={error}");

        Random random = new(147);
        foreach (Stage stage in new[] { Zenith.BuildStage(), Meridian.BuildStage(), Aegis.BuildStage(0) }) {
            Vessel vessel = new("ray comparison", new[] { stage });
            bool agrees = true;
            for (int sample = 0; sample < 24; sample++) {
                double angle = random.NextDouble() * Math.Tau;
                Vector3d from = new Vector3d(Math.Cos(angle) * 12, Math.Sin(angle) * 12, vessel.Base + random.NextDouble() * (vessel.Tip - vessel.Base));
                Vector3d toward = new Vector3d(0, 0, vessel.Base + random.NextDouble() * (vessel.Tip - vessel.Base));
                Vector3d axis = (toward - from).Normalized;
                double hit = ExhaustInteraction.Raycast(vessel, from - Vector3d.UnitZ * vessel.CentreOfMassZ, axis, 40);
                double reference = 0;
                for (int step = 0; step < 512 && reference < 40; step++) {
                    double d = VesselSurface.Distance(vessel, from + axis * reference);
                    if (d < 0.0001) { break; }
                    reference += Math.Max(d * 0.7, 0.00001);
                }
                if (reference < 40 && (hit < 0 || Math.Abs(hit - reference) > 0.003)) { agrees = false; }
            }
            Expect("analytic exhaust rays preserve " + stage.Name + " contacts", agrees, "compared with full distance-field marching");
        }
    }
}
