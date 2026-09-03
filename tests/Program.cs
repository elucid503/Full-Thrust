using FullThrust.Sim;

namespace FullThrust.Sim.Tests;

public static class Program {

    private static int _checks;
    private static int _failures;

    private static readonly CelestialBody Home = BodyCatalog.Home;

    public static int Main() {

        WorldConstants();
        VectorAlgebra();
        QuaternionRotation();
        OrbitRoundTrip();
        CircularOrbitIsCircular();
        PeriodClosesTheOrbit();
        VisViva();
        IntegratorMatchesKepler();
        WarpPreservesTheOrbit();
        ProgradeBurnRaisesApoapsis();
        PropellantAndDeltaV();
        RigidBodyRotation();
        HullMassProperties();
        MeridianStage();
        AttitudeHolds();

        Console.WriteLine();
        Console.WriteLine($"{_checks - _failures}/{_checks} checks passed");

        return _failures > 0 ? 1 : 0;

    }

    private static void WorldConstants() {

        Section("world constants");

        Near("planet radius", Home.Radius, 1274200.0, 1e-6);
        Near("surface gravity", Home.SurfaceGravity, 9.81, 1e-4);
        Near("circular velocity at surface", Home.CircularVelocityAtSurface, 3535.7, 0.5);
        Near("escape velocity at surface", Home.EscapeVelocityAtSurface, 5000.0, 1.0);
        Near("circular velocity at 70 km", Home.CircularVelocityAt(70000.0), 3442.2, 0.5);

        Orbit low = CircularOrbit(70000.0, 0.0);

        Near("period at 70 km", low.Period, 2453.6, 2.0);

    }

    private static void VectorAlgebra() {

        Section("vector algebra");

        Close("cross of unit axes", Vector3d.Cross(Vector3d.UnitX, Vector3d.UnitY), Vector3d.UnitZ, 1e-15);
        Near("dot of perpendicular axes", Vector3d.Dot(Vector3d.UnitX, Vector3d.UnitY), 0.0, 1e-15);
        Near("length of 3-4-5 triangle", new Vector3d(3.0, 4.0, 0.0).Length, 5.0, 1e-15);
        Near("normalized length", new Vector3d(7.0, -3.0, 2.0).Normalized.Length, 1.0, 1e-15);
        Close("normalizing zero is safe", Vector3d.Zero.Normalized, Vector3d.Zero, 0.0);

    }

    private static void QuaternionRotation() {

        Section("quaternion rotation");

        QuaternionD quarterAboutX = QuaternionD.FromAxisAngle(Vector3d.UnitX, Math.PI * 0.5);

        Close("quarter turn about x", quarterAboutX.Rotate(Vector3d.UnitZ), -Vector3d.UnitY, 1e-14);

        QuaternionD composed = QuaternionD.FromAxisAngle(Vector3d.UnitZ, Math.PI * 0.5) * quarterAboutX;

        Close("composition applies right factor first", composed.Rotate(Vector3d.UnitZ), Vector3d.UnitX, 1e-14);

        Close("shortest arc between opposing axes", QuaternionD.FromTo(Vector3d.UnitZ, -Vector3d.UnitZ).Rotate(Vector3d.UnitZ), -Vector3d.UnitZ, 1e-12);
        Close("shortest arc onto a new axis", QuaternionD.FromTo(Vector3d.UnitZ, Vector3d.UnitX).Rotate(Vector3d.UnitZ), Vector3d.UnitX, 1e-14);

        QuaternionD spun = QuaternionD.Identity;

        Vector3d rate = new Vector3d(0.0, 0.0, 0.4);

        int steps = 200000;
        double dt = Math.PI * 2.0 / 0.4 / steps;

        for (int step = 0; step < steps; step++) {

            spun = QuaternionD.Integrate(spun, rate, dt);

        }

        Close("full revolution returns to start", spun.Rotate(Vector3d.UnitX), Vector3d.UnitX, 1e-8);

    }

