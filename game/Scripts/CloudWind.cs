using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

internal static class CloudWind {

    // A steady southwesterly at the launch coast. Spherical advection stays tangent everywhere,
    // carries all noise scales together, and never crosses a texture seam or resets a clock.
    internal const double Speed = 10.0;
    private static readonly Vector3d Axis = WindAxis();

    private static Vector3d WindAxis() {

        LaunchSite site = LaunchSite.Home;
        double cosine = Math.Cos(site.Latitude);
        Vector3d up = new(cosine * Math.Cos(site.Longitude), cosine * Math.Sin(site.Longitude), Math.Sin(site.Latitude));
        Vector3d east = Vector3d.Cross(Vector3d.UnitZ, up).Normalized;
        Vector3d north = Vector3d.Cross(up, east);
        return Vector3d.Cross(up, (east + north).Normalized).Normalized;

    }

    private static double Angle(double time, double radius) => Math.IEEERemainder(time * (Speed / radius), Math.Tau);

    public static Basis Frame(CelestialBody body, double time, bool bodyFixed = false) {

        Basis wind = new(Frames.Direction(Axis), (float)-Angle(time, body.Radius));
        double spin = Math.IEEERemainder(body.SpinAt(time), Math.Tau);
        return bodyFixed ? wind : wind * new Basis(Vector3.Up, (float)-spin);

    }

    public static Vector3d Advect(Vector3d point, double elapsed, double radius) {

        double angle = Angle(elapsed, radius);
        double cosine = Math.Cos(angle);
        double sine = Math.Sin(angle);
        return point * cosine + Vector3d.Cross(Axis, point) * sine + Axis * (Vector3d.Dot(Axis, point) * (1.0 - cosine));

    }

}
