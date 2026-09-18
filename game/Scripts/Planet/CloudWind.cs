using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

internal static class CloudWind {

    // Integrating the shared wind preserves cloud phase when F1 changes the weather.
    internal const double Speed = 10.0;
    private static readonly Vector3d Axis = Weather.Axis;

    public static Basis Frame(CelestialBody body, double time, bool bodyFixed = false) {

        double distance = body.Weather?.WindDistanceAt(time) ?? time * Speed;
        Basis wind = new(Frames.Direction(Axis), (float)-Math.IEEERemainder(distance / body.Radius, Math.Tau));
        double spin = Math.IEEERemainder(body.SpinAt(time), Math.Tau);
        return bodyFixed ? wind : wind * new Basis(Vector3.Up, (float)-spin);

    }

    public static Vector3d Advect(Vector3d point, double elapsed, double radius, Weather weather = null, double startTime = 0.0) {

        double distance = weather == null ? elapsed * Speed : weather.WindDistanceAt(startTime + elapsed) - weather.WindDistanceAt(startTime);
        double angle = Math.IEEERemainder(distance / radius, Math.Tau);
        double cosine = Math.Cos(angle);
        double sine = Math.Sin(angle);
        return point * cosine + Vector3d.Cross(Axis, point) * sine + Axis * (Vector3d.Dot(Axis, point) * (1.0 - cosine));

    }

}