    private static void OrbitRoundTrip() {

        Section("orbit round trip");

        RoundTrip("circular equatorial", StateOnCircularOrbit(70000.0, 0.0));
        RoundTrip("circular inclined", StateOnCircularOrbit(70000.0, 0.9));
        RoundTrip("elliptical inclined", (new Vector3d(1344200.0, 0.0, 0.0), Rotated(new Vector3d(0.0, 3960.0, 0.0), 0.6)));
        RoundTrip("retrograde", (new Vector3d(1344200.0, 0.0, 0.0), new Vector3d(0.0, -3442.2, 0.0)));
        RoundTrip("eccentric and tilted", (new Vector3d(1500000.0, 200000.0, -90000.0), new Vector3d(-400.0, 3100.0, 900.0)));
        RoundTrip("hyperbolic", (new Vector3d(1474200.0, 0.0, 0.0), new Vector3d(500.0, 4800.0, 1200.0)));

    }

    private static void CircularOrbitIsCircular() {

        Section("circular orbit stays circular");

        Orbit orbit = CircularOrbit(70000.0, 0.7);

        double radius = 1344200.0;
        double speed = Home.CircularVelocityAt(70000.0);

        double worstRadius = 0.0;
        double worstSpeed = 0.0;

        for (int sample = 0; sample <= 600; sample++) {

            (Vector3d position, Vector3d velocity) = orbit.StateAt(orbit.Period * 3.0 * sample / 600.0);

            worstRadius = Math.Max(worstRadius, Math.Abs(position.Length - radius));
            worstSpeed = Math.Max(worstSpeed, Math.Abs(velocity.Length - speed));

        }

        Near("radius held over three orbits", worstRadius, 0.0, 1e-2);
        Near("speed held over three orbits", worstSpeed, 0.0, 1e-6);

    }

    private static void PeriodClosesTheOrbit() {

        Section("period closes the orbit");

        Orbit orbit = Orbit.FromStateVectors(new Vector3d(1344200.0, 0.0, 0.0), Rotated(new Vector3d(0.0, 3900.0, 0.0), 0.45), Home.Mu, 0.0);

        (Vector3d startPosition, Vector3d startVelocity) = orbit.StateAt(0.0);
        (Vector3d endPosition, Vector3d endVelocity) = orbit.StateAt(orbit.Period);

        Close("position after one period", endPosition, startPosition, 1e-6);
        Close("velocity after one period", endVelocity, startVelocity, 1e-9);

    }

    private static void VisViva() {

        Section("vis-viva");

        Orbit orbit = Orbit.FromStateVectors(new Vector3d(1344200.0, 0.0, 0.0), Rotated(new Vector3d(0.0, 3900.0, 0.0), 0.45), Home.Mu, 0.0);

        double worst = 0.0;

        for (int sample = 0; sample <= 400; sample++) {

            (Vector3d position, Vector3d velocity) = orbit.StateAt(orbit.Period * sample / 400.0);

            double expected = orbit.SpeedAt(position.Length);

            worst = Math.Max(worst, Math.Abs(velocity.Length - expected) / expected);

        }

        Near("speed matches vis-viva everywhere", worst, 0.0, 1e-12);

    }

    private static void IntegratorMatchesKepler() {

        Section("integrator matches kepler");

        (Vector3d position, Vector3d velocity) = (new Vector3d(1344200.0, 0.0, 0.0), Rotated(new Vector3d(0.0, 3900.0, 0.0), 0.45));

        Vessel vessel = Coaster(position, velocity);

        Orbit reference = Orbit.FromStateVectors(position, velocity, Home.Mu, 0.0);

        double energyBefore = SpecificEnergy(vessel);

        double dt = 0.5;
        int steps = (int)Math.Round(reference.Period / dt);

        for (int step = 0; step < steps; step++) {

            Integrator.Step(vessel, Home, dt);

        }

        (Vector3d expectedPosition, Vector3d expectedVelocity) = reference.StateAt(steps * dt);

        Close("integrated position after one orbit", vessel.Position, expectedPosition, 1.0);
        Close("integrated velocity after one orbit", vessel.Velocity, expectedVelocity, 1e-3);

        double energyAfter = SpecificEnergy(vessel);

        Near("specific energy conserved", Math.Abs(energyAfter - energyBefore) / Math.Abs(energyBefore), 0.0, 1e-10);

    }

