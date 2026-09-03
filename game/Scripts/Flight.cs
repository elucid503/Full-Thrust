using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>Advances the vessel; coasts on the analytic conic, integrates only under thrust.</summary>
public sealed partial class Flight : Node {

    private const double StartAltitude = 70000.0;
    private const double StartInclination = 0.61;
    private const double StartTrueAnomaly = 0.60;

    private const double IntegrationStep = 1.0 / 120.0;

    public static Flight Active { get; private set; }

    public CelestialBody Body { get; private set; }
    public Vessel Vessel { get; private set; }

    public double Time { get; private set; }

    private Orbit _rails;
    private double _integrationDebt;

    public override void _Ready() {

        Active = this;

        Body = BodyCatalog.Home;
        Vessel = Meridian.Build();

        double radius = Body.Radius + StartAltitude;
        double speed = Body.CircularVelocityAt(StartAltitude);

        double sine = Math.Sin(StartTrueAnomaly);
        double cosine = Math.Cos(StartTrueAnomaly);

        double inclinationSine = Math.Sin(StartInclination);
        double inclinationCosine = Math.Cos(StartInclination);

        Vessel.Position = new Vector3d(radius * cosine, radius * sine * inclinationCosine, radius * sine * inclinationSine);
        Vessel.Velocity = new Vector3d(-speed * sine, speed * cosine * inclinationCosine, speed * cosine * inclinationSine);

        Vessel.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, Vessel.Velocity.Normalized);

        Rerail();

        Frames.Rebase(Vessel.Position);

    }

    public void Advance(double delta) {

        Time += delta;

        if (Vessel.CurrentThrust > 0.0) {

            _integrationDebt += delta;

            while (_integrationDebt >= IntegrationStep) {

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

            Integrator.StepAttitude(Vessel, delta);

        }

        Frames.Rebase(Vessel.Position);

    }

    public Orbit Orbit => _rails;

    public double Altitude => Body.AltitudeOf(Vessel.Position);

    private void Rerail() {

        _rails = Vessel.OrbitAround(Body, Time);

    }

}
