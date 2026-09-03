namespace FullThrust.Sim;

public static class Integrator {

    public static void Step(Vessel vessel, CelestialBody body, double dt) {

        StepAttitude(vessel, dt);
        StepTranslation(vessel, body, dt);

    }

    // Body-frame Euler rates for a diagonal inertia; attitude still advances on a coasting trajectory.
    public static void StepAttitude(Vessel vessel, double dt) {

        SpendRcs(vessel, dt);

        Vector3d inertia = vessel.Inertia;
        Vector3d rate = vessel.AngularVelocity;
        Vector3d torque = vessel.ControlTorque;

        Vector3d angularAcceleration = new Vector3d(

            (torque.X - (inertia.Z - inertia.Y) * rate.Y * rate.Z) / inertia.X,
            (torque.Y - (inertia.X - inertia.Z) * rate.Z * rate.X) / inertia.Y,
            (torque.Z - (inertia.Y - inertia.X) * rate.X * rate.Y) / inertia.Z

        );

        vessel.AngularVelocity = rate + angularAcceleration * dt;

        vessel.Orientation = QuaternionD.Integrate(vessel.Orientation, vessel.AngularVelocity, dt);

    }

    private static void StepTranslation(Vessel vessel, CelestialBody body, double dt) {

        double thrust = vessel.CurrentThrust;
        double flow = thrust > 0.0 ? vessel.MassFlowRate * Math.Clamp(vessel.Throttle, 0.0, 1.0) : 0.0;

        Vector3d thrustAxis = vessel.Nose;
        Vector3d translation = vessel.RcsForce;

        double mu = body.Mu;

        (Vector3d Velocity, Vector3d Acceleration, double MassRate) Derive(Vector3d position, Vector3d velocity, double mass) {

            double radius = position.Length;

            Vector3d gravity = position * (-mu / (radius * radius * radius));
            Vector3d acceleration = gravity + (thrustAxis * thrust + translation) / mass;

            return (velocity, acceleration, -flow);

        }

        Vector3d p0 = vessel.Position;
        Vector3d v0 = vessel.Velocity;
        double m0 = vessel.Mass;

        var k1 = Derive(p0, v0, m0);
        var k2 = Derive(p0 + k1.Velocity * (dt * 0.5), v0 + k1.Acceleration * (dt * 0.5), m0 + k1.MassRate * (dt * 0.5));
        var k3 = Derive(p0 + k2.Velocity * (dt * 0.5), v0 + k2.Acceleration * (dt * 0.5), m0 + k2.MassRate * (dt * 0.5));
        var k4 = Derive(p0 + k3.Velocity * dt, v0 + k3.Acceleration * dt, m0 + k3.MassRate * dt);

        double sixth = dt / 6.0;

        vessel.Position = p0 + (k1.Velocity + (k2.Velocity + k3.Velocity) * 2.0 + k4.Velocity) * sixth;
        vessel.Velocity = v0 + (k1.Acceleration + (k2.Acceleration + k3.Acceleration) * 2.0 + k4.Acceleration) * sixth;

        // ponytail: propellant is debited at the step boundary, so a tank empties up to one step late; sub-step the burn if that ever matters
        vessel.PropellantMass = Math.Max(0.0, vessel.PropellantMass - flow * dt);

        if (flow > 0.0) {

            vessel.RecomputeMassProperties();

        }

    }


    // The quads are the whole attitude authority, so holding an attitude costs propellant like any other burn.
    private static void SpendRcs(Vessel vessel, double dt) {

        double duty = vessel.RcsDuty;

        if (duty <= 0.0) {

            return;

        }

        vessel.RcsPropellantMass = Math.Max(0.0, vessel.RcsPropellantMass - duty * vessel.RcsMassFlowRate * dt);

    }

}
