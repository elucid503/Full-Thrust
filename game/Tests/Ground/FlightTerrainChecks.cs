using System;
using System.Reflection;
using FullThrust.Sim;
using static FullThrust.Game.Checks;
using Godot;

namespace FullThrust.Game;

// End-to-end checks: actual Flight.Advance, real survey, no visual cell warm-up.
public sealed partial class FlightTerrainChecks : Node {
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public override void _Ready() {
        try {
            Main main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            main.SetProcess(false);
            Flight flight = Flight.Active;
            flight.PlaceAt(28.7, -80.8, 8, 0);
            Vector3d up = flight.Vessel.Position.Normalized;
            flight.Vessel.Velocity = flight.Body.AirVelocityAt(flight.Vessel.Position) - up * 30;
            for (int i = 0; i < 120 && flight.Vessel.Intact; i++) { flight.Advance(1.0 / 120); }
            Check(flight.ContactCount > 0, "Flight.Advance reports ground hull contact");
            Check(flight.Vessel.Fate == VesselFate.Impacted, "hard ground impact ends flight");
            Check(GroundCollision.Measure(flight.Body, flight.Vessel, flight.Vessel.Position,
                flight.Vessel.Orientation, flight.Time).Clearance >= -0.03, "crashed hull remains above terrain");
            Vector3d wreck = flight.Body.ToBodyFixed(flight.Vessel.Position, flight.Time);
            flight.Advance(0.2);
            Check((flight.Body.ToBodyFixed(flight.Vessel.Position, flight.Time) - wreck).Length < 0.01,
                "wreck stays fixed to rotating terrain");
            Check(flight.Body.HeightAboveGround(flight.Vessel.Position, flight.Time) > 0.5,
                "ground contact occurs before centre reaches terrain");

            flight.Invulnerable = true;
            flight.Vessel.Fate = VesselFate.Flying;
            flight.PlaceAt(28.7, -80.8, 0.1, 0);
            up = flight.Vessel.Position.Normalized;
            flight.Vessel.Position -= up * 0.9;
            flight.Advance(1.0 / 120);
            Check(flight.Vessel.Intact, "invulnerability suppresses impact damage");
            Check(GroundCollision.Measure(flight.Body, flight.Vessel, flight.Vessel.Position,
                flight.Vessel.Orientation, flight.Time).Clearance >= -0.03, "invulnerable vessel still depenetrates ground");

            // Use a different cell that the camera/flight has never visited.
            Forest forest = (Forest)typeof(Planet).GetField("_forest", Private).GetValue(Planet.Active);
            int rows = (int)typeof(Forest).GetField("_rows", Private).GetValue(forest);
            int row = (int)((28.75 + 90) / 180 * rows);
            Type keyType = typeof(Forest).GetNestedType("Key", BindingFlags.NonPublic);
            object grove = null;
            Transform3D[] trees = Array.Empty<Transform3D>();
            // Coastal landforms can leave the old fixed cell bare; find a nearby cold wooded cell.
            for (int radius = 0; radius <= 8 && trees.Length == 0; radius++) {
                for (int dy = -radius; dy <= radius && trees.Length == 0; dy++) {
                    int candidateRow = row + dy;
                    int candidateColumns = (int)typeof(Forest).GetMethod("Columns", Private).Invoke(forest, new object[] { candidateRow });
                    int centreColumn = (int)((-80.85 + 180) / 360 * candidateColumns);
                    for (int dx = -radius; dx <= radius && trees.Length == 0; dx++) {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) { continue; }
                        object key = Activator.CreateInstance(keyType, candidateRow, centreColumn + dx);
                        grove = typeof(Forest).GetMethod("Generate", Private).Invoke(forest, new object[] { key, default(System.Threading.CancellationToken) });
                        trees = (Transform3D[])grove.GetType().GetField("Trees").GetValue(grove);
                    }
                }
            }
            Check(trees.Length > 0, "unstreamed test cell contains trees");
            Vector3d anchor = (Vector3d)grove.GetType().GetField("Anchor").GetValue(grove);
            Vector3d tree = anchor + Frames.Sim(trees[0].Origin);
            up = tree.Normalized;
            Vector3d east = Vector3d.Cross(Vector3d.UnitZ, up).Normalized;
            Vector3d inertialUp = flight.Body.ToInertial(up, flight.Time);
            Vector3d inertialEast = flight.Body.ToInertial(east, flight.Time);
            flight.PlaceAt(28.75, -80.85, 6, 100);
            flight.Invulnerable = false;
            flight.Vessel.Fate = VesselFate.Flying;
            flight.Vessel.Orientation = QuaternionD.LookAlong(inertialEast, inertialUp);
            flight.Vessel.Position = flight.Body.ToInertial(tree + up * 4
                - east * (flight.Vessel.Tip - flight.Vessel.CentreOfMassZ + 6), flight.Time);
            flight.Vessel.Velocity = flight.Body.AirVelocityAt(flight.Vessel.Position) + inertialEast * 100;
            int contacts = flight.ContactCount;
            int destroyed = Planet.Active.DestroyedScatter;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 120 && flight.Vessel.Intact; i++) { flight.Advance(1.0 / 120); }
            timer.Stop();
            Check(flight.ContactCount > contacts, "cold-cell flight hits terrain objects without rendering");
            Check(Planet.Active.DestroyedScatter > destroyed, "actual flight destroys a tree before visual streaming");
            Check(flight.Vessel.Fate == VesselFate.Impacted, "tree impact damages vessel");
            GD.Print($"Cold-cell collision scenario: {timer.Elapsed.TotalMilliseconds:F2} ms");
            GD.Print($"Flight terrain: {Passed} checks passed");
            GetTree().Quit();
        } catch (Exception error) {
            Fail(this, error);
        }
    }
}