    private static void WarpPreservesTheOrbit() {

        Section("warp preserves the orbit");

        Orbit before = Orbit.FromStateVectors(new Vector3d(1344200.0, 0.0, 0.0), Rotated(new Vector3d(0.0, 3900.0, 0.0), 0.45), Home.Mu, 0.0);

        (Vector3d position, Vector3d velocity) = before.StateAt(3600.0);

        Orbit after = Orbit.FromStateVectors(position, velocity, Home.Mu, 3600.0);

        Near("semi-major axis unchanged", after.SemiMajorAxis, before.SemiMajorAxis, 1e-6);
        Near("eccentricity unchanged", after.Eccentricity, before.Eccentricity, 1e-12);
        Near("inclination unchanged", after.Inclination, before.Inclination, 1e-12);
        Near("apoapsis unchanged", after.ApoapsisRadius, before.ApoapsisRadius, 1e-6);
        Near("periapsis unchanged", after.PeriapsisRadius, before.PeriapsisRadius, 1e-6);

    }

    private static void ProgradeBurnRaisesApoapsis() {

        Section("prograde burn raises apoapsis");

        double radius = 1344200.0;
        double circular = Home.CircularVelocityAt(70000.0);

        Vector3d position = new Vector3d(radius, 0.0, 0.0);
        Vector3d velocity = new Vector3d(0.0, circular + 200.0, 0.0);

        Orbit orbit = Orbit.FromStateVectors(position, velocity, Home.Mu, 0.0);

        double energy = velocity.LengthSquared * 0.5 - Home.Mu / radius;
        double momentum = Vector3d.Cross(position, velocity).Length;

        double expectedAxis = -Home.Mu / (2.0 * energy);
        double expectedEccentricity = Math.Sqrt(1.0 + 2.0 * energy * momentum * momentum / (Home.Mu * Home.Mu));

        Near("semi-major axis", orbit.SemiMajorAxis, expectedAxis, 1e-6);
        Near("eccentricity", orbit.Eccentricity, expectedEccentricity, 1e-12);
        Near("periapsis stays at the burn point", orbit.PeriapsisRadius, radius, 1e-6);

        Expect("apoapsis is raised", orbit.ApoapsisRadius > radius + 100000.0, $"apoapsis {orbit.ApoapsisRadius:F0} m");

    }

    private static void PropellantAndDeltaV() {

        Section("propellant and delta-v");

        Vessel vessel = new Vessel {

            Name = "test",

            Position = new Vector3d(1344200.0, 0.0, 0.0),
            Velocity = new Vector3d(0.0, 3442.2, 0.0),

            DryMass = 2000.0,
            PropellantMass = 8000.0,

            ThrustNewtons = 200000.0,
            SpecificImpulse = 320.0,

            Inertia = new Vector3d(5000.0, 5000.0, 2000.0),

        };

        double expectedDeltaV = 320.0 * Vessel.StandardGravity * Math.Log(5.0);

        Near("rocket equation", vessel.DeltaV, expectedDeltaV, 1e-9);
        Near("mass flow rate", vessel.MassFlowRate, 200000.0 / (320.0 * Vessel.StandardGravity), 1e-9);

        double expectedBurn = 8000.0 / vessel.MassFlowRate;

        vessel.Throttle = 1.0;

        double dt = 0.01;
        double elapsed = 0.0;

        while (vessel.PropellantMass > 0.0 && elapsed < expectedBurn * 2.0) {

            Integrator.Step(vessel, Home, dt);

            elapsed += dt;

        }

        Near("burn time matches mass flow", elapsed, expectedBurn, dt * 2.0);
        Near("delta-v is spent", vessel.DeltaV, 0.0, 1e-12);

    }

