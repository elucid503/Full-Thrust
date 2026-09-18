using FullThrust.Sim;

namespace FullThrust.Sim.Tests;

public static partial class Program {

    private static void EngineDynamics() {

        Section("engine lifecycle, actuators and plume flow");
        EngineLimits unrestricted = new();
        EngineState engine = new();
        engine.Advance(0.01, unrestricted, 2.0, true);
        Near("current engines can run below one percent", engine.Power, 0.01, 1e-9);
        engine.Advance(1.0, unrestricted, 2.0, true);
        engine.Advance(0.0, unrestricted, 0.06, true);
        Expect("shutdown cuts chamber pressure before purge", engine.Power < 0.04 && engine.Burnoff > 0.0, $"{engine.Power}");
        engine.Advance(0.0, unrestricted, 0.16, true);
        Expect("purge remains after useful thrust ends", engine.Power == 0.0 && engine.Purge > 0.9, $"{engine.Purge}");
        engine.Advance(0.0, unrestricted, 1.0, true);
        Expect("shutdown has a finite end", engine.Phase == EnginePhase.Off && engine.Purge == 0.0, engine.Phase.ToString());
        for (int i = 0; i < 10; i++) {

            engine.Advance(1.0, unrestricted, 1.0, true);
            engine.Advance(0.0, unrestricted, 1.0, true);

        }
        Expect("default restart count is unlimited", engine.Ignitions == 11, $"{engine.Ignitions}");
        EngineLimits limited = new() { MinimumThrottle = 0.3, IgnitionDelaySeconds = 0.5, RestartLimit = 0 };
        EngineState future = new();
        future.Advance(0.1, limited, 0.4, true);
        Near("ignition delay consumes no chamber flow", future.Power, 0.0, 0.0);
        future.Advance(0.1, limited, 2.0, true);
        Near("optional minimum throttle is enforced", future.Power, 0.3, 1e-8);
        future.Advance(0.0, limited, 1.0, true);
        future.Advance(1.0, limited, 2.0, true);
        Expect("optional restart limit prevents another ignition", future.Phase == EnginePhase.Exhausted && future.Power == 0.0, future.Phase.ToString());
        EngineState coarse = new();
        EngineState fine = new();
        coarse.Traverse(Vector3d.UnitX * 0.1, 0.1, 0.12, 0.12);
        for (int i = 0; i < 12; i++) { fine.Traverse(Vector3d.UnitX * 0.1, 0.1, 0.12, 0.01); }
        Near("gimbal approaches exponentially", coarse.Gimbal.X, 0.1 * (1.0 - Math.Exp(-1.0)), 1e-12);
        Close("gimbal traversal is independent of step size", coarse.Gimbal, fine.Gimbal, 1e-12);
        fine.Traverse(Vector3d.UnitX, 0.02, 0.12, 1.0);
        Expect("gimbal stays inside mechanical limit", fine.Gimbal.Length <= 0.0200001, $"{fine.Gimbal.Length}");

        Vessel vessel = new("booster", new[] { Zenith.BuildStage() }) { Throttle = 1.0 };
        CelestialBody vacuum = new() { Radius = 1000.0 };
        vessel.AdvanceEngines(vacuum, 2.0);
        Near("balanced cluster retains vacuum rating", vessel.CurrentThrust, Zenith.ThrustNewtons, 0.001);
        Near("balanced engines introduce no torque", vessel.EngineTorque.Length, 0.0, 1e-7);
        vessel.SetEngine(0, false);
        vessel.AdvanceEngines(vacuum, 1.0);
        Near("engine shutdown preserves neighbours' power", vessel.CurrentThrust, Zenith.ThrustNewtons * 5.0 / 6.0, 0.001);
        Expect("an outer engine shutdown creates a real moment", vessel.EngineTorque.Length > 100000.0, $"{vessel.EngineTorque.Length}");
        vessel.SetEngine(0, true);
        vessel.AdvanceEngines(vacuum, 2.0);
        vessel.ControlTorque = Vector3d.UnitX * vessel.GimbalTorqueLimit * 0.5;
        vessel.AdvanceEngines(vacuum, 0.2);
        Expect("gimbal deflection changes force and torque together", vessel.EngineForce.Y > 0.0 && vessel.EngineTorque.X > 0.0, $"{vessel.EngineForce} / {vessel.EngineTorque}");
        double vacuumFlow = vessel.CurrentMassFlow;
        vessel.Position = Vector3d.UnitX * Home.Radius;
        vessel.AdvanceEngines(Home, 2.0);
        Expect("ambient pressure reduces thrust", vessel.CurrentThrust < Zenith.ThrustNewtons && vessel.CurrentThrust > Zenith.ThrustNewtons * 0.7, $"{vessel.CurrentThrust}");
        Near("ambient pressure does not change commanded propellant flow", vessel.CurrentMassFlow, vacuumFlow, 1e-7);

        Vessel launch = Stack.Build();
        launch.Position = Vector3d.UnitX * Home.Radius;
        launch.Throttle = 1.0;
        launch.AdvanceEngines(Home, 2.0);
        double launchTwr = launch.CurrentThrust / (launch.Mass * Home.SurfaceGravity);
        Expect("full stack retains comfortable launch authority", launchTwr > 1.15 && launchTwr < 1.8, $"TWR={launchTwr:F3}");
        Console.WriteLine($"  engine calibration: launch TWR {launchTwr:F3}, sea-level thrust {launch.CurrentThrust / 1000.0:F1} kN, vacuum {launch.ThrustNewtons / 1000.0:F1} kN");
        Vessel starved = new("starved", new[] { Zenith.BuildStage() }) { Position = Vector3d.UnitX * 1500000.0, Throttle = 1.0 };
        starved.AdvanceEngines(vacuum, 2.0);
        starved.PropellantMass = 0.001;
        starved.RecomputeMassProperties();
        double availableImpulse = starved.PropellantMass * starved.SpecificImpulse * Vessel.StandardGravity;
        double mass = starved.Mass;
        Integrator.Step(starved, vacuum, 0.01);
        Expect("last fuel cannot deliver a full-step impulse", starved.Velocity.Length * mass < availableImpulse * 1.01, $"impulse={starved.Velocity.Length * mass}");
        Near("burnout leaves no propellant flow", starved.CurrentMassFlow, 0.0, 0.0);
        Vessel ascent = Stack.Build();
        ascent.Position = Vector3d.UnitX * (Home.Radius + 100.0);
        ascent.Velocity = Home.AirVelocityAt(ascent.Position);
        ascent.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, Vector3d.UnitX);
        ascent.Throttle = 1.0;
        Autopilot launchPilot = new() { Hold = AttitudeHold.Ascent };
        for (int i = 0; i < 4800; i++) {

            launchPilot.Update(ascent, 1.0 / 240.0);
            Integrator.Step(ascent, Home, 1.0 / 240.0, i / 240.0);

        }
        Expect("full stack climbs under the physical actuator model", Home.AltitudeOf(ascent.Position) > 500.0 && ascent.AngularVelocity.Length < 0.03,
            $"altitude={Home.AltitudeOf(ascent.Position):F1} rate={ascent.AngularVelocity.Length:F4}");
        EngineState original = ascent.Active.EngineStates[0];
        Vessel separated = ascent.Separate();
        Expect("separation preserves the chamber and its history", ReferenceEquals(original, separated.Active.EngineStates[0]) && original.Ignitions == 1, $"ignitions={original.Ignitions}");
        separated.AdvanceEngines(Home, 0.23);
        Expect("a separated burning stage shuts down and purges", separated.CurrentThrust == 0.0 && original.Purge > 0.0, $"phase={original.Phase}");
        launch.Fate = VesselFate.Impacted;
        Near("a wreck cannot keep producing thrust", launch.CurrentThrust, 0.0, 0.0);
        Near("a wreck cannot keep consuming engine propellant", launch.CurrentMassFlow, 0.0, 0.0);
        EngineState rcsValve = new();
        rcsValve.Advance(0.5, unrestricted, 0.06, true, true);
        Expect("RCS valve reaches working pressure promptly", rcsValve.Power > 0.49, $"{rcsValve.Power}");
        rcsValve.Advance(0.0, unrestricted, 0.10, true, true);
        Near("RCS cutoff ends chamber output", rcsValve.Power, 0.0, 0.0);

