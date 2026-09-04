using FullThrust.Sim;

namespace FullThrust.Sim.Tests;

public static partial class Program {

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
        AegisCapsule();
        AttitudeHolds();
        OrbitGeometry();
        ManeuverNodes();
        ReactionControl();
        AtmosphereColumn();
        AeroGeometry();
        AeroForcesAndMoments();
        SkinHeating();
        Staging();
        Reentry();
        NozzleExpansion();
        VesselContacts();
        GroundSurvey();

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

        foreach ((Vector3d nose, Vector3d dorsal) in new[] {

            (Vector3d.UnitX, Vector3d.UnitZ),
            (Vector3d.UnitZ, Vector3d.UnitY),
            (-Vector3d.UnitZ, Vector3d.UnitX),
            (new Vector3d(0.3, -0.9, 0.31), new Vector3d(-0.5, 0.2, 0.84)),
            (new Vector3d(1.0, 0.0, 0.0), new Vector3d(2.0, 0.0, 0.0)),

        }) {

            QuaternionD aimed = QuaternionD.LookAlong(nose, dorsal);

            Close($"look along {nose} puts the nose on target", aimed.Rotate(Vector3d.UnitZ), nose.Normalized, 1e-12);
            Near($"look along {nose} stays orthonormal", Vector3d.Dot(aimed.Rotate(Vector3d.UnitX), aimed.Rotate(Vector3d.UnitY)), 0.0, 1e-12);
            Near($"look along {nose} keeps handedness", Vector3d.Dot(Vector3d.Cross(aimed.Rotate(Vector3d.UnitX), aimed.Rotate(Vector3d.UnitY)), aimed.Rotate(Vector3d.UnitZ)), 1.0, 1e-12);

            Vector3d wanted = dorsal - nose.Normalized * Vector3d.Dot(dorsal, nose.Normalized);

            if (wanted.LengthSquared > 1e-12) {

                Close($"look along {nose} rolls to the reference", aimed.Rotate(Vector3d.UnitY), wanted.Normalized, 1e-12);

            }

        }

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

        double radius = Home.Radius + Home.AtmosphereTop + 25000.0;
        (Vector3d position, Vector3d velocity) = (new Vector3d(radius, 0.0, 0.0), Rotated(new Vector3d(0.0, 3900.0, 0.0), 0.45));

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

        Vessel vessel = new Vessel("test", new[] { Cylinder("test", 2000.0, 8000.0, 200000.0, 320.0) }) {

            Position = new Vector3d(1344200.0, 0.0, 0.0),
            Velocity = new Vector3d(0.0, 3442.2, 0.0),

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

        Vessel vessel = Stack.Build();

        Stage meridian = vessel.Active;
        Stage capsule = vessel.Forward;

        Near("stack length", vessel.Length, Meridian.PayloadDatum + Aegis.Height, 1e-9);
        Near("tank capacity", vessel.PropellantCapacity, meridian.Hull.TankVolume * Meridian.PropellantDensity, 1e-9);

        Near("the stack closes to a point", vessel.RadiusAt(vessel.Tip), 0.0, 1e-9);
        Near("the adapter carries the tank's diameter", vessel.RadiusAt(Meridian.AdapterBase + 0.05), Meridian.BodyRadius, 1e-6);
        Near("the adapter closes over the shield", vessel.RadiusAt(Meridian.PayloadDatum + 0.05), Meridian.BodyRadius, 1e-6);

        Expect("centre of mass lies inside the stack", vessel.CentreOfMassZ > 0.0 && vessel.CentreOfMassZ < vessel.Tip, $"centre {vessel.CentreOfMassZ:F3} m");
        Expect("the stack is slender", vessel.Inertia.X > vessel.Inertia.Z * 4.0, $"transverse {vessel.Inertia.X:F0}, axial {vessel.Inertia.Z:F0}");

        Near("dry mass is the two stages", vessel.DryMass, Meridian.DryMass + Aegis.DryMass, 1e-9);
        Near("attitude authority is both clusters", vessel.ControlTorqueLimit, Meridian.ControlTorque + Aegis.ControlTorque, 1e-9);

        double loadedCentre = vessel.CentreOfMassZ;

        vessel.PropellantMass = 0.0;
        vessel.RecomputeMassProperties();

        Expect("burning off propellant moves the centre of mass forward", vessel.CentreOfMassZ > loadedCentre, $"{loadedCentre:F3} m to {vessel.CentreOfMassZ:F3} m");

        Near("an empty stage is its shell, its engine and its bottle", meridian.Mass, Meridian.DryMass + Meridian.RcsPropellantMass, 1e-9);
        Near("the capsule carries no bulk propellant", capsule.PropellantMass, 0.0, 1e-12);

    }

