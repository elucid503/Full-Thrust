using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>A camera cut loose from the vessel. It is flown in simulation coordinates and takes the
/// floating origin with it, so it can be taken to the far side of the planet without the world
/// coming apart under it.</summary>
public sealed partial class FreeCamera : Camera3D {

    private const float LookSpeed = 0.004f;
    private const float PitchLimit = 1.52f;

    private const float MinimumSpeed = 0.5f;
    private const float MaximumSpeed = 200_000.0f;

    // Held shift is a sprint, so the same speed setting covers a walk round the pad and a run to
    // the horizon without going back to the panel for it.
    private const float Sprint = 8.0f;

    public static FreeCamera Active { get; private set; }

    /// <summary>True while the free camera holds the view, and with it the movement keys.</summary>
    public static bool Flying => Active != null && Active.Current;

    /// <summary>Metres a second at full stick.</summary>
    public float Speed { get; set; } = 80.0f;

    // Held in body-fixed coordinates rather than inertial ones. The ground turns at eighty metres a
    // second under the pad, and a camera parked in the inertial frame is swept off it in seconds.
    private Vector3d _where;

    /// <summary>Where the eye is, in inertial simulation coordinates.</summary>
    public Vector3d Where => Flight.Active != null ? Flight.Active.Body.ToInertial(_where, Flight.Active.Time) : _where;

    private float _yaw;
    private float _pitch;

    private bool _looking;

    public override void _ExitTree() {

        if (Active == this) { Active = null; }

    }

    public override void _Ready() {

        Active = this;

        Near = 0.4f;
        Far = 4_000_000.0f;
        Fov = 55.0f;

        Current = false;

    }

    /// <summary>Takes the view from the chase camera, starting where it stood and looking where it
    /// looked, so the cut is a handover rather than a jump.</summary>
    public void Take(OrbitCamera chase) {

        Settle(Frames.Origin + Frames.Sim(chase.Eye));

        Vector3 forward = chase.Forward;

        Vector3 up = Vertical(Where);

        Frames.Horizon(up, out Vector3 side, out Vector3 ahead);

        _pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(forward.Dot(up), -1.0f, 1.0f)), -PitchLimit, PitchLimit);
        _yaw = Mathf.Atan2(-forward.Dot(side), forward.Dot(ahead));

        Current = true;

    }

    public void Release() {

        Current = false;

        OrbitCamera.Active?.MakeCurrent();

    }

    /// <summary>One frame of free flight. Called from the frame loop after the origin has settled,
    /// so the eye never lags a rebase by a frame and jumps five kilometres for one.</summary>
    public void Fly(double delta) {

        if (!Current) {

            Frames.Anchor = null;

            return;

        }

        Vector3d at = Where;

        Vector3 up = Vertical(at);

        Frames.Horizon(up, out Vector3 side, out Vector3 ahead);

        Vector3 forward = Look(side, ahead, up);
        Vector3 right = forward.Cross(up).Normalized();

        Vector3 command = forward * Axis(Key.W, Key.S) + right * Axis(Key.D, Key.A) + up * Axis(Key.E, Key.Q);

        if (command.LengthSquared() > 0.0f) {

            float rate = Speed * (Input.IsKeyPressed(Key.Shift) ? Sprint : 1.0f);

            at += Frames.Sim(command.Normalized() * (rate * (float)delta));

            Settle(at);

        }

        Frames.Anchor = at;

        GlobalPosition = Frames.Point(at);

        LookAt(GlobalPosition + forward, up);

    }

    public override void _UnhandledInput(InputEvent @event) {

        if (!Current) {

            return;

        }

        if (@event is InputEventMouseButton button) {

            if (button.ButtonIndex == MouseButton.Right) {

                _looking = button.Pressed;

            }

            if (button.ButtonIndex == MouseButton.WheelUp) {

                Speed = Mathf.Clamp(Speed * 1.30f, MinimumSpeed, MaximumSpeed);

            }

            if (button.ButtonIndex == MouseButton.WheelDown) {

                Speed = Mathf.Clamp(Speed / 1.30f, MinimumSpeed, MaximumSpeed);

            }

        }

        if (@event is InputEventMouseMotion motion && _looking) {

            _yaw += motion.Relative.X * LookSpeed;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * LookSpeed, -PitchLimit, PitchLimit);

        }

    }

    private void Settle(Vector3d inertial) {

        _where = Flight.Active != null ? Flight.Active.Body.ToBodyFixed(inertial, Flight.Active.Time) : inertial;

    }

    private static Vector3 Vertical(Vector3d at) => at.LengthSquared > 1.0 ? Frames.Direction(at.Normalized) : Vector3.Up;

    // The horizon triple is the left-handed one the chase arm swings in, so the yaw that turns the
    // view to the right runs against its side axis.
    private Vector3 Look(Vector3 side, Vector3 ahead, Vector3 up) {

        return ahead * (Mathf.Cos(_pitch) * Mathf.Cos(_yaw))
            - side * (Mathf.Cos(_pitch) * Mathf.Sin(_yaw))
            + up * Mathf.Sin(_pitch);

    }

    private static float Axis(Key positive, Key negative) {

        return (Input.IsKeyPressed(positive) ? 1.0f : 0.0f) - (Input.IsKeyPressed(negative) ? 1.0f : 0.0f);

    }

}
