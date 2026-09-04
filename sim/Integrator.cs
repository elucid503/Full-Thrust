namespace FullThrust.Sim;

public static class Integrator {

    public static void Step(Vessel vessel, CelestialBody body, double dt) {

        // Aerodynamic loads are read once for the step, the way thrust already is. Over a step this
        // short the air the vehicle is in changes by parts in ten thousand, and RK4 is here for
        // gravity, which does not.
        vessel.Aero = Aerodynamics.Compute(vessel, body);

        StepAttitude(vessel, dt);
        StepTranslation(vessel, body, dt);
        StepThermal(vessel, dt);

    }

    // Body-frame Euler rates for a diagonal inertia; attitude still advances on a coasting trajectory.
    public static void StepAttitude(Vessel vessel, double dt) {

        SpendRcs(vessel, dt);

        Vector3d inertia = vessel.Inertia;
        Vector3d rate = vessel.AngularVelocity;
        Vector3d torque = vessel.ControlTorque + vessel.Aero.Torque;

        Vector3d angularAcceleration = new Vector3d(

            (torque.X - (inertia.Z - inertia.Y) * rate.Y * rate.Z) / inertia.X,
            (torque.Y - (inertia.X - inertia.Z) * rate.Z * rate.X) / inertia.Y,
            (torque.Z - (inertia.Y - inertia.X) * rate.X * rate.Y) / inertia.Z

        );

        vessel.AngularVelocity = rate + angularAcceleration * dt;

        vessel.Orientation = QuaternionD.Integrate(vessel.Orientation, vessel.AngularVelocity, dt);

    }

    /// <summary>Advances the leading skin's temperature under whatever flux the air is putting on
    /// it. Runs in vacuum too, where the flux is nothing and the skin radiates itself cool.</summary>
    public static void StepThermal(Vessel vessel, double dt) {

        vessel.SkinTemperature = Thermal.Step(vessel.SkinTemperature, vessel.Aero.HeatFlux, vessel.Leading.HeatCapacity, dt);

    }

    private readonly struct Rate {

        public Vector3d Velocity { get; }
        public Vector3d Acceleration { get; }
        public double MassRate { get; }

        public Rate(Vector3d velocity, Vector3d acceleration, double massRate) {

            Velocity = velocity;
            Acceleration = acceleration;
            MassRate = massRate;

        }

    }

    private static Rate Derive(Vector3d position, Vector3d velocity, double mass, double mu, Vector3d push, double flow) {

        double radius = position.Length;

        Vector3d gravity = position * (-mu / (radius * radius * radius));

        return new Rate(velocity, gravity + push / mass, -flow);

    }

    private static void StepTranslation(Vessel vessel, CelestialBody body, double dt) {

        double thrust = vessel.CurrentThrust;
        double flow = vessel.CurrentMassFlow;

        Vector3d push = vessel.Nose * thrust + vessel.RcsForce + vessel.Aero.Force;

        double mu = body.Mu;

        Vector3d p0 = vessel.Position;
        Vector3d v0 = vessel.Velocity;

        double m0 = vessel.Mass;

        Rate k1 = Derive(p0, v0, m0, mu, push, flow);
        Rate k2 = Derive(p0 + k1.Velocity * (dt * 0.5), v0 + k1.Acceleration * (dt * 0.5), m0 + k1.MassRate * (dt * 0.5), mu, push, flow);
        Rate k3 = Derive(p0 + k2.Velocity * (dt * 0.5), v0 + k2.Acceleration * (dt * 0.5), m0 + k2.MassRate * (dt * 0.5), mu, push, flow);
        Rate k4 = Derive(p0 + k3.Velocity * dt, v0 + k3.Acceleration * dt, m0 + k3.MassRate * dt, mu, push, flow);

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

        vessel.SpendReactionControl(vessel.RcsDuty, dt);

    }

}