    private static void AegisCapsule() {

        Section("aegis capsule");

        Hull hull = Aegis.BuildHull(0.0);

        Near("the shield starts on the axis", hull.RadiusAt(0.0), 0.0, 1e-12);
        Near("the capsule closes to a point", hull.RadiusAt(Aegis.Height), 0.0, 1e-9);

        double widest = 0.0;
        double last = double.NegativeInfinity;

        bool rising = true;

        foreach (Hull.Station station in hull.Stations) {

            rising &= station.Z > last;
            last = station.Z;

            widest = Math.Max(widest, station.Radius);

        }

        Expect("stations run forward", rising, "a station fell back");
        Near("the shoulder is the widest point", widest, Aegis.BaseRadius + 0.001, 0.03);

        // Tangency, checked the way it shows: a crease is a step in the slope, so the slope either
        // side of every join has to agree.
        foreach (double join in new[] { Aegis.WallBase, Aegis.DomeSeat }) {

            double before = (Aegis.RadiusAt(join) - Aegis.RadiusAt(join - 0.002)) / 0.002;
            double after = (Aegis.RadiusAt(join + 0.002) - Aegis.RadiusAt(join)) / 0.002;

            Near($"the profile has no crease at {join:F3} m", after, before, 0.02);

        }

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

        Vessel vessel = new Vessel("coaster", new[] { Cylinder("coaster", 1000.0, 0.0, 0.0, 300.0) }) {

            Position = position,
            Velocity = velocity,

        };

        vessel.Inertia = new Vector3d(1000.0, 1000.0, 400.0);

        return vessel;

    }

