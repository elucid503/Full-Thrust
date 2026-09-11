using FullThrust.Sim;
namespace FullThrust.Sim.Tests;
public static partial class Program {
    private static void GroundHullContacts() {
        Section("ground hull contact");
        Vessel vessel = new("ground probe", new[] { Aegis.BuildStage(0) });
        double time = 1000;
        Vector3d up = Vector3d.UnitX;
        Vector3d surface = up * Home.SurfaceRadiusUnder(up * Home.Radius, time);
        vessel.Orientation = QuaternionD.LookAlong(up, Vector3d.UnitZ);
        Vector3d start = surface + up * 10;
        vessel.Position = surface - up * 5;
        vessel.Velocity = Home.AirVelocityAt(vessel.Position) - up * 100;
        bool hit = GroundCollision.Resolve(Home, vessel, start, vessel.Orientation, time, time + 0.15);
        Near("fast ground crossing hits hull", hit ? 1 : 0, 1, 0);
        Near("hard impact causes damage", vessel.Fate == VesselFate.Impacted ? 1 : 0, 1, 0);
        Near("tail stays above surface", GroundCollision.Measure(Home, vessel, vessel.Position, vessel.Orientation, time + 0.15).Clearance >= -0.03 ? 1 : 0, 1, 0);
        vessel.Fate = VesselFate.Flying;
        vessel.Position = surface;
        vessel.Velocity = Home.AirVelocityAt(vessel.Position);
        GroundCollision.Resolve(Home, vessel, vessel.Position, vessel.Orientation, time, time, false);
        Near("embedded vessel pushed out", GroundCollision.Measure(Home, vessel, vessel.Position, vessel.Orientation, time).Clearance >= -0.03 ? 1 : 0, 1, 0);
        Near("damage-free contact remains physical", vessel.Intact ? 1 : 0, 1, 0);
        Vector3d resting = vessel.Position;
        vessel.Position = resting + up;
        vessel.Velocity = Home.AirVelocityAt(vessel.Position) + up * 10;
        GroundCollision.Resolve(Home, vessel, resting, vessel.Orientation, time, time + 0.1, false);
        Near("supported vessel can lift off", (vessel.Position - resting).Length > 0.9 ? 1 : 0, 1, 0);
        vessel.Position = surface + up * 1000;
        Near("distant vessel has no ground contact", GroundCollision.Resolve(Home, vessel, vessel.Position, vessel.Orientation, time, time) ? 1 : 0, 0, 0);
    }
}
