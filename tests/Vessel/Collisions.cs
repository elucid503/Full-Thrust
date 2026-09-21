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

        Interstage(origin);
        EngineContacts();

    }

    private static void EngineContacts() {
        Section("individual engine collision geometry");
        Vessel cluster = new("cluster", new[] { new Stage {
            Name = "three engines", ShellMass = 100.0,
            Hull = new Hull(new[] { new Hull.Station(2.0, 3.0), new Hull.Station(4.0, 3.0) }, 2.1, 3.9),
            Parts = new[] { new Part { Kind = PartKind.Engine, Bottom = 0.0, Top = 2.0, Count = 3, RingRadius = 1.8,
                Profile = new[] { new Hull.Station(0.0, 0.3), new Hull.Station(1.0, 0.1), new Hull.Station(2.0, 0.15) } } }
        } });
        Vessel probe = new("probe", new[] { new Stage { Name = "probe", ShellMass = 1.0,
            Hull = new Hull(new[] { new Hull.Station(-0.04, 0.04), new Hull.Station(0.04, 0.04) }, -0.03, 0.03) } });
        Vector3d Datum(Vector3d local) => local - Vector3d.UnitZ * cluster.CentreOfMassZ;
        foreach (EngineState engine in cluster.Active.EngineStates) {
            Vector3d local = engine.Mount - Vector3d.UnitZ * 1.8;
            probe.Position = Datum(local);
            Expect("each clustered bell has a solid collider", VesselCollision.Find(cluster, probe, out _), $"mount={engine.Mount}");
            Expect("exhaust sees the same clustered bell", VesselSurface.Distance(cluster, local) < 0.0, "bell interior");
        }
        probe.Position = Datum(Vector3d.UnitZ * 0.2);
        Expect("cluster centre stays open between bells", !VesselCollision.Find(cluster, probe, out _) && VesselSurface.Distance(cluster, Vector3d.UnitZ * 0.2) > 0.0, "clear centre");
        EngineState moving = cluster.Active.EngineStates[0];
        moving.Traverse(Vector3d.UnitY * 0.4, 0.5, 0.01, 1.0);
        Vector3d bent = moving.Mount + moving.ContactRotation.Rotate(-Vector3d.UnitZ * 1.8);
        probe.Position = Datum(bent);
        Expect("gimbaled bell collider follows its mount", VesselCollision.Find(cluster, probe, out _), "bent bell");
        Expect("gimbaled bell stops exhaust", VesselSurface.Distance(cluster, bent) < 0.0, "bent surface");
        probe.Position = Datum(moving.Mount - Vector3d.UnitZ * 1.8);
        Expect("gimbal vacates its previous collision volume", !VesselCollision.Find(cluster, probe, out _), "old bell position");
        cluster.Active.ContactHull = new Hull(new[] { new Hull.Station(2.0, 3.5), new Hull.Station(4.0, 3.5) }, 2.1, 3.9);
        cluster.Active.ContactRevision++;
        probe.Position = Datum(new Vector3d(3.4, 0, 3.0));
        Expect("refined imported profile invalidates collision cache", VesselCollision.Find(cluster, probe, out _), "updated hull");
        Expect("refined imported profile also stops exhaust", VesselSurface.Distance(cluster, new Vector3d(3.4, 0, 3.0)) < 0.0, "updated gas surface");
    }

    // The booster is a solid to its tank dome and a tube above it. A convex hull over the whole
    // stage would report the nested upper stage as two metres of overlap, so the first of these is
    // what says the cavity is really empty and the second is what says its wall is really there.
    private static void Interstage(Vector3d origin) {

        Section("interstage cavity");

        Hull bay = Zenith.BuildHull();
        Vessel booster = new Vessel("booster", new[] { Zenith.BuildStage() });
        Vessel upper = new Vessel("upper", new[] { Meridian.BuildStage() });

        Vector3d seat = Vector3d.UnitZ * (Zenith.PayloadDatum - booster.CentreOfMassZ + upper.CentreOfMassZ);

        booster.Position = origin;
        upper.Position = origin + seat;

        Near("a seated upper stage hangs clear of the interstage",
            VesselCollision.Find(booster, upper, out _) ? 1.0 : 0.0, 0.0, 0.0);

        double clearance = bay.BayRadius - Meridian.EngineMouthRadius;

        upper.Position = origin + seat + Vector3d.UnitX * (clearance * 0.4);

        Near("a small excursion still clears the wall",
            VesselCollision.Find(booster, upper, out _) ? 1.0 : 0.0, 0.0, 0.0);

        upper.Position = origin + seat + Vector3d.UnitX * (clearance + 0.08);

        bool fouled = VesselCollision.Find(booster, upper, out VesselCollision.Contact wall);
        Near("a bell driven sideways fouls the interstage wall", fouled ? 1.0 : 0.0, 1.0, 0.0);

        if (fouled) {

            Near("the wall pushes radially, not along the stack", Math.Abs(wall.Normal.Z), 0.0, 0.2);

        }

    }

}