    /// <summary>A plain cylindrical stage, so a test can dial mass and thrust without authoring a
    /// mould line for every case.</summary>
    private static Stage Cylinder(string name, double dryMass, double propellant, double thrust, double impulse, double torque = 0.0) {

        Hull hull = new Hull(new[] { new Hull.Station(0.0, 1.0), new Hull.Station(4.0, 1.0) }, 0.4, 3.6);

        return new Stage {

            Name = name,

            Hull = hull,

            ShellMass = dryMass,

            PropellantMass = propellant,
            PropellantCapacity = Math.Max(propellant, 1.0),

            ThrustNewtons = thrust,
            SpecificImpulse = impulse,

            ControlTorque = torque,

            RcsThrustNewtons = torque > 0.0 ? 1000.0 : 0.0,
            RcsPropellantMass = torque > 0.0 ? 100.0 : 0.0,
            RcsPropellantCapacity = torque > 0.0 ? 100.0 : 0.0,

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

        Vessel vessel = Stack.Build();

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

        Autopilot autopilot = new Autopilot { Hold = AttitudeHold.Prograde };

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
        Near("and commands full torque", vessel.ControlTorque.X, Meridian.ControlTorque + Aegis.ControlTorque, 1e-9);

        autopilot.ManualCommand = Vector3d.Zero;

        vessel.AngularVelocity = new Vector3d(0.0, 0.2, -0.15);
        autopilot.Hold = AttitudeHold.Stability;

        for (int tick = 0; tick < 60 * 60; tick++) {

            autopilot.Update(vessel, step);
            Integrator.StepAttitude(vessel, step);

        }

        Near("a stability hold kills the rotation", vessel.AngularVelocity.Length, 0.0, 1e-3);

    }

    private static void OrbitGeometry() {

        Section("orbit geometry");

        Orbit orbit = Orbit.FromStateVectors(new Vector3d(1344200.0, 0.0, 0.0), Rotated(new Vector3d(0.0, 3900.0, 0.0), 0.45), Home.Mu, 0.0);

        double worst = 0.0;

        for (int sample = 0; sample <= 400; sample++) {

            double time = orbit.Period * sample / 400.0;

            (Vector3d state, _) = orbit.StateAt(time);

            Vector3d traced = orbit.PositionAtTrueAnomaly(orbit.TrueAnomalyAt(time));

            worst = Math.Max(worst, (traced - state).Length);

        }

        Near("the traced path follows the propagated state", worst, 0.0, 1e-6);

        Near("periapsis radius from the conic", orbit.RadiusAtTrueAnomaly(0.0), orbit.PeriapsisRadius, 1e-6);
        Near("apoapsis radius from the conic", orbit.RadiusAtTrueAnomaly(Math.PI), orbit.ApoapsisRadius, 1e-6);

        double toApoapsis = orbit.TimeToApoapsis(0.0);

        (Vector3d atApoapsis, _) = orbit.StateAt(toApoapsis);

        Near("time to apoapsis lands on apoapsis", atApoapsis.Length, orbit.ApoapsisRadius, 1e-3);
        Expect("time to apoapsis is within one period", toApoapsis >= 0.0 && toApoapsis <= orbit.Period, $"{toApoapsis:F1} s of {orbit.Period:F1} s");

        (Vector3d atPeriapsis, _) = orbit.StateAt(orbit.TimeToPeriapsis(0.0));

        Near("time to periapsis lands on periapsis", atPeriapsis.Length, orbit.PeriapsisRadius, 1e-3);

        Vector3d ascending = orbit.PositionAtTrueAnomaly(orbit.AscendingNodeTrueAnomaly);
        Vector3d descending = orbit.PositionAtTrueAnomaly(orbit.DescendingNodeTrueAnomaly);

        Near("the ascending node lies in the reference plane", ascending.Z, 0.0, 1e-6);
        Near("the descending node lies in the reference plane", descending.Z, 0.0, 1e-6);

        (_, Vector3d climbing) = orbit.StateAt(orbit.TimeToTrueAnomaly(0.0, orbit.AscendingNodeTrueAnomaly));

        Expect("the vessel is climbing at the ascending node", climbing.Z > 0.0, $"z rate {climbing.Z:F3} m/s");

        Near("the plane normal is a unit vector", orbit.PlaneNormal.Length, 1.0, 1e-12);
        Near("the plane normal carries the inclination", Math.Acos(orbit.PlaneNormal.Z), orbit.Inclination, 1e-12);

        Orbit escape = Orbit.FromStateVectors(new Vector3d(1474200.0, 0.0, 0.0), new Vector3d(500.0, 4800.0, 1200.0), Home.Mu, 0.0);

        Expect("an open conic reports a finite anomaly limit", escape.TrueAnomalyLimit < Math.PI, $"{escape.TrueAnomalyLimit:F3} rad");
        Near("a hyperbolic true anomaly round trips", escape.PositionAtTrueAnomaly(escape.TrueAnomalyAt(600.0)).Length, escape.StateAt(600.0).Position.Length, 1e-3);

    }

    private static void ManeuverNodes() {

        Section("maneuver nodes");

        double altitude = Home.AtmosphereTop + 25000.0;
        double radius = Home.Radius + altitude;
        double circular = Home.CircularVelocityAt(altitude);

        Orbit orbit = Orbit.FromStateVectors(new Vector3d(radius, 0.0, 0.0), new Vector3d(0.0, circular, 0.0), Home.Mu, 0.0);

        Maneuver node = new Maneuver { Time = orbit.Period * 0.5, Prograde = 200.0 };

        Near("delta-v is the magnitude of the components", node.DeltaV, 200.0, 1e-12);

        Orbit raised = node.Result(orbit);

        Near("a prograde burn leaves periapsis at the node", raised.PeriapsisRadius, radius, 1.0);
        Expect("a prograde burn raises apoapsis", raised.ApoapsisRadius > radius + 100000.0, $"apoapsis {raised.ApoapsisRadius - Home.Radius:F0} m");
        Near("and leaves the plane alone", raised.Inclination, orbit.Inclination, 1e-9);

        Maneuver plane = new Maneuver { Time = orbit.Period * 0.25, Normal = 400.0 };

        Orbit tilted = plane.Result(orbit);

        Expect("a normal burn tilts the plane", tilted.Inclination > 0.1, $"inclination {tilted.Inclination:F4} rad");
        Near("and holds the speed", tilted.SpeedAt(radius), Math.Sqrt(circular * circular + 400.0 * 400.0), 1e-6);

        Vessel vessel = Stack.Build();

        double burn = new Maneuver { Prograde = 500.0 }.BurnSeconds(vessel);

        double exhaust = Meridian.SpecificImpulse * Vessel.StandardGravity;
        double expected = vessel.Mass * (1.0 - Math.Exp(-500.0 / exhaust)) / vessel.MassFlowRate;

        Near("burn time follows the rocket equation", burn, expected, 1e-9);
        Expect("a burn beyond the tank is impossible", double.IsInfinity(new Maneuver { Prograde = vessel.DeltaV * 2.0 }.BurnSeconds(vessel)), "reported a finite burn");

        Maneuver centred = new Maneuver { Time = 1000.0, Prograde = 500.0 };

        Near("ignition is half a burn before the node", centred.IgnitionTime(vessel), 1000.0 - burn * 0.5, 1e-9);

        // Flying the impulse as a real burn should land close to the conic the node predicted.
        Vessel flown = Stack.Build();

        (flown.Position, flown.Velocity) = orbit.StateAt(centred.IgnitionTime(flown));

        flown.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, flown.Velocity.Normalized);
        flown.Throttle = 1.0;

        double step = 1.0 / 240.0;
        double elapsed = 0.0;

        while (elapsed < burn) {

            Integrator.Step(flown, Home, step);

            elapsed += step;

        }

        Orbit achieved = flown.OrbitAround(Home, 0.0);
        Orbit predicted = centred.Result(orbit);

        Near("a flown burn matches the predicted apoapsis", achieved.ApoapsisRadius, predicted.ApoapsisRadius, predicted.ApoapsisRadius * 0.02);

    }

