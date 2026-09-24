namespace FullThrust.Sim;

public sealed partial class Stage {

    public EngineLimits OperatingLimits { get; init; } = new();
    public double GimbalResponseSeconds { get; init; } = 0.12;
    public double RatingPressurePascals { get; init; }
    public double ChamberPressurePascals { get; init; } = 7_000_000.0;
    public EngineState[] EngineStates { get; private set; } = Array.Empty<EngineState>();
    public double DeliveredThrust { get; private set; }
    public double DeliveredFlow { get; private set; }
    public double PressureThrustFactor { get; private set; } = 1.0;
    public bool CanCommandEngines {

        get {

            for (int i = 0; i < EngineStates.Length; i++) {

                if (IsEngineLit(i) && EngineStates[i].Phase != EnginePhase.Exhausted) { return true; }

            }
            return false;

        }

    }
    public bool HasEngineTransient => Array.Exists(EngineStates, e => e.Phase is EnginePhase.Igniting or EnginePhase.Shutdown or EnginePhase.Purging);

    private double _exitArea;

    private void BuildEngineStates() {

        EngineStates = new EngineState[EngineCount];
        int index = 0;
        _exitArea = 0.0;
        foreach (Part part in Parts) {

            if (part.Kind != PartKind.Engine) { continue; }
            int count = Math.Max(part.Count, 1);
            double radius = part.Profile != null && part.Profile.Length > 0 ? part.Profile[0].Radius : part.Extent;
            double ring = count > 1 ? part.RingRadius > 0.0 ? part.RingRadius : Hull.RadiusAt(part.Top) / (1.0 + 1.2 * Math.Sin(Math.PI / count) / 1.15) : 0.0;
            double fit = count > 1 ? Math.Min(1.0, Hull.RadiusAt(part.Top) / (radius / Math.Sin(Math.PI / count) * 1.15 + radius * 1.2)) : 1.0;
            _exitArea += Math.PI * radius * radius * fit * fit * count;
            double exitPressure = 0.0;
            if (part.Profile is { Length: > 1 }) {
                double throat = double.MaxValue;
                Hull.Station mouth = part.Profile[0];
                foreach (Hull.Station station in part.Profile) {
                    throat = Math.Min(throat, station.Radius);
                    if (station.Z < mouth.Z) { mouth = station; }
                }
                exitPressure = ChamberPressurePascals * Nozzle.PressureRatio(mouth.Radius * mouth.Radius / (throat * throat));
            }
            for (int i = 0; i < count; i++) {

                double angle = Math.Tau * i / count;
                Hull.Station[] contact = part.Profile == null ? Array.Empty<Hull.Station>() : new Hull.Station[part.Profile.Length];
                for (int j = 0; j < contact.Length; j++) {
                    contact[j] = new Hull.Station((part.Profile[j].Z - part.Top) * fit, part.Profile[j].Radius * fit);
                }
                Array.Sort(contact, (a, b) => a.Z.CompareTo(b.Z));
                EngineStates[index++] = new EngineState {

                    Mount = new Vector3d(Math.Cos(angle) * ring, -Math.Sin(angle) * ring, part.Top),
                    ExitRadius = radius * fit,
                    ExitDistance = part.Length * fit,
                    ExitPressure = exitPressure,
                    ContactProfile = contact,

                };

            }

        }

    }

    public void AdvanceEngines(double throttle, double ambientPressure, double dt, bool active = true) {

        DeliveredThrust = 0.0;
        DeliveredFlow = 0.0;
        double rating = ThrustNewtons / Math.Max(EngineCount, 1);
        double area = _exitArea / Math.Max(EngineCount, 1);
        PressureThrustFactor = ThrustNewtons > 0.0 ? Math.Max(0.0, 1.0 + (RatingPressurePascals - ambientPressure) * _exitArea / ThrustNewtons) : 1.0;
        for (int i = 0; i < EngineStates.Length; i++) {

            EngineState engine = EngineStates[i];
            engine.Advance(active && IsEngineLit(i) ? throttle : 0.0, OperatingLimits, dt, PropellantMass > 0.0);
            // Pressure thrust vanishes with chamber flow, including low-throttle separation.
            double thrust = Math.Max(0.0, rating + (RatingPressurePascals - ambientPressure) * area) * engine.Power;
            DeliveredThrust += thrust;
            DeliveredFlow += SpecificImpulse > 0.0 ? rating * engine.Power / (SpecificImpulse * Vessel.StandardGravity) : 0.0;

        }

    }

}
