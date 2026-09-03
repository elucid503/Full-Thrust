using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>Sim (Z-polar, double) to Godot (Y-up, float) via a floating origin.</summary>
public static class Frames {

    public const double RebaseDistance = 5000.0;

    public static Vector3d Origin { get; private set; } = Vector3d.Zero;

    public static void Rebase(Vector3d focus) {

        if ((focus - Origin).LengthSquared < RebaseDistance * RebaseDistance) {

            return;

        }

        Origin = focus;

    }

    public static Vector3 Point(Vector3d position) => Direction(position - Origin);

    // A quarter turn about X carries the sim's polar Z onto Godot's vertical Y and preserves handedness.
    public static Vector3 Direction(Vector3d value) => new Vector3((float)value.X, (float)value.Z, (float)-value.Y);

    // Same quarter-turn as Direction: shuffle the axis, leave the angle.
    public static Quaternion Rotation(QuaternionD value) => new Quaternion((float)value.X, (float)value.Z, (float)-value.Y, (float)value.W);

}