    private static void HullMassProperties() {

        Section("hull mass properties");

        Hull.Station[] cylinder = {

            new Hull.Station(0.0, 2.0),
            new Hull.Station(6.0, 2.0),

        };

        Hull hull = new Hull(cylinder, 0.0, 6.0);

        Near("cylinder volume", hull.Volume, Math.PI * 4.0 * 6.0, 1e-9);
        Near("cylinder length", hull.Length, 6.0, 1e-12);
        Near("cylinder max radius", hull.MaxRadius, 2.0, 1e-12);

        MassProperties shell = hull.Structure(120.0);

        Near("shell centre of mass", shell.CentreZ, 3.0, 1e-9);
        Near("shell axial moment", shell.Inertia.Z, 120.0 * 4.0, 1e-6);
        Near("shell transverse moment", shell.Inertia.X, 120.0 * (4.0 / 2.0 + 36.0 / 12.0), 1e-3);

        MassProperties full = hull.Propellant(500.0, 1.0);

        Near("full column centre of mass", full.CentreZ, 3.0, 1e-6);
        Near("full column axial moment", full.Inertia.Z, 500.0 * 4.0 / 2.0, 1e-6);
        Near("full column transverse moment", full.Inertia.X, 500.0 * (4.0 / 4.0 + 36.0 / 12.0), 1e-3);

        MassProperties half = hull.Propellant(250.0, 0.5);

        Near("half column sits low", half.CentreZ, 1.5, 1e-4);
        Near("half column transverse moment", half.Inertia.X, 250.0 * (4.0 / 4.0 + 9.0 / 12.0), 1e-3);

        MassProperties stacked = MassProperties.Combine(new MassProperties(2.0, -1.0, Vector3d.Zero), new MassProperties(2.0, 1.0, Vector3d.Zero));

        Near("combined centre of mass", stacked.CentreZ, 0.0, 1e-12);
        Near("combined transverse moment uses parallel axis", stacked.Inertia.X, 4.0, 1e-12);

        Hull cone = new Hull(new[] { new Hull.Station(0.0, 0.0), new Hull.Station(3.0, 1.0) }, 0.0, 3.0);

        Near("cone volume", cone.Volume, Math.PI * 1.0 * 3.0 / 3.0, 1e-4);
        Near("cone centre of mass", cone.Propellant(10.0, 1.0).CentreZ, 2.25, 1e-3);

    }

    private static void MeridianStage() {

        Section("meridian stage");

        Vessel vessel = Meridian.Build();

        Near("hull length", vessel.Hull.Length, Meridian.OverallLength, 1e-12);
        Near("tank capacity", vessel.PropellantCapacity, vessel.Hull.TankVolume * Meridian.PropellantDensity, 1e-9);

        Near("nose closes on the capsule deck", vessel.Hull.RadiusAt(Meridian.OverallLength), Meridian.DeckRadius, 1e-9);
        Expect("centre of mass lies inside the hull", vessel.CentreOfMassZ > 0.0 && vessel.CentreOfMassZ < Meridian.OverallLength, $"centre {vessel.CentreOfMassZ:F3} m");
        Expect("stage is slender", vessel.Inertia.X > vessel.Inertia.Z * 4.0, $"transverse {vessel.Inertia.X:F0}, axial {vessel.Inertia.Z:F0}");

        double loadedCentre = vessel.CentreOfMassZ;

        vessel.PropellantMass = 0.0;
        vessel.RecomputeMassProperties();

        Expect("burning off propellant moves the centre of mass forward", vessel.CentreOfMassZ > loadedCentre, $"{loadedCentre:F3} m to {vessel.CentreOfMassZ:F3} m");
        Near("empty stage inertia is the structure alone", vessel.Inertia.Z, vessel.Hull.Structure(Meridian.DryMass).Inertia.Z, 1e-6);

    }