    private static void ReactionControl() {

        Section("reaction control");

        Vessel vessel = Stack.Build();

        Near("the split rejoins the tank", vessel.FuelMass + vessel.OxidiserMass, vessel.PropellantMass, 1e-9);
        Near("oxidiser leads at the mixture ratio", vessel.OxidiserMass / vessel.FuelMass, vessel.MixtureRatio, 1e-12);

        Expect("a loaded cluster is live", vessel.HasRcs, "cluster reported dry");

        Autopilot autopilot = new Autopilot();

        vessel.RcsEnabled = false;
        autopilot.ManualCommand = Vector3d.UnitX;
        autopilot.Update(vessel, 1.0 / 60.0);

        Near("a disabled cluster raises no torque", vessel.ControlTorque.Length, 0.0, 1e-12);

        vessel.RcsEnabled = true;
        autopilot.Update(vessel, 1.0 / 60.0);

        Near("an enabled cluster raises full torque", vessel.ControlTorque.X, Meridian.ControlTorque + Aegis.ControlTorque, 1e-9);

        double duty = vessel.RcsDuty;
        double before = vessel.RcsPropellantMass;

        Integrator.StepAttitude(vessel, 1.0);

        Near("firing spends the bottle at the rated flow", before - vessel.RcsPropellantMass, duty * vessel.RcsMassFlowRate, 1e-9);

        vessel.ControlTorque = Vector3d.Zero;
        autopilot.ManualCommand = Vector3d.Zero;

        before = vessel.RcsPropellantMass;

        Integrator.StepAttitude(vessel, 10.0);

        Near("a converged hold spends nothing", vessel.RcsPropellantMass, before, 1e-12);

        vessel.TranslationCommand = Vector3d.UnitZ;
        vessel.Orientation = QuaternionD.Identity;

        Close("translation pushes along the commanded body axis", vessel.RcsForce.Normalized, Vector3d.UnitZ, 1e-12);
        Near("at a third of the cluster rating", vessel.RcsForce.Length, (Meridian.RcsThrustNewtons + Aegis.RcsThrustNewtons) / 3.0, 1e-9);

        foreach (Stage stage in vessel.Stages) {

            stage.RcsPropellantMass = 0.0;

        }

        Near("a dry cluster pushes nothing", vessel.RcsForce.Length, 0.0, 1e-12);
        Expect("and is not accelerating", !vessel.IsAccelerating, "reported acceleration");

        Vessel drained = Stack.Build();

        drained.TranslationCommand = Vector3d.UnitZ;

        double seconds = 0.0;

        while (drained.RcsPropellantMass > 0.0 && seconds < 10000.0) {

            Integrator.StepAttitude(drained, 0.25);

            seconds += 0.25;

        }

        Expect("the bottle outlasts a long trim", seconds > 200.0, $"dry after {seconds:F0} s");

    }

    private static void AtmosphereColumn() {

        Section("atmosphere column");

        Atmosphere air = Home.Atmosphere;

        Near("sea level density", air.DensityAt(0.0), 1.225, 1e-12);
        Near("sea level temperature", air.TemperatureAt(0.0), 288.0, 1e-12);
        Near("sea level pressure", air.PressureAt(0.0), 1.225 * 287.05 * 288.0, 1e-6);
        Near("sea level speed of sound", air.SpeedOfSoundAt(0.0), 340.3, 0.5);

        // The column is exponential in shape but shifted to meet zero at the top, so one scale
        // height down from the datum is an e-fold to within the shift itself.
        Near("one scale height is an e-fold", air.DensityAt(air.ScaleHeight) / air.DensityAt(0.0), 1.0 / Math.E, 1e-3);

        Near("the air ends at the top", air.DensityAt(air.Top), 0.0, 0.0);
        Near("and beyond it", air.DensityAt(air.Top * 2.0), 0.0, 0.0);

        Expect("there is still air just under the top", air.DensityAt(air.Top - 1.0) > 0.0, "the column ended early");

        double last = double.MaxValue;
        bool falling = true;

        for (double altitude = 0.0; altitude <= air.Top; altitude += 250.0) {

            falling &= air.DensityAt(altitude) < last;

            last = air.DensityAt(altitude);

        }

        Expect("density falls all the way up", falling, "the column rose somewhere");

        Near("the tropopause holds its temperature", air.TemperatureAt(air.Top), air.TemperatureAt(air.TropopauseAltitude), 1e-12);

        Near("air at the equator turns with the ground", Home.AirVelocityAt(new Vector3d(Home.Radius, 0.0, 0.0)).Length, Math.PI * 2.0 * Home.Radius / Home.RotationPeriodSeconds, 1e-9);
        Near("and stands still over the pole", Home.AirVelocityAt(new Vector3d(0.0, 0.0, Home.Radius)).Length, 0.0, 1e-12);

    }

