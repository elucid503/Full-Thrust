using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>Advances the vessel; coasts on the analytic conic, integrates only under thrust.</summary>
public sealed partial class Flight : Node {

    // The surface maps are 8192 x 4096 - 977 m a texel - so at 70 km one texel covered twenty screen
    // pixels and the ground read as mush. Held here until M4's LOD surface earns a lower orbit back.
    private const double StartAltitude = 300000.0;
    private const double StartInclination = 0.61;
    private const double StartTrueAnomaly = 0.60;

    private const double IntegrationStep = 1.0 / 120.0;

    private const double ThrottleRate = 0.6;

    public static readonly double[] WarpFactors = { 1.0, 2.0, 5.0, 10.0, 50.0, 100.0, 1000.0 };

    public static Flight Active { get; private set; }

    public CelestialBody Body { get; private set; }
    public Vessel Vessel { get; private set; }

    public Autopilot Autopilot { get; private set; }

    public double Time { get; private set; }

    public int WarpStep { get; private set; }

    public double Warp => WarpFactors[WarpStep];

    private Orbit _rails;
    private double _integrationDebt;

    public override void _Ready() {

        Active = this;

        Body = BodyCatalog.Home;
        Vessel = Meridian.Build();

        Autopilot = new Autopilot { MaxTorque = Meridian.ControlTorque };

        double radius = Body.Radius + StartAltitude;
        double speed = Body.CircularVelocityAt(StartAltitude);

        double sine = Math.Sin(StartTrueAnomaly);
        double cosine = Math.Cos(StartTrueAnomaly);

        double inclinationSine = Math.Sin(StartInclination);
        double inclinationCosine = Math.Cos(StartInclination);

        Vessel.Position = new Vector3d(radius * cosine, radius * sine * inclinationCosine, radius * sine * inclinationSine);
        Vessel.Velocity = new Vector3d(-speed * sine, speed * cosine * inclinationCosine, speed * cosine * inclinationSine);

        Vessel.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, Vessel.Velocity.Normalized);

        Autopilot.Hold = AttitudeHold.Prograde;

        Rerail();

        Frames.Rebase(Vessel.Position);

    }

    public void Advance(double delta) {

        ReadControls(delta);

        double step = delta * Warp;

        Time += step;

        if (Vessel.CurrentThrust > 0.0) {

            _integrationDebt += step;

            while (_integrationDebt >= IntegrationStep) {

                Autopilot.Update(Vessel, IntegrationStep);

                Integrator.Step(Vessel, Body, IntegrationStep);

                _integrationDebt -= IntegrationStep;

            }

            Rerail();

        }
        else {

            _integrationDebt = 0.0;

            (Vector3d position, Vector3d velocity) = _rails.StateAt(Time);

            Vessel.Position = position;
            Vessel.Velocity = velocity;

            // Warped time is not simulated time; the vessel is held rigid rather than spun by a step it never took.
            if (WarpStep == 0) {

                Autopilot.Update(Vessel, delta);

                Integrator.StepAttitude(Vessel, delta);

            }
            else {

                Vessel.AngularVelocity = Vector3d.Zero;
                Vessel.ControlTorque = Vector3d.Zero;

            }

        }

        Frames.Rebase(Vessel.Position);

    }

    public Orbit Orbit => _rails;

    public double Altitude => Body.AltitudeOf(Vessel.Position);

    /// <summary>Steps the warp factor, refusing to leave 1x while the engine is lit.</summary>
    public bool SetWarpStep(int step) {

        step = Math.Clamp(step, 0, WarpFactors.Length - 1);

        if (step > 0 && Vessel.CurrentThrust > 0.0) {

            return false;

        }

        WarpStep = step;

        return true;

    }

    public override void _UnhandledKeyInput(InputEvent @event) {

        if (@event is not InputEventKey key || !key.Pressed || key.Echo) {

            return;

        }

        switch (key.Keycode) {

            case Key.T:

                Autopilot.Hold = Autopilot.Hold == AttitudeHold.Off ? AttitudeHold.Stability : AttitudeHold.Off;

                break;

            case Key.Key1: Autopilot.Hold = AttitudeHold.Prograde; break;
            case Key.Key2: Autopilot.Hold = AttitudeHold.Retrograde; break;
            case Key.Key3: Autopilot.Hold = AttitudeHold.Normal; break;
            case Key.Key4: Autopilot.Hold = AttitudeHold.Antinormal; break;
            case Key.Key5: Autopilot.Hold = AttitudeHold.RadialOut; break;
            case Key.Key6: Autopilot.Hold = AttitudeHold.RadialIn; break;

            case Key.Z: Vessel.Throttle = 1.0; break;
            case Key.X: Vessel.Throttle = 0.0; break;

            case Key.Period: SetWarpStep(WarpStep + 1); break;
            case Key.Comma: SetWarpStep(WarpStep - 1); break;

        }

    }

    private void ReadControls(double delta) {

        if (Input.IsKeyPressed(Key.Shift)) {

            Vessel.Throttle = Math.Min(1.0, Vessel.Throttle + ThrottleRate * delta);

        }

        if (Input.IsKeyPressed(Key.Ctrl)) {

            Vessel.Throttle = Math.Max(0.0, Vessel.Throttle - ThrottleRate * delta);

        }

        if (Vessel.CurrentThrust > 0.0) {

            WarpStep = 0;

        }

        Autopilot.ManualCommand = new Vector3d(

            Axis(Key.S, Key.W),
            Axis(Key.A, Key.D),
            Axis(Key.E, Key.Q)

        );

    }

    private static double Axis(Key positive, Key negative) {

        return (Input.IsKeyPressed(positive) ? 1.0 : 0.0) - (Input.IsKeyPressed(negative) ? 1.0 : 0.0);

    }

    private void Rerail() {

        _rails = Vessel.OrbitAround(Body, Time);

    }

}
