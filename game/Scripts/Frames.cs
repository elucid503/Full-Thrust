using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>Sim (Z-polar, double) to Godot (Y-up, float) via a floating origin.</summary>
public static class Frames {

    public const double RebaseDistance = 5000.0;

    public static Vector3d Origin { get; private set; } = Vector3d.Zero;

    /// <summary>What the origin follows instead of whatever is passed to Rebase, while the debug
    /// free camera has the view. Every rebase runs through one decision, so nothing can pull the
    /// origin back to the vessel a frame after the camera has taken it away.</summary>
    public static Vector3d? Anchor { get; set; }

    // The origin is pinned to the ground, not to inertial space: a pad turns at ninety metres a
    // second, and an origin left behind drags every shadow texel and float rounding through the
    // scene each frame. Held body-fixed and read back at the current spin.
    private static Vector3d _fixedOrigin = Vector3d.Zero;
    private static CelestialBody _body;
    private static double _time;

    public static void Follow(CelestialBody body, double time) {

        _body = body;
        _time = time;

        Origin = body.ToInertial(_fixedOrigin, time);

    }

    public static void Rebase(Vector3d focus) {

        if (Anchor.HasValue) {

            focus = Anchor.Value;

        }

        Vector3d fixedFocus = _body != null ? _body.ToBodyFixed(focus, _time) : focus;

        if ((fixedFocus - _fixedOrigin).LengthSquared < RebaseDistance * RebaseDistance) {

            return;

        }

        _fixedOrigin = fixedFocus;
        Origin = _body != null ? _body.ToInertial(_fixedOrigin, _time) : focus;

    }

    public static Vector3 Point(Vector3d position) => Direction(position - Origin);

    /// <summary>The local horizon at a vertical: the two axes a view rolled to that vertical turns
    /// about. Yaw and pitch measured in this frame stay level at any latitude, where an arm built on
    /// the world's polar axis leans by the latitude itself.</summary>
    public static void Horizon(Vector3 vertical, out Vector3 side, out Vector3 ahead) {

        Vector3 reference = Mathf.Abs(vertical.Y) > 0.999f ? Vector3.Right : Vector3.Up;

        side = reference.Cross(vertical).Normalized();
        ahead = side.Cross(vertical);

    }

    // A quarter turn about X carries the sim's polar Z onto Godot's vertical Y and preserves handedness.
    public static Vector3 Direction(Vector3d value) => new Vector3((float)value.X, (float)value.Z, (float)-value.Y);

    // The inverse of Direction: a Godot direction read back as a sim one. The map turns a drag in
    // camera axes into a delta-v, and the sim is the only place that vector means anything.
    public static Vector3d Sim(Vector3 value) => new Vector3d(value.X, -value.Z, value.Y);

    // Same quarter-turn as Direction: shuffle the axis, leave the angle.
    public static Quaternion Rotation(QuaternionD value) => new Quaternion((float)value.X, (float)value.Z, (float)-value.Y, (float)value.W);

}
