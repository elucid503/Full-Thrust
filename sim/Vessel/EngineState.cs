namespace FullThrust.Sim;

public enum EnginePhase { Off, Igniting, Running, Shutdown, Purging, Exhausted }

/// <summary>Optional operating restrictions. A restart excludes the first ignition; null means unlimited.</summary>
public sealed class EngineLimits {

    public double MinimumThrottle { get; init; }
    public double IgnitionDelaySeconds { get; init; }
    public int? RestartLimit { get; init; }

}

/// <summary>One chamber and actuator, advanced only by simulation time.</summary>
public sealed class EngineState {

    public EnginePhase Phase { get; private set; }
    public double Power { get; private set; }
    public double PhaseTime { get; private set; }
    public int Ignitions { get; private set; }
    public Vector3d Mount { get; init; }
    public double ExitRadius { get; init; }
    public double ExitDistance { get; init; }
    // Rings in engine-local coordinates, with the mount at Z=0. The view can refine these
    // from an imported mesh; the authored profile remains available to headless simulation.
    private Hull.Station[] _contactProfile;
    public double ContactCentreZ { get; private set; }
    public double ContactBound { get; private set; }
    public Hull.Station[] ContactProfile {
        get => _contactProfile;
        set {
            value = value == null ? null : VesselSurface.SimplifyProfile(value);
            _contactProfile = value;
            ContactCentreZ = value is { Length: > 0 } ? (value[0].Z + value[^1].Z) * 0.5 : 0.0;
            ContactBound = 0.0;
            if (value == null) { return; }
            foreach (Hull.Station ring in value) {
                double z = ring.Z - ContactCentreZ;
                ContactBound = Math.Max(ContactBound, Math.Sqrt(z * z + ring.Radius * ring.Radius));
            }
        }
    }
    public QuaternionD ContactRotation { get; private set; } = QuaternionD.Identity;
    public Vector3d Gimbal { get; private set; }
    public Vector3d Direction => ContactRotation.Rotate(Vector3d.UnitZ);
    public double Residual { get; private set; }
    public double Purge => Phase is EnginePhase.Shutdown or EnginePhase.Purging
        ? Residual * Pulse(PhaseTime, 0.06, 0.22, 0.9) : 0.0;
    public double Burnoff => Phase is EnginePhase.Shutdown or EnginePhase.Purging
        ? Residual * Pulse(PhaseTime, 0.025, 0.10, 0.32) : 0.0;

    private bool _commanded;

    private static double Pulse(double t, double start, double peak, double end) {

        if (t <= start || t >= end) { return 0.0; }
        double u = t < peak ? (t - start) / (peak - start) : (end - t) / (end - peak);
        return u * u * (3.0 - 2.0 * u);

    }

    public void Advance(double command, EngineLimits limits, double dt, bool supplied, bool reactionControl = false) {

        if (!double.IsFinite(dt) || dt <= 0.0) { return; }
        command = supplied && double.IsFinite(command) ? Math.Clamp(command, 0.0, 1.0) : 0.0;
        bool on = command > 0.0;
        if (on && !_commanded) {

            bool allowed = !limits.RestartLimit.HasValue || Ignitions <= Math.Max(limits.RestartLimit.Value, 0);
            Phase = allowed ? EnginePhase.Igniting : EnginePhase.Exhausted;
            PhaseTime = 0.0;
            if (allowed) { Ignitions++; }

        }
        if (!on && _commanded) {

            Residual = Power;
            Phase = Power > 0.0001 ? EnginePhase.Shutdown : EnginePhase.Off;
            PhaseTime = 0.0;

        }
        _commanded = on;
        double before = PhaseTime;
        PhaseTime += dt;
        double burnStep = dt;
        if (Phase == EnginePhase.Igniting) {

            double delay = Math.Max(limits.IgnitionDelaySeconds, 0.0);
            burnStep = Math.Clamp(PhaseTime - Math.Max(before, delay), 0.0, dt);
            if (PhaseTime >= delay) { Phase = EnginePhase.Running; }

        }
        double wanted = Phase == EnginePhase.Running ? Math.Max(command, Math.Clamp(limits.MinimumThrottle, 0.0, 1.0)) : 0.0;
        double response = reactionControl ? 0.012 : wanted > Power ? 0.075 : wanted > 0.0 ? 0.055 : 0.018;
        Power = wanted + (Power - wanted) * Math.Exp(-burnStep / response);
        if (!supplied) { Power = 0.0; }
        if (Phase == EnginePhase.Shutdown && PhaseTime >= 0.09) { Phase = EnginePhase.Purging; Power = 0.0; }
        if (Phase == EnginePhase.Purging && PhaseTime >= 0.9) { Phase = EnginePhase.Off; Residual = 0.0; }
        if (Power < 0.00001) { Power = 0.0; }

    }

    public void Traverse(Vector3d target, double range, double responseSeconds, double dt) {

        if (!double.IsFinite(dt) || dt <= 0.0) { return; }
        double magnitude = target.Length;
        if (magnitude > range) { target *= Math.Max(range, 0.0) / magnitude; }
        Gimbal = target + (Gimbal - target) * Math.Exp(-dt / Math.Max(responseSeconds, 0.001));
        if (Gimbal.Length > range) { Gimbal = Gimbal.Normalized * Math.Max(range, 0.0); }
        ContactRotation = QuaternionD.FromAxisAngle(Gimbal.Normalized, Gimbal.Length);

    }

}
