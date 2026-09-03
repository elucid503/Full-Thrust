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
