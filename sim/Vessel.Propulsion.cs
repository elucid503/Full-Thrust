namespace FullThrust.Sim;

public sealed partial class Vessel {

    public Vector3d EngineForce { get; private set; }
    public Vector3d EngineTorque { get; private set; }
    public Vector3d AppliedRcsTorque { get; private set; }
    private bool _actuatorsAdvanced;
    public Vector3d AppliedControlTorque => _actuatorsAdvanced && Active.EngineCount > 0 ? EngineTorque + AppliedRcsTorque : ControlTorque;

    public Vector3d ControlTorqueLimits {

        get {

            double pitch = ControlTorqueLimit;
            double roll = RcsEnabled ? ThrusterTorqueLimit : 0.0;
            Stage stage = Active;
            double power = 0.0;
            foreach (EngineState engine in stage.EngineStates) { power += engine.Power; }
            for (int i = 0; i < stage.EngineStates.Length; i++) {

                EngineState engine = stage.EngineStates[i];
                double radius = Math.Sqrt(engine.Mount.X * engine.Mount.X + engine.Mount.Y * engine.Mount.Y);
                roll += CurrentThrust * engine.Power / Math.Max(power, 0.00001) * Math.Sin(stage.GimbalRange * stage.GimbalLimit(i)) * radius;

            }
            return new Vector3d(pitch, pitch, roll);

        }

    }

    public void AdvanceEngines(CelestialBody body, double dt, bool traverse = true) {

        if (dt <= 0.0) { return; }
        double pressure = body.Atmosphere?.PressureAt(body.AltitudeOf(Position)) ?? 0.0;
        foreach (Stage stage in _stages) { stage.AdvanceEngines(Throttle, pressure, dt, stage == Active && Intact); }
        if (traverse) { AdvanceActuators(dt); }

    }

    public void AdvanceActuators(double dt) {

        if (dt <= 0.0) { return; }
        _actuatorsAdvanced = true;
        EngineForce = Vector3d.Zero;
        EngineTorque = Vector3d.Zero;
        Stage current = Active;
        double rcs = RcsEnabled ? ThrusterTorqueLimit : 0.0;
        Vector3d limits = ControlTorqueLimits;
        AppliedRcsTorque = new Vector3d(

            Math.Clamp(ControlTorque.X * rcs / Math.Max(limits.X, 0.00001), -rcs, rcs),
            Math.Clamp(ControlTorque.Y * rcs / Math.Max(limits.Y, 0.00001), -rcs, rcs),
            Math.Clamp(ControlTorque.Z * rcs / Math.Max(limits.Z, 0.00001), -rcs, rcs)

        );
        Vector3d demand = ControlTorque - AppliedRcsTorque;
        double totalPower = 0.0;
        foreach (EngineState engine in current.EngineStates) { totalPower += engine.Power; }
        double thrustPerPower = totalPower > 0.0 ? CurrentThrust / totalPower : 0.0;
        for (int i = 0; i < current.EngineStates.Length; i++) {

            EngineState engine = current.EngineStates[i];
            Vector3d lever = engine.Mount - Vector3d.UnitZ * CentreOfMassZ;
            double thrust = thrustPerPower * engine.Power;
            Vector3d target = Vector3d.Zero;
            if (thrust > 0.0 && lever.LengthSquared > 0.0 && current.IsEngineLit(i)) {

                Vector3d moment = demand * (engine.Power / Math.Max(totalPower, 0.00001));
                double axial = Math.Abs(lever.Z) > 0.001 ? lever.Z : -0.001;
                target = new Vector3d(moment.X, moment.Y, 0.0) / (thrust * axial);
                double radial = lever.X * lever.X + lever.Y * lever.Y;
                if (radial > 0.0001) {

                    target -= new Vector3d(lever.X, lever.Y, 0.0) * (moment.Z / (thrust * radial));

                }

            }
            engine.Traverse(target, current.GimbalRange * current.GimbalLimit(i), current.GimbalResponseSeconds, dt);
            double supplied = current.DeliveredFlow > 0.0 ? Math.Min(1.0, current.PropellantMass / (current.DeliveredFlow * dt)) : 1.0;
            Vector3d force = engine.Direction * thrust;
            EngineForce += force;
            EngineTorque += Vector3d.Cross(lever, force) * supplied;

        }

    }

}
