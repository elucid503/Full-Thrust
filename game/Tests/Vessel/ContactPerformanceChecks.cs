using System;
using System.Diagnostics;
using System.Reflection;
using FullThrust.Sim;
using Godot;

namespace FullThrust.Game;

// Repeatable CPU workload using the imported contact profiles, not the lower-resolution
// authored fallback. Run headless; timings deliberately are not machine-dependent assertions.
public sealed partial class ContactPerformanceChecks : Node {
    private static void Measure(string name, int count, Action work) {
        for (int i = 0; i < 10; i++) { work(); }
        double best = double.MaxValue;
        for (int run = 0; run < 3; run++) {
            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 0; i < count; i++) { work(); }
            best = Math.Min(best, watch.Elapsed.TotalMilliseconds / count);
        }
        GD.Print($"CONTACT PERF {name}: {best:F4} ms/op");
    }

    public override void _Ready() {
        try {
            Vessel source = Stack.Build();
            Vessel receiver = new("receiver", new[] { Meridian.BuildStage() });
            foreach (Vessel vessel in new[] { source, receiver }) {
                VesselView view = new(); AddChild(view); view.Build(vessel, false);
            }
            CelestialBody body = BodyCatalog.Home;
            body.Terrain = (Terrain)typeof(Flight).GetMethod("Survey", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            source.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, Vector3d.UnitX);
            source.Position = Vector3d.UnitX * (body.SolidRadiusUnder(Vector3d.UnitX * body.Radius, 0) + source.CentreOfMassZ - source.Base + 2);
            Vector3d parked = source.Position;
            GD.Print($"CONTACT PERF ground stations: {VesselCollision.Stations(source).Count}");
            Measure("ground measure", 80, () => GroundCollision.Measure(body, source, parked, source.Orientation, 0));
            Measure("near-ground step", 80, () => {
                source.Position = parked;
                GroundCollision.Resolve(body, source, parked, source.Orientation, 0, 1.0 / 120, false);
            });
            EngineState engine = source.Active.EngineStates[0];
            Vector3d bell = engine.Mount + engine.ContactRotation.Rotate(Vector3d.UnitZ * engine.ContactCentreZ) - Vector3d.UnitZ * source.CentreOfMassZ;
            receiver.Position = source.Position + source.Orientation.Rotate(bell);
            receiver.Orientation = source.Orientation;
            Measure("vessel contact", 40, () => VesselCollision.Find(source, receiver, out _));
            source.Throttle = 1;
            source.Active.AdvanceEngines(1, 100000, 2);
            Vector3d exit = source.Position + source.Orientation.Rotate(engine.Mount - engine.Direction * engine.ExitDistance - Vector3d.UnitZ * source.CentreOfMassZ);
            receiver.Position = exit - source.Orientation.Rotate(Vector3d.UnitZ) * 5;
            Vessel[] pair = { source, receiver };
            Measure("exhaust contact", 40, () => ExhaustInteraction.Apply(pair, 100000, 1.0 / 120));
            Vessel[] solo = { source };
            Measure("solo exhaust", 200, () => ExhaustInteraction.Apply(solo, 100000, 1.0 / 120));
            LaunchSite site = LaunchSite.Home;
            site.Commission(body);
            source.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, site.Up);
            parked = site.Up * (body.Radius + site.Height + source.CentreOfMassZ - source.Base + 2);
            Measure("pad ground step", 80, () => {
                source.Position = parked;
                GroundCollision.Resolve(body, source, parked, source.Orientation, 0, 1.0 / 120, false);
            });
            GetTree().Quit();
        } catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
