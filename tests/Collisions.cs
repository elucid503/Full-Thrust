using FullThrust.Sim;

namespace FullThrust.Sim.Tests;

public static partial class Program {

    private static void VesselContacts() {

        Section("vessel contacts");
        Vessel a = new Vessel("A", new[] { Aegis.BuildStage(0.0) });
        Vessel b = new Vessel("B", new[] { Aegis.BuildStage(0.0) });
        Vector3d origin = new Vector3d(1_500_000.0, 700_000.0, -300_000.0);
        a.Position = origin;
        b.Position = origin + Vector3d.UnitX * 2.2;
        a.Velocity = new Vector3d(4.0, 3000.0, 0.0);
        b.Velocity = new Vector3d(-4.0, 3000.0, 0.0);
        Vector3d momentum = a.Velocity * a.Mass + b.Velocity * b.Mass;

        bool found = VesselCollision.Find(a, b, out VesselCollision.Contact contact);
        Near("overlapping side hulls contact", found ? 1.0 : 0.0, 1.0, 0.0);

        if (found) {

            Near("contact depth follows capsule width", contact.Depth, 0.2, 0.025);
            Near("contact normal points between hulls", Vector3d.Dot(contact.Normal, Vector3d.UnitX), 1.0, 0.01);
            VesselCollision.Resolve(a, b, contact);
            Close("contact preserves linear momentum", a.Velocity * a.Mass + b.Velocity * b.Mass, momentum, 1.0e-6);
            Near("contact reverses closing normal velocity", (b.Velocity - a.Velocity).X > 0.0 ? 1.0 : 0.0, 1.0, 0.0);
            Near("contact pushes hulls apart", (b.Position - a.Position).Length > 2.35 ? 1.0 : 0.0, 1.0, 0.0);

        }

        b.Position = origin + Vector3d.UnitX * 2.5;
        Near("nearby nonintersecting hulls stay clear", VesselCollision.Find(a, b, out _) ? 1.0 : 0.0, 0.0, 0.0);
        b.Position = origin + Vector3d.UnitZ * 1.3;
        b.Orientation = QuaternionD.FromAxisAngle(Vector3d.UnitY, 0.9);
        Near("tilted hull contacts", VesselCollision.Find(a, b, out _) ? 1.0 : 0.0, 1.0, 0.0);

        Vessel stage = new Vessel("stage", new[] { Meridian.BuildStage() });
        stage.Position = origin;
        b.Position = origin + new Vector3d(1.8, 0.0, 2.0);
        b.Orientation = QuaternionD.Identity;
        stage.Velocity = new Vector3d(0.0, 3000.0, 0.0);
        b.Velocity = new Vector3d(-5.0, 3000.0, 0.0);
        found = VesselCollision.Find(stage, b, out contact);
        Near("off-centre capsule and stage contact", found ? 1.0 : 0.0, 1.0, 0.0);

        if (found) {

            VesselCollision.Resolve(stage, b, contact);
            Near("off-centre impact induces rotation", stage.AngularVelocity.Length > 0.0001 ? 1.0 : 0.0, 1.0, 0.0);

        }

    }

}