    private static void RigidBodyRotation() {

        Section("rigid body rotation");

        Vessel spinning = Coaster(new Vector3d(1344200.0, 0.0, 0.0), new Vector3d(0.0, 3442.2, 0.0));

        spinning.Inertia = new Vector3d(5000.0, 5000.0, 2000.0);
        spinning.AngularVelocity = new Vector3d(0.0, 0.0, 0.1);

        for (int step = 0; step < 10000; step++) {

            Integrator.Step(spinning, Home, 0.001);

        }

        Close("torque-free axial spin is constant", spinning.AngularVelocity, new Vector3d(0.0, 0.0, 0.1), 1e-12);

        Vessel torqued = Coaster(new Vector3d(1344200.0, 0.0, 0.0), new Vector3d(0.0, 3442.2, 0.0));

        torqued.Inertia = new Vector3d(5000.0, 5000.0, 2000.0);
        torqued.ControlTorque = new Vector3d(0.0, 0.0, 100.0);

        for (int step = 0; step < 10000; step++) {

            Integrator.Step(torqued, Home, 0.001);

        }

        Near("torque spins the roll axis up", torqued.AngularVelocity.Z, 100.0 / 2000.0 * 10.0, 1e-9);

        Vessel tumbling = Coaster(new Vector3d(1344200.0, 0.0, 0.0), new Vector3d(0.0, 3442.2, 0.0));

        tumbling.Inertia = new Vector3d(3000.0, 5000.0, 2000.0);
        tumbling.AngularVelocity = new Vector3d(0.3, 0.2, 0.5);

        double momentumBefore = AngularMomentum(tumbling).Length;

        for (int step = 0; step < 20000; step++) {

            Integrator.Step(tumbling, Home, 0.0005);

        }

        double momentumAfter = AngularMomentum(tumbling).Length;

        Near("angular momentum conserved while tumbling", Math.Abs(momentumAfter - momentumBefore) / momentumBefore, 0.0, 1e-3);

    }

    private static Vessel Coaster(Vector3d position, Vector3d velocity) {

        return new Vessel {

            Name = "coaster",

            Position = position,
            Velocity = velocity,

            DryMass = 1000.0,
            PropellantMass = 0.0,

            ThrustNewtons = 0.0,
            SpecificImpulse = 300.0,

            Inertia = new Vector3d(1000.0, 1000.0, 400.0),

        };

    }

    private static (Vector3d Position, Vector3d Velocity) StateOnCircularOrbit(double altitude, double inclination) {

        double radius = Home.Radius + altitude;

        return (new Vector3d(radius, 0.0, 0.0), Rotated(new Vector3d(0.0, Home.CircularVelocityAt(altitude), 0.0), inclination));

    }

    private static Orbit CircularOrbit(double altitude, double inclination) {

        (Vector3d position, Vector3d velocity) = StateOnCircularOrbit(altitude, inclination);

        return Orbit.FromStateVectors(position, velocity, Home.Mu, 0.0);

    }

    private static Vector3d Rotated(Vector3d value, double inclination) => QuaternionD.FromAxisAngle(Vector3d.UnitX, inclination).Rotate(value);

    private static double SpecificEnergy(Vessel vessel) => vessel.Velocity.LengthSquared * 0.5 - Home.Mu / vessel.Position.Length;

    private static Vector3d AngularMomentum(Vessel vessel) {

        Vector3d rate = vessel.AngularVelocity;
        Vector3d inertia = vessel.Inertia;

        return vessel.Orientation.Rotate(new Vector3d(inertia.X * rate.X, inertia.Y * rate.Y, inertia.Z * rate.Z));

    }