        Vessel upper = new("upper", new[] { Meridian.BuildStage(0.0) }) { Throttle = 1.0 };
        upper.AdvanceEngines(vacuum, 2.0);
        upper.ControlTorque = Vector3d.UnitZ * upper.ThrusterTorqueLimit;
        upper.AdvanceActuators(0.01);
        Near("axial main engine leaves roll to RCS", upper.AppliedRcsTorque.Z, upper.ThrusterTorqueLimit, 1e-7);
        Near("RCS roll is charged to the propellant bottle", upper.RcsDuty, 1.0 / Math.Sqrt(3.0), 1e-7);

        Vessel steered = new("turn", new[] { Zenith.BuildStage() }) {

            Position = Vector3d.UnitX * 1500000.0, Throttle = 1.0,

        };
        steered.AdvanceEngines(vacuum, 1.0);
        Autopilot pilot = new() { Hold = AttitudeHold.Maneuver, ManeuverDirection = new Vector3d(0.0, 0.2, 1.0).Normalized };
        for (int i = 0; i < 4800; i++) {

            pilot.Update(steered, 1.0 / 240.0);
            Integrator.Step(steered, vacuum, 1.0 / 240.0);

        }
        Expect("autopilot settles with physical gimbal lag", steered.AngularVelocity.Length < 0.015 && Vector3d.Dot(steered.Nose, pilot.ManeuverDirection) > 0.999,
            $"rate={steered.AngularVelocity.Length} alignment={Vector3d.Dot(steered.Nose, pilot.ManeuverDirection)}");

    }

}