    private static void AeroGeometry() {

        Section("aerodynamic geometry");

        AeroProfile cylinder = AeroProfile.Build(0.0, 6.0, z => 2.0);

        Near("cylinder reference area", cylinder.ReferenceArea, Math.PI * 4.0, 1e-9);
        Near("cylinder planform area", cylinder.PlanformArea, 4.0 * 6.0, 1e-9);
        Near("cylinder planform centre", cylinder.PlanformCentre, 3.0, 1e-9);
        Near("cylinder volume", cylinder.Volume, Math.PI * 4.0 * 6.0, 1e-6);

        // A cone's potential centre of pressure is two thirds of the way back from its point, which
        // is the one number slender-body theory is always checked against.
        AeroProfile cone = AeroProfile.Build(0.0, 3.0, z => (3.0 - z) / 3.0);

        Near("cone potential centre", cone.PotentialCentre, 1.0, 1e-3);
        Near("cone planform centre", cone.PlanformCentre, 1.0, 1e-3);

        AeroProfile domed = AeroProfile.Build(0.0, 4.0, z => z < 1.0 ? Math.Sqrt(Math.Max(1.0 - (z - 1.0) * (z - 1.0), 0.0)) : 1.0);

        Near("a spherical end reports its own radius", domed.BaseCurvature, 1.0, 0.02);
        Expect("a flat end falls back to its own width", cylinder.BaseCurvature < 4.0, $"{cylinder.BaseCurvature:F3} m");

        Near("a hemisphere leans fully with the flow", domed.BaseTilt, 1.0, 1e-6);
        Expect("a flat face barely leans at all", cylinder.BaseTilt < 0.5, $"tilt {cylinder.BaseTilt:F3}");

    }

    private static void AeroForcesAndMoments() {

        Section("aerodynamic forces and moments");

        Vessel capsule = Capsule();

        // Over the pole the air is not turning, so the air-relative speed is the speed and every
        // figure below can be checked against a closed form.
        double altitude = 30000.0;

        Vector3d position = new Vector3d(0.0, 0.0, Home.Radius + altitude);
        Vector3d velocity = new Vector3d(3000.0, 0.0, 0.0);

        AeroForces trimmed = Trimmed(capsule, position, velocity);

        double density = Home.Atmosphere.DensityAt(altitude);
        double pressure = 0.5 * density * 3000.0 * 3000.0;

        Near("dynamic pressure", trimmed.DynamicPressure, pressure, 1e-6);
        Near("mach number", trimmed.Mach, 3000.0 / Home.Atmosphere.SpeedOfSoundAt(altitude), 1e-9);
        Near("shield first is a straight reversal", trimmed.AngleOfAttack, Math.PI, 1e-9);

        Near("drag on the trimmed capsule", trimmed.Force.Length, pressure * capsule.Profile.ReferenceArea * 1.62, 1.0);
        Close("and it opposes the flow exactly", trimmed.Force.Normalized, -velocity.Normalized, 1e-9);
        Near("a trimmed capsule is raising no moment", trimmed.Torque.Length, 0.0, 1e-6);

        Near("stagnation heating follows Sutton and Graves", trimmed.HeatFlux, Aerodynamics.SuttonGraves * Math.Sqrt(density / capsule.Profile.BaseCurvature) * Math.Pow(3000.0, 3.0), 1e-6);

        Near("drag goes as the square of speed", Trimmed(capsule, position, velocity * 2.0).Force.Length / trimmed.Force.Length, 4.0, 0.02);

        Vector3d axis = Vector3d.UnitY;

        Expect("shield first is restoring", Vector3d.Dot(Tilted(capsule, position, velocity, axis, 0.17, false), axis) < 0.0, "the moment ran with the upset");
        Expect("dome first is not", Vector3d.Dot(Tilted(capsule, position, velocity, axis, 0.17, true), axis) > 0.0, "the moment held it nose on");

        // Above the air there is nothing to fly through, however fast the vessel is going.
        capsule.Position = new Vector3d(0.0, 0.0, Home.Radius + Home.AtmosphereTop + 1000.0);

        Expect("vacuum raises no load at all", !Aerodynamics.Compute(capsule, Home).InAir, "reported air above the atmosphere");

    }

    /// <summary>The capsule on its own, which is what actually flies an entry.</summary>
    private static Vessel Capsule() {

        Vessel stack = Stack.Build();

        stack.Separate();

        return stack;

    }

    private static AeroForces Trimmed(Vessel vessel, Vector3d position, Vector3d velocity) {

        vessel.Position = position;
        vessel.Velocity = velocity;

        vessel.AngularVelocity = Vector3d.Zero;
        vessel.Orientation = QuaternionD.LookAlong(-velocity.Normalized, position);

        return Aerodynamics.Compute(vessel, Home);

    }