    private static void RoundTrip(string name, (Vector3d Position, Vector3d Velocity) state) {

        Orbit orbit = Orbit.FromStateVectors(state.Position, state.Velocity, Home.Mu, 0.0);

        (Vector3d position, Vector3d velocity) = orbit.StateAt(0.0);

        Close($"{name}: position", position, state.Position, state.Position.Length * 1e-9);
        Close($"{name}: velocity", velocity, state.Velocity, state.Velocity.Length * 1e-8);

    }

    private static void AttitudeHolds() {

        Section("attitude holds");

        Vessel vessel = Meridian.Build();

        double radius = Home.Radius + 200000.0;
        double speed = Home.CircularVelocityAt(200000.0);

        vessel.Position = new Vector3d(radius, 0.0, 0.0);
        vessel.Velocity = new Vector3d(0.0, speed * 0.8, speed * 0.6);

        Vector3d prograde = Autopilot.Reference(AttitudeHold.Prograde, vessel.Position, vessel.Velocity);
        Vector3d normal = Autopilot.Reference(AttitudeHold.Normal, vessel.Position, vessel.Velocity);
        Vector3d radial = Autopilot.Reference(AttitudeHold.RadialOut, vessel.Position, vessel.Velocity);

        Near("prograde is a unit vector", prograde.Length, 1.0, 1e-12);
        Near("the reference triad is orthogonal", Vector3d.Dot(prograde, normal) + Vector3d.Dot(normal, radial) + Vector3d.Dot(radial, prograde), 0.0, 1e-12);
        Near("radial points away from the body", Vector3d.Dot(radial, vessel.Position.Normalized), 1.0, 1e-12);
        Close("retrograde opposes prograde", Autopilot.Reference(AttitudeHold.Retrograde, vessel.Position, vessel.Velocity), -prograde, 1e-12);

        Autopilot autopilot = new Autopilot { MaxTorque = Meridian.ControlTorque, Hold = AttitudeHold.Prograde };

        vessel.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, -prograde);

        double step = 1.0 / 60.0;

        for (int tick = 0; tick < 60 * 90; tick++) {

            autopilot.Update(vessel, step);
            Integrator.StepAttitude(vessel, step);

        }

        Near("a prograde hold converges from a full reversal", Vector3d.Angle(vessel.Nose, prograde), 0.0, 0.01);
        Near("and settles rather than oscillating", vessel.AngularVelocity.Length, 0.0, 1e-3);

        autopilot.ManualCommand = Vector3d.UnitX;
        autopilot.Update(vessel, step);

        Expect("pilot input drops the hold", autopilot.Hold == AttitudeHold.Off, $"hold is {autopilot.Hold}");
        Near("and commands full torque", vessel.ControlTorque.X, Meridian.ControlTorque, 1e-9);

        autopilot.ManualCommand = Vector3d.Zero;

        vessel.AngularVelocity = new Vector3d(0.0, 0.2, -0.15);
        autopilot.Hold = AttitudeHold.Stability;

        for (int tick = 0; tick < 60 * 60; tick++) {

            autopilot.Update(vessel, step);
            Integrator.StepAttitude(vessel, step);

        }

        Near("a stability hold kills the rotation", vessel.AngularVelocity.Length, 0.0, 1e-3);

    }

    private static void Section(string title) {

        Console.WriteLine();
        Console.WriteLine(title);

    }

    private static void Expect(string name, bool condition, string detail) {

        _checks++;

        if (condition) {

            Console.WriteLine($"  pass  {name}");

            return;

        }

        _failures++;

        Console.WriteLine($"  FAIL  {name}  {detail}");

    }

    private static void Near(string name, double actual, double expected, double tolerance) {

        Expect(name, Math.Abs(actual - expected) <= tolerance, $"got {actual:G17}, expected {expected:G17}, tolerance {tolerance:G3}");

    }

    private static void Close(string name, Vector3d actual, Vector3d expected, double tolerance) {

        Expect(name, (actual - expected).Length <= tolerance, $"got {actual}, expected {expected}, off by {(actual - expected).Length:G6}");

    }

}
