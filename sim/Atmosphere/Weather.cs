namespace FullThrust.Sim;

public enum WeatherPreset { Calm, Fair, Breezy, Gale }

/// <summary>Shared surface wind, sea state and cloud coverage, evaluated on the simulation clock.</summary>
public sealed class Weather {

    public static readonly Vector3d Origin = SiteDirection();
    public static readonly Vector3d Along = WindDirection();
    public static readonly Vector3d Axis = Vector3d.Cross(Origin, Along).Normalized;

    private double _changed;
    private double _distance;
    private double _wind = 10.0;
    private double _sea = 10.0;
    private double _coverage;
    private double _targetCoverage;
    private Weather _previous;

    public double TargetWindSpeed { get; private set; } = 10.0;
    public WeatherPreset Preset { get; private set; } = WeatherPreset.Fair;

    private static Vector3d SiteDirection() {

        LaunchSite site = LaunchSite.Home;
        return new Vector3d(Math.Cos(site.Latitude) * Math.Cos(site.Longitude),
            Math.Cos(site.Latitude) * Math.Sin(site.Longitude), Math.Sin(site.Latitude));

    }

    private static Vector3d WindDirection() {

        Vector3d east = Vector3d.Cross(Vector3d.UnitZ, Origin).Normalized;
        return (east + Vector3d.Cross(Origin, east)).Normalized;

    }

    private double Elapsed(double time) => Math.Max(0.0, time - _changed);
    private double Blend(double from, double to, double time, double seconds) => to + (from - to) * Math.Exp(-Elapsed(time) / seconds);

    private Weather At(double time) {

        Weather state = this;
        while (state._previous != null && time < state._changed) { state = state._previous; }
        return state;

    }

    public double WindSpeedAt(double time) => At(time).CurrentWind(time);
    public double SeaSpeedAt(double time) => At(time).CurrentSea(time);
    public double SeaSpeedRateAt(double time) => (At(time).TargetWindSpeed - SeaSpeedAt(time)) / 25.0;
    public double CoverageAt(double time) => At(time).CurrentCoverage(time);
    public double WindDistanceAt(double time) => At(time).CurrentDistance(time);
    private double CurrentWind(double time) => Blend(_wind, TargetWindSpeed, time, 5.0);
    private double CurrentSea(double time) => Blend(_sea, TargetWindSpeed, time, 25.0);
    private double CurrentCoverage(double time) => Blend(_coverage, _targetCoverage, time, 8.0);
    private double CurrentDistance(double time) => _distance + TargetWindSpeed * Elapsed(time)
        + (_wind - TargetWindSpeed) * 5.0 * (1.0 - Math.Exp(-Elapsed(time) / 5.0));

    public void SetWindSpeed(double speed, double time) {

        if (!double.IsFinite(speed) || !double.IsFinite(time)) { return; }
        Weather previous = time > _changed ? (Weather)MemberwiseClone() : _previous;
        _distance = WindDistanceAt(time);
        _wind = WindSpeedAt(time);
        _sea = SeaSpeedAt(time);
        _coverage = CoverageAt(time);
        _changed = time;
        TargetWindSpeed = Math.Clamp(speed, 0.0, 40.0);
        _previous = previous;

    }

    public void SetPreset(WeatherPreset preset, double time) {

        SetWindSpeed(preset switch { WeatherPreset.Calm => 0.0, WeatherPreset.Breezy => 18.0, WeatherPreset.Gale => 32.0, _ => 10.0 }, time);
        _targetCoverage = preset switch { WeatherPreset.Calm => -0.65, WeatherPreset.Breezy => 0.18, WeatherPreset.Gale => 0.42, _ => 0.0 };
        Preset = preset;

    }

    public Vector3d VelocityAt(CelestialBody body, Vector3d position, double time) {

        if (!body.HasAtmosphere) { return Vector3d.Zero; }
        double height = Math.Max(0.0, body.AltitudeOf(position));
        double fade = 1.0 - Ocean.Smooth(12_000.0, body.AtmosphereTop, height);
        Vector3d wind = Vector3d.Cross(Axis, body.ToBodyFixed(position, time).Normalized) * (WindSpeedAt(time) * fade);
        return body.ToInertial(wind, time);

    }

}
