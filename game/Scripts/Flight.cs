using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>Advances the vehicle and everything shed from it; coasts on the analytic conic, and
/// integrates whenever thrust or air makes that conic a lie.</summary>
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

    // Past this a spent stage is a dot on a conic, and it is far enough out that the floating
    // origin no longer holds it steady anyway.
    public const double DebrisRange = 60_000.0;

    public static readonly double[] WarpFactors = { 1.0, 2.0, 5.0, 10.0, 50.0, 100.0, 1000.0 };

    public static Flight Active { get; private set; }

    /// <summary>One body being propagated: what it is, the conic it is coasting on, and how much
    /// real time the integrator still owes it.</summary>
    public sealed class Tracked {

        public Vessel Vessel { get; init; }

        public Orbit Rails { get; set; }

        internal double Debt;

    }

    public CelestialBody Body { get; private set; }
    public Vessel Vessel { get; private set; }

    public Autopilot Autopilot { get; private set; }

    /// <summary>The planned impulse, or null when nothing is planned.</summary>
    public Maneuver Node { get; private set; }

    public bool WarpingToNode { get; private set; }

    public double Time { get; private set; }

    public int WarpStep { get; private set; }

    public double Warp => WarpFactors[WarpStep];

    /// <summary>Raised with the stage that has just come away, so the views can follow it out.</summary>
    public event Action<Vessel> Staged;

    /// <summary>Raised with a tracked body that has stopped existing, so its view goes with it.</summary>
    public event Action<Vessel> Scrubbed;

    private readonly List<Tracked> _debris = new List<Tracked>();

    private Tracked _own;

    public override void _Ready() {

        Active = this;

        Body = BodyCatalog.Home;
        Vessel = Stack.Build();

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

    /// <summary>Spent stages still being tracked, in the order they came off.</summary>
    public IReadOnlyList<Tracked> Debris => _debris;

    /// <summary>What ended the flight, or Flying while it has not.</summary>
    public VesselFate Fate => Vessel.Fate;

    public bool Ended => !Vessel.Intact;

    public bool DebugPaused { get; set; }

    public double AtmosphereTop => Body.AtmosphereTop;

    public bool InAtmosphere => Body.AirDensityAt(Vessel.Position) > 0.0;

    /// <summary>True while anything being tracked is in air. Warp propagates conics, and a conic is
    /// not what a body in air is flying, so the ladder is held at one until the sky is empty.</summary>
    public bool AirborneTraffic {

        get {

            if (InAtmosphere) {

                return true;

            }

            foreach (Tracked debris in _debris) {

                if (Body.AirDensityAt(debris.Vessel.Position) > 0.0) {

                    return true;

                }

            }

            return false;

        }

    }

    public void Advance(double delta) {

        if (DebugPaused) {

            Vessel.Aero = Aerodynamics.Compute(Vessel, Body);
            return;

        }

        if (Ended) {

            return;

        }

        ReadControls(delta);

        Retire();
        AimAtNode();
        RunWarpToNode();

        double step = delta * Warp;

        Time += step;

        Fly(_own, delta, step);

        Sweep(delta, step);

        Judge(Vessel);

        Frames.Rebase(Vessel.Position);

    }

    /// <summary>One body forward by one frame: integrated where a force acts on it, propagated on
    /// its own conic where none does.</summary>
    private void Fly(Tracked track, double delta, double step) {

        Vessel vessel = track.Vessel;

        bool flown = !vessel.IsDebris;

        if (vessel.IsAccelerating || Body.AirDensityAt(vessel.Position) > 0.0) {

            // Both thrust and air hold the ladder at one, so the integrator only ever advances real
            // time and the debt never has to carry a warped step.
            track.Debt += step;

            while (track.Debt >= IntegrationStep) {

                if (flown) {

                    Autopilot.Update(vessel, IntegrationStep);

                }

                Integrator.Step(vessel, Body, IntegrationStep);

                track.Debt -= IntegrationStep;

            }

            track.Rails = vessel.OrbitAround(Body, Time);

            return;

        }

        track.Debt = 0.0;

        (Vector3d position, Vector3d velocity) = track.Rails.StateAt(Time);

        vessel.Position = position;
        vessel.Velocity = velocity;

        // Nothing is flying through anything out here, so last frame's loads must not be left
        // standing on the vessel for the attitude step or the readouts to find.
        vessel.Aero = default;

        if (WarpStep == 0) {

            if (flown) {

                Autopilot.Update(vessel, delta);

            }

            Integrator.StepAttitude(vessel, delta);
            Integrator.StepThermal(vessel, delta);

            return;

        }

        // Warped time is not simulated time; the vessel is held rigid rather than spun by a step it never took.
        vessel.AngularVelocity = Vector3d.Zero;
        vessel.ControlTorque = Vector3d.Zero;

    }

    private void Sweep(double delta, double step) {

        for (int index = _debris.Count - 1; index >= 0; index--) {

            Tracked debris = _debris[index];

            Fly(debris, delta, step);

            Judge(debris.Vessel);

            if (debris.Vessel.Intact) {

                continue;

            }

            _debris.RemoveAt(index);

            Scrubbed?.Invoke(debris.Vessel);

        }

    }

    /// <summary>Whether a body is still flying, or has run out of sky or out of shield.</summary>
    private void Judge(Vessel vessel) {

        if (!vessel.Intact) {

            return;

        }

        if (Body.AltitudeOf(vessel.Position) <= 0.0) {

            vessel.Fate = VesselFate.Impacted;

            return;

        }

        if (vessel.SkinTemperature > vessel.SkinLimit) {

            vessel.Fate = VesselFate.BurnedUp;

        }

    }

    public Orbit Orbit => _own.Rails;

    /// <summary>The conic the planned node would leave the vessel on, or null when nothing is planned.</summary>
    public Orbit PlannedOrbit => Node != null && !Node.IsEmpty ? Node.Result(_own.Rails) : null;

    /// <summary>Lets the bottom stage go. It flies as a body of its own from the moment the bolts
    /// fire; nothing about it is faked out afterwards.</summary>
    public bool Separate() {

        if (Ended || !Vessel.CanSeparate) {

            return false;

        }

        Vessel spent = Vessel.Separate();

        _debris.Add(new Tracked { Vessel = spent, Rails = spent.OrbitAround(Body, Time) });

        Rerail();

        Staged?.Invoke(spent);

        return true;

    }

    /// <summary>Drops the vehicle onto a level flight path at an altitude and airspeed, keeping its
    /// heading. A debug entry point: an entry or a low burn otherwise costs a whole deorbit to reach.</summary>
    public void Place(double altitude, double speed) {

        Vector3d up = Vessel.Position.Normalized;
        Vector3d along = (Vessel.Velocity - up * Vector3d.Dot(Vessel.Velocity, up)).Normalized;

        if (along.LengthSquared < 1.0e-12) {

            along = Vector3d.Cross(up, Math.Abs(up.Z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX).Normalized;

        }

        Vessel.Position = up * (Body.Radius + Math.Max(altitude, 1.0));
        Vessel.Velocity = along * speed + Body.AirVelocityAt(Vessel.Position);

        Vessel.AngularVelocity = Vector3d.Zero;

        Rerail();

        Frames.Rebase(Vessel.Position);

    }

    /// <summary>Starts the flight over. The one way back from a vehicle that has been lost.</summary>
    public void Restart() {

        GetTree().ReloadCurrentScene();

    }

    /// <summary>Places a node at a true anomaly on the current orbit, keeping any impulse already dialled in.</summary>
    public void PlaceNode(double trueAnomaly) {

        Node ??= new Maneuver();

        RetimeNode(trueAnomaly);

    }

    public void RetimeNode(double trueAnomaly) {

        if (Node == null) {

            return;

        }

        double ahead = _own.Rails.TimeToTrueAnomaly(Time, trueAnomaly);

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

        Autopilot.ManeuverDirection = Node.WorldDeltaV(_own.Rails);

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

    /// <summary>Steps the warp factor, refusing to leave 1x under thrust or with anything in air.</summary>
    public bool SetWarpStep(int step) {

        step = Math.Clamp(step, 0, WarpFactors.Length - 1);

        if (step > 0 && (Vessel.IsAccelerating || AirborneTraffic)) {

            return false;

        }

        WarpStep = step;

        return true;

    }

    public override void _UnhandledKeyInput(InputEvent @event) {

        if (@event is not InputEventKey key || !key.Pressed || key.Echo) {

            return;

        }

        if (Ended) {

            if (key.Keycode == Key.Enter || key.Keycode == Key.KpEnter) {

                Restart();

            }

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

            case Key.Space: Separate(); break;

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

        if (Vessel.IsAccelerating || AirborneTraffic) {

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

        _own ??= new Tracked { Vessel = Vessel };

        _own.Rails = Vessel.OrbitAround(Body, Time);

    }

}
