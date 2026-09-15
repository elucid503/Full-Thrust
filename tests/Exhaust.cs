using FullThrust.Sim;

namespace FullThrust.Sim.Tests;

public static partial class Program {

    private static void ExhaustTerrain(Terrain terrain) {

        CelestialBody body = new CelestialBody { Radius = Home.Radius, Terrain = terrain, RotationPeriodSeconds = Home.RotationPeriodSeconds };
        foreach (var site in new[] { (Latitude: 28.0, Longitude: 86.9, Water: false), (Latitude: 30.0, Longitude: -40.0, Water: true) }) {

            Vector3d up = Site(site.Latitude, site.Longitude);
            double radius = terrain.SurfaceRadius(up);
            var hit = ExhaustInteraction.Surface(body, up * (radius + 12.0), -up, 30.0, 0.0);
            Expect("exhaust contacts surveyed terrain or sea level", hit.HasValue, "no contact");
            if (hit.HasValue) {

                Near("exhaust hit follows terrain elevation", hit.Value.Distance, 12.0, 0.001);
                Expect("surface contact selects dust or steam", hit.Value.Water == site.Water, $"water={hit.Value.Water}");
                Expect("terrain contact normal faces outward", Vector3d.Dot(hit.Value.Normal, up) > 0.5, "inward normal");

            }

        }

    }

    private static void ExhaustContacts() {

        Section("exhaust momentum and surface contact");
        Vessel target = new Vessel("target", new[] { Aegis.BuildStage(0.0) });
        target.Position = new Vector3d(1_500_000.0, 0.0, 0.0);
        Vector3d nozzle = target.Position + Vector3d.UnitZ * 10.0;
        var emitter = new ExhaustInteraction.Emitter(nozzle, -Vector3d.UnitZ, 0.5, 50.0, 0.0, 61_000.0);
        ExhaustInteraction.Hit? Plane(Vector3d origin, Vector3d direction, double reach) {

            double distance = (target.Position.Z - origin.Z) / direction.Z;
            return new ExhaustInteraction.Hit(target, origin + direction * distance, distance);

        }

        ExhaustInteraction.Accumulate(emitter, Plane);
        Close("intercepted momentum pushes away from the nozzle", target.ExhaustForce, -Vector3d.UnitZ * 7_320.0, 1.0e-7);
        Expect("impingement transfers a small bounded share of thrust", target.ExhaustForce.Length / emitter.Thrust is > 0.05 and < 0.20, $"{target.ExhaustForce.Length} N");

        target.ExhaustForce = Vector3d.Zero;
        target.ExhaustTorque = Vector3d.Zero;
        ExhaustInteraction.Accumulate(emitter with { Position = nozzle + Vector3d.UnitX * 3.0 }, Plane);
        Expect("off-centre exhaust generates bounded target torque", target.ExhaustTorque.Y is > 20_000.0 and < 24_000.0, $"got {target.ExhaustTorque.Y}");
        Vector3d velocity = target.Velocity;
        Integrator.Step(target, new CelestialBody { Radius = 100.0, Mu = 0.0 }, 0.01);
        Expect("integrator applies exhaust acceleration", target.Velocity.Z < velocity.Z, $"got {target.Velocity.Z}");
        Expect("integrator applies exhaust angular acceleration", target.AngularVelocity.Y > 0.0, $"got {target.AngularVelocity.Y}");

        target.ExhaustForce = Vector3d.Zero;
        target.ExhaustTorque = Vector3d.Zero;
        ExhaustInteraction.Accumulate(emitter with { Thrust = 0.0 }, Plane);
        Close("shutdown transfers no momentum", target.ExhaustForce, Vector3d.Zero, 0.0);
        ExhaustInteraction.Accumulate(emitter with { Length = 5.0 }, Plane);
        Close("surfaces beyond plume reach receive no force", target.ExhaustForce, Vector3d.Zero, 0.0);
        ExhaustInteraction.Accumulate(emitter, (_, _, _) => null);
        Close("a missed vehicle receives no force", target.ExhaustForce, Vector3d.Zero, 0.0);
        target.Fate = VesselFate.Impacted;
        ExhaustInteraction.Accumulate(emitter, Plane);
        Close("a retired wreck receives no flight load", target.ExhaustForce, Vector3d.Zero, 0.0);

        CelestialBody body = new CelestialBody { Radius = 1000.0, RotationPeriodSeconds = 1000.0 };
        var hit = ExhaustInteraction.Surface(body, Vector3d.UnitX * 1010.0, -Vector3d.UnitX, 20.0, 0.0);
        Expect("downward exhaust reaches the surface", hit.HasValue, "no hit");
        if (hit.HasValue) {

            Near("surface hit is at the actual distance", hit.Value.Distance, 10.0, 0.001);
            Close("surface normal faces away from the planet", hit.Value.Normal, Vector3d.UnitX, 0.001);
            Expect("a body without ocean data produces dust", !hit.Value.Water, "water selected");

        }

        Expect("upward exhaust does not hit ground", !ExhaustInteraction.Surface(body, Vector3d.UnitX * 1010.0, Vector3d.UnitX, 20.0, 0.0).HasValue, "unexpected hit");
        Expect("short plume does not reach ground", !ExhaustInteraction.Surface(body, Vector3d.UnitX * 1010.0, -Vector3d.UnitX, 5.0, 0.0).HasValue, "unexpected hit");

    }

}