    /// <summary>The moment raised when the capsule is knocked off its trim, in world axes.</summary>
    private static Vector3d Tilted(Vessel vessel, Vector3d position, Vector3d velocity, Vector3d axis, double angle, bool reversed) {

        Vector3d aim = reversed ? velocity.Normalized : -velocity.Normalized;

        vessel.Position = position;
        vessel.Velocity = velocity;

        vessel.AngularVelocity = Vector3d.Zero;
        vessel.Orientation = QuaternionD.LookAlong(QuaternionD.FromAxisAngle(axis, angle).Rotate(aim), position);

        return vessel.Orientation.Rotate(Aerodynamics.Compute(vessel, Home).Torque);

    }

    private static void SkinHeating() {

        Section("skin heating");

        double flux = 100_000.0;

        double settled = Thermal.Equilibrium(flux);

        Near("equilibrium radiates what it takes in", Thermal.Emissivity * Thermal.StefanBoltzmann * Math.Pow(settled, 4.0), flux + Thermal.Emissivity * Thermal.StefanBoltzmann * Math.Pow(Thermal.AmbientTemperature, 4.0), 1e-6);

        double temperature = Thermal.AmbientTemperature;

        for (int step = 0; step < 60 * 400; step++) {

            temperature = Thermal.Step(temperature, flux, 4200.0, 1.0 / 60.0);

        }

        Near("a soaked skin settles on it", temperature, settled, 1.0);
        Expect("and never runs past it", temperature <= settled + 1e-9, $"{temperature:F3} K over {settled:F3} K");

        // A step far longer than the skin's own time constant has to land on equilibrium rather
        // than ring about it, or a single long step would throw the temperature to nonsense.
        Near("one enormous step lands on it too", Thermal.Step(Thermal.AmbientTemperature, flux, 4200.0, 10_000.0), settled, 1e-9);

        double cooling = Thermal.Step(1500.0, 0.0, 4200.0, 60.0);

        Expect("a hot skin radiates itself cool in vacuum", cooling < 1400.0, $"{cooling:F0} K after a minute");
        Expect("and never below the sky it is radiating into", cooling >= Thermal.AmbientTemperature, $"{cooling:F3} K");

    }

    private static void Staging() {

        Section("staging");

        Vessel stack = Stack.Build();

        stack.Position = new Vector3d(1574200.0, 0.0, 0.0);
        stack.Velocity = new Vector3d(0.0, 3181.0, 0.0);
        stack.Orientation = QuaternionD.LookAlong(stack.Velocity, stack.Position);

        stack.Throttle = 1.0;

        double mass = stack.Mass;
        int stages = stack.StageCount;

        Vector3d momentum = stack.Velocity * mass;
        Vector3d centre = stack.Position * mass;

        Expect("a stack of two can separate", stack.CanSeparate, "reported it could not");

        Vessel spent = stack.Separate();

        Expect("separation hands back the stage that came off", spent != null && spent.Name == "Meridian", "nothing came off");
        Expect("and it is debris", spent.IsDebris, "reported it was still crewed");
        Expect("the stack is one stage shorter", stack.StageCount == stages - 1, $"{stack.StageCount} stages");
        Expect("and the capsule is now the live stage", stack.Active.Name == "Aegis", stack.Active.Name);
        Expect("a single stage cannot separate again", !stack.CanSeparate && stack.Separate() == null, "it came apart twice");

        Near("mass is conserved", stack.Mass + spent.Mass, mass, 1e-9);
        Close("momentum is conserved", stack.Velocity * stack.Mass + spent.Velocity * spent.Mass, momentum, 1e-6);
        Close("and the pair's centre of mass has not moved", stack.Position * stack.Mass + spent.Position * spent.Mass, centre, 1e-6);

        Near("the springs push them apart at the rated speed", (stack.Velocity - spent.Velocity).Length, 0.7, 1e-9);
        Expect("the capsule leaves forward", Vector3d.Dot(stack.Velocity - spent.Velocity, stack.Nose) > 0.0, "it went backwards");

        Near("the lever is cut with the bolts", stack.Throttle, 0.0, 1e-12);

        Expect("the two are now apart in space", (stack.Position - spent.Position).Length > 3.0, $"{(stack.Position - spent.Position).Length:F2} m apart");

        // Each piece carries its own everything: neither can reach into the other any more.
        Near("the capsule keeps its own thrusters", stack.ControlTorqueLimit, Aegis.ControlTorque, 1e-9);
        Near("and the spent stage keeps its own", spent.ControlTorqueLimit, Meridian.ControlTorque, 1e-9);

        Near("the capsule's mass properties are its own", stack.CentreOfMassZ, stack.Forward.Properties.CentreZ, 1e-9);
        Expect("and it has an aerodynamic profile of its own", stack.Profile.Length < 3.0, $"{stack.Profile.Length:F2} m");

    }

