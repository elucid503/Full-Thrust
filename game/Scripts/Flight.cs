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

    // Warp is stepped down so that whatever factor is running still leaves this long before ignition.
    private const double WarpMargin = 12.0;

    public static readonly double[] WarpFactors = { 1.0, 2.0, 5.0, 10.0, 50.0, 100.0, 1000.0 };

    public static Flight Active { get; private set; }

    public CelestialBody Body { get; private set; }
    public Vessel Vessel { get; private set; }

    public Autopilot Autopilot { get; private set; }

    /// <summary>The planned impulse, or null when nothing is planned.</summary>
    public Maneuver Node { get; private set; }

    public bool WarpingToNode { get; private set; }

    public double Time { get; private set; }

    public int WarpStep { get; private set; }

    public double Warp => WarpFactors[WarpStep];

    private Orbit _rails;
    private double _integrationDebt;

    public override void _Ready() {

        Active = this;

        Body = BodyCatalog.Home;
        Vessel = Meridian.Build();

        Autopilot = new Autopilot();

        double radius = Body.Radius + StartAltitude;
        double speed = Body.CircularVelocityAt(StartAltitude);

        double sine = Math.Sin(StartTrueAnomaly);
        double cosine = Math.Cos(StartTrueAnomaly);

        double inclinationSine = Math.Sin(StartInclination);
        double inclinationCosine = Math.Cos(StartInclination);

        Vessel.Position = new Vector3d(radius * cosine, radius * sine * inclinationCosine, radius * sine * inclinationSine);
        Vessel.Velocity = new Vector3d(-speed * sine, speed * cosine * inclinationCosine, speed * cosine * inclinationSine);

        // A shortest-arc rotation leaves the roll wherever it lands, which reads as a crooked navball.
        Vessel.Orientation = QuaternionD.LookAlong(Vessel.Velocity, Vessel.Position);

        Autopilot.Hold = AttitudeHold.Prograde;

        Rerail();

        Frames.Rebase(Vessel.Position);

    }

    public void Advance(double delta) {

        ReadControls(delta);

        Retire();
        AimAtNode();
        RunWarpToNode();

        double step = delta * Warp;

        Time += step;

        if (Vessel.IsAccelerating) {

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

    /// <summary>The conic the planned node would leave the vessel on, or null when nothing is planned.</summary>
    public Orbit PlannedOrbit => Node != null && !Node.IsEmpty ? Node.Result(_rails) : null;

    /// <summary>Places a node at a true anomaly on the current orbit, keeping any impulse already dialled in.</summary>
    public void PlaceNode(double trueAnomaly) {

        Node ??= new Maneuver();

        RetimeNode(trueAnomaly);

    }

    public void RetimeNode(double trueAnomaly) {

        if (Node == null) {

            return;

        }

        double ahead = _rails.TimeToTrueAnomaly(Time, trueAnomaly);

        if (double.IsNaN(ahead)) {

            return;

        }

        Node.Time = Time + ahead;

    }

    public void ClearNode() {

        Node = null;

        WarpingToNode = false;

        if (Autopilot.Hold == AttitudeHold.Maneuver) {

            Autopilot.Hold = AttitudeHold.Stability;

        }

    }

    public void ToggleWarpToNode() {

        WarpingToNode = Node != null && !Node.IsEmpty && !WarpingToNode;

        if (!WarpingToNode) {

            SetWarpStep(0);

        }

    }

    /// <summary>Seconds until the engine must light for the planned node, or NaN when there is none.</summary>
    public double TimeToIgnition => Node != null && !Node.IsEmpty ? Node.IgnitionTime(Vessel) - Time : double.NaN;

    private void Retire() {

        if (Node != null && Time > Node.Time + 2.0) {

            ClearNode();

        }

    }

    private void AimAtNode() {

        if (Node == null || Node.IsEmpty) {

            if (Autopilot.Hold == AttitudeHold.Maneuver) {

                Autopilot.Hold = AttitudeHold.Stability;

            }

            return;

        }

        Autopilot.ManeuverDirection = Node.WorldDeltaV(_rails);

    }

    // Warp comes down the ladder as the node approaches rather than being cut, so the rails never overshoot it.
    private void RunWarpToNode() {

        if (!WarpingToNode) {

            return;

        }

        double remaining = TimeToIgnition;

        if (double.IsNaN(remaining) || remaining <= WarpMargin) {

            WarpingToNode = false;

            SetWarpStep(0);

            return;

        }

        int wanted = 0;

        for (int step = WarpFactors.Length - 1; step > 0; step--) {

            if (remaining / WarpFactors[step] >= WarpMargin) {

                wanted = step;

                break;

            }

        }

        SetWarpStep(wanted);

    }

    public double Altitude => Body.AltitudeOf(Vessel.Position);

    /// <summary>Steps the warp factor, refusing to leave 1x while the engine is lit.</summary>
    public bool SetWarpStep(int step) {

        step = Math.Clamp(step, 0, WarpFactors.Length - 1);

        if (step > 0 && Vessel.IsAccelerating) {

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

            case Key.Key7:

                if (Node != null && !Node.IsEmpty) {

                    Autopilot.Hold = AttitudeHold.Maneuver;

                }

                break;

            case Key.R: Vessel.RcsEnabled = !Vessel.RcsEnabled; break;

            case Key.M: MapView.Active?.Toggle(); break;

            case Key.Tab: ToggleWarpToNode(); break;
            case Key.Delete: ClearNode(); break;

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

        if (Vessel.IsAccelerating) {

            WarpStep = 0;

        }

        Autopilot.ManualCommand = new Vector3d(

            Axis(Key.S, Key.W),
            Axis(Key.A, Key.D),
            Axis(Key.E, Key.Q)

        );

        Vessel.TranslationCommand = new Vector3d(

            Axis(Key.L, Key.J),
            Axis(Key.I, Key.K),
            Axis(Key.H, Key.N)

        );

    }

    private static double Axis(Key positive, Key negative) {

        return (Input.IsKeyPressed(positive) ? 1.0 : 0.0) - (Input.IsKeyPressed(negative) ? 1.0 : 0.0);

    }

    private void Rerail() {

        _rails = Vessel.OrbitAround(Body, Time);

    }

}