    private static void Reentry() {

        Section("reentry");

        Vessel capsule = Entering(true);
        Vessel stage = Entering(false);

        Fall(capsule, out double capsulePeak, out double capsuleG, out double capsuleSeconds);
        Fall(stage, out double stagePeak, out _, out _);

        Expect("the capsule reaches the ground", capsule.Fate == VesselFate.Impacted, $"fate {capsule.Fate}");
        Expect("and takes a few minutes to do it", capsuleSeconds > 120.0 && capsuleSeconds < 2400.0, $"{capsuleSeconds:F0} s");

        Expect("its shield survives the entry", capsulePeak < Aegis.HeatLimit, $"{capsulePeak:F0} K of {Aegis.HeatLimit:F0} K");
        Expect("but it is hot enough to glow", capsulePeak > 800.0, $"peak {capsulePeak:F0} K");

        Expect("the load is survivable", capsuleG < 8.0, $"{capsuleG:F1} g");
        Expect("and is felt at all", capsuleG > 2.0, $"{capsuleG:F1} g");

        Expect("a bare stage burns up instead", stagePeak > Meridian.HeatLimit, $"{stagePeak:F0} K of {Meridian.HeatLimit:F0} K");

        Expect("the capsule ends up flying shield first", Math.Abs(capsule.Aero.AngleOfAttack - Math.PI) < 0.9, $"{capsule.Aero.AngleOfAttack * 180.0 / Math.PI:F0} degrees");

        // A pass that grazes the air is a pass that costs energy, which is what makes a low orbit
        // decay at all rather than lasting for ever.
        Vessel grazing = Entering(true);

        grazing.Position = new Vector3d(Home.Radius + 40000.0, 0.0, 0.0);
        grazing.Velocity = new Vector3d(0.0, Home.CircularVelocityAt(40000.0), 0.0);
        grazing.Orientation = QuaternionD.LookAlong(-grazing.Velocity.Normalized, grazing.Position);

        double before = grazing.OrbitAround(Home, 0.0).SemiMajorAxis;

        for (int step = 0; step < 60 * 60; step++) {

            Integrator.Step(grazing, Home, 1.0 / 60.0);

        }

        double after = grazing.OrbitAround(Home, 0.0).SemiMajorAxis;

        Expect("skimming the atmosphere pulls the orbit down", after < before - 100.0, $"{before:F0} m to {after:F0} m");

    }

    /// <summary>A vehicle on a transfer from three hundred kilometres down to twenty, released
    /// pointing the way it would actually be let go.</summary>
    private static Vessel Entering(bool capsule) {

        Vessel stack = Stack.Build();

        double apoapsis = Home.Radius + 300000.0;
        double periapsis = Home.Radius + 20000.0;

        double axis = (apoapsis + periapsis) * 0.5;

        stack.Position = new Vector3d(apoapsis, 0.0, 0.0);
        stack.Velocity = new Vector3d(0.0, Math.Sqrt(Home.Mu * (2.0 / apoapsis - 1.0 / axis)), 0.0);

        Vessel spent = stack.Separate();

        Vessel flying = capsule ? stack : spent;

        flying.Orientation = QuaternionD.LookAlong(-flying.Velocity.Normalized, flying.Position);
        flying.RcsEnabled = false;

        return flying;

    }

    /// <summary>Coasts the vessel down to the air on its conic, then flies the entry.</summary>
    private static void Fall(Vessel vessel, out double peakTemperature, out double peakLoad, out double seconds) {

        double step = 1.0 / 60.0;

        peakTemperature = 0.0;
        peakLoad = 0.0;
        seconds = 0.0;

        double time = 0.0;

        while (time < 6000.0) {

            double altitude = Home.AltitudeOf(vessel.Position);

            if (altitude <= 0.0) {

                vessel.Fate = VesselFate.Impacted;

                return;

            }

            if (altitude > Home.AtmosphereTop) {

                (vessel.Position, vessel.Velocity) = vessel.OrbitAround(Home, time).StateAt(time + 5.0);

                time += 5.0;

                continue;

            }

            Integrator.Step(vessel, Home, step);

            time += step;
            seconds += step;

            peakTemperature = Math.Max(peakTemperature, vessel.SkinTemperature);
            peakLoad = Math.Max(peakLoad, vessel.Aero.Force.Length / vessel.Mass / Home.SurfaceGravity);

        }

    }

    private static void NozzleExpansion() {

        Section("nozzle expansion");

        const double gamma = 1.22;

        double areaRatio = Meridian.ExpansionRatio;

        double exitMach = Nozzle.ExitMach(areaRatio, gamma);

        Near("area ratio round-trips through the exit mach", Nozzle.AreaRatio(exitMach, gamma), areaRatio, 1e-9);
        Expect("a 29:1 bell runs about mach four", exitMach > 3.8 && exitMach < 4.3, $"got {exitMach:F3}");

        Near("the sonic throat has an area ratio of one", Nozzle.AreaRatio(1.0, gamma), 1.0, 1e-12);
        Near("prandtl-meyer is zero at mach one", Nozzle.PrandtlMeyer(1.0, gamma), 0.0, 1e-12);

        double limit = Math.PI * 0.5 * (Math.Sqrt((gamma + 1.0) / (gamma - 1.0)) - 1.0);

        Near("the turning function converges to its limit", Nozzle.PrandtlMeyer(Nozzle.VacuumMach, gamma), limit, 0.2);

        Atmosphere air = Home.Atmosphere;

        Exhaust seaLevel = Nozzle.Expand(Meridian.ChamberPressure, areaRatio, gamma, 1.0, air.PressureAt(0.0), Meridian.EngineMouthRadius);
        Exhaust vacuum = Nozzle.Expand(Meridian.ChamberPressure, areaRatio, gamma, 1.0, 0.0, Meridian.EngineMouthRadius);

        Exhaust cached = Nozzle.ExpandFromMach(Meridian.ChamberPressure, exitMach, gamma, 1.0, air.PressureAt(0.0), Meridian.EngineMouthRadius);

        Near("cached nozzle geometry preserves exit pressure", cached.ExitPressure, seaLevel.ExitPressure, 1e-9);
        Near("cached nozzle geometry preserves separation", cached.Contraction, seaLevel.Contraction, 1e-12);

        foreach (double ambient in new[] { 0.0, 0.01, 100.0, 10_000.0, 101_325.0 }) {

            foreach (double setting in new[] { 0.0, 0.001, 0.1, 0.5, 1.0 }) {

                Exhaust state = Nozzle.ExpandFromMach(Meridian.ChamberPressure, exitMach, gamma, setting, ambient, Meridian.EngineMouthRadius);
                Expect("pressure/throttle sweep stays finite", double.IsFinite(state.TurnAngle) && double.IsFinite(state.Contraction)
                    && double.IsFinite(state.ShockCellLength) && state.Contraction > 0.0 && state.Contraction <= 1.0,
                    $"pressure {ambient}, throttle {setting}");

            }

        }

        Near("exit pressure follows the isentropic relation", seaLevel.ExitPressure, Meridian.ChamberPressure * Nozzle.PressureRatio(exitMach, gamma), 1e-6);

        Expect("the bell is over-expanded at sea level", seaLevel.PressureRatio < 1.0, $"got {seaLevel.PressureRatio:F3}");
        Expect("and the flow has separated off the wall", seaLevel.IsSeparated && seaLevel.Contraction > 0.3, $"got {seaLevel.Contraction:F3}");
        Expect("the jet necks in", seaLevel.TurnAngle < 0.0, $"got {seaLevel.TurnAngle:F3}");
        Expect("with shock cells a few diameters apart", seaLevel.ShockCellLength > Meridian.EngineMouthRadius && seaLevel.ShockCellLength < Meridian.EngineMouthRadius * 12.0, $"got {seaLevel.ShockCellLength:F3}");

        Expect("vacuum is infinitely under-expanded", double.IsPositiveInfinity(vacuum.PressureRatio), $"got {vacuum.PressureRatio}");
        Expect("a vacuum plume turns past a right angle", vacuum.TurnAngle > Math.PI * 0.5 && vacuum.TurnAngle < Math.PI, $"got {vacuum.TurnAngle:F3}");
        Expect("with no shock cells and no separation", vacuum.ShockCellLength == 0.0 && !vacuum.IsSeparated, $"got {vacuum.ShockCellLength}, {vacuum.Contraction}");

        // The exit pressure is a quarter of an atmosphere, so the jet is ideal about where the air is.
        double low = 0.0;
        double high = air.Top;

        for (int step = 0; step < 60; step++) {

            double middle = (low + high) * 0.5;

            if (air.PressureAt(middle) > seaLevel.ExitPressure) {

                low = middle;

            }
            else {

                high = middle;

            }

        }

        double idealAltitude = (low + high) * 0.5;

        Exhaust ideal = Nozzle.Expand(Meridian.ChamberPressure, areaRatio, gamma, 1.0, air.PressureAt(idealAltitude), Meridian.EngineMouthRadius);

        Expect("the jet is near ideal where exit meets ambient", Math.Abs(ideal.PressureRatio - 1.0) < 0.25, $"got {ideal.PressureRatio:F3} at {idealAltitude:F0} m");
        Expect("and turns through almost nothing there", Math.Abs(ideal.TurnAngle) < 0.15, $"got {ideal.TurnAngle:F3}");

        Exhaust throttled = Nozzle.Expand(Meridian.ChamberPressure, areaRatio, gamma, 0.4, air.PressureAt(0.0), Meridian.EngineMouthRadius);

        Expect("throttling back in thick air separates the flow further", throttled.Contraction < seaLevel.Contraction, $"got {throttled.Contraction:F3} against {seaLevel.Contraction:F3}");

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
