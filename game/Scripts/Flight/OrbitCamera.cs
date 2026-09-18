using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>Chase camera on a yaw-pitch arm around the vessel.</summary>
public sealed partial class OrbitCamera : Node3D {

    private const float MinimumDistance = 6.0f;
    private const float MaximumDistance = 400.0f;

    private const float PitchLimit = 1.45f;
    private const float LookSpeed = 0.006f;

    public static OrbitCamera Active { get; private set; }

    [Export] public float Distance { get; set; } = 95.0f;
    [Export] public float Yaw { get; set; } = 2.5f;
    [Export] public float Pitch { get; set; } = 0.22f;

    private Camera3D _camera;
    private bool _dragging;

    public Vector3 Eye => _camera.GlobalPosition;
    public Vector3 Forward => -_camera.GlobalTransform.Basis.Z;

    public bool IsCurrent => _camera.Current;

    public float NearPlane => _camera.Near;
    public float FarPlane => _camera.Far;
    public float DebugYawRate { get; set; }

    public override void _Process(double delta) {

        Yaw += DebugYawRate * (float)delta;

    }

    public override void _Ready() {

        Active = this;

        _camera = GetNode<Camera3D>("Camera3D");

        _camera.Near = 0.4f;
        _camera.Far = 4_000_000.0f;
        _camera.Fov = 55.0f;

        _camera.Current = true;

    }

    public override void _UnhandledInput(InputEvent @event) {

        if (FreeCamera.Flying) {

            return;

        }

        if (@event is InputEventMouseButton button) {

            if (button.ButtonIndex == MouseButton.Right) {

                _dragging = button.Pressed;

            }

            if (button.ButtonIndex == MouseButton.WheelUp) {

                Distance = Mathf.Clamp(Distance * 0.88f, MinimumDistance, MaximumDistance);

            }

            if (button.ButtonIndex == MouseButton.WheelDown) {

                Distance = Mathf.Clamp(Distance * 1.14f, MinimumDistance, MaximumDistance);

            }

        }

        if (@event is InputEventMouseMotion motion && _dragging) {

            Yaw += motion.Relative.X * LookSpeed;
            Pitch = Mathf.Clamp(Pitch - motion.Relative.Y * LookSpeed, -PitchLimit, PitchLimit);

        }

    }

    public void MakeCurrent() => _camera.Current = true;

    /// <summary>Swings the arm so the camera looks along the given direction, with yaw and pitch
    /// taken about the local vertical the view will be rolled to.</summary>
    public void AimAt(Vector3 direction, Vector3 vertical) {

        Vector3 arm = -direction.Normalized();

        Frames.Horizon(vertical, out Vector3 side, out Vector3 ahead);

        Pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(arm.Dot(vertical), -1.0f, 1.0f)), -PitchLimit, PitchLimit);
        Yaw = Mathf.Atan2(arm.Dot(side), arm.Dot(ahead));

    }

    /// <summary>Swings the arm and then lifts the eye clear of the ground, so a chase view close to
    /// the surface cannot end up looking at the inside of a hill.</summary>
    public void Sync(Vector3 focus, Vector3 vertical, float floor) {

        Frames.Horizon(vertical, out Vector3 side, out Vector3 ahead);

        Vector3 arm = side * (Mathf.Cos(Pitch) * Mathf.Sin(Yaw))
            + vertical * Mathf.Sin(Pitch)
            + ahead * (Mathf.Cos(Pitch) * Mathf.Cos(Yaw));

        Position = focus;

        Vector3 eye = focus + arm * Distance;

        // In double: a radius of a million metres cut to float steps by an eighth of a metre, and
        // an eye held on the floor would step with it every frame.
        Vector3d radial = Frames.Origin + Frames.Sim(eye);

        double height = radial.Length;

        if (height > 0.0 && height < floor) {

            eye = Frames.Point(radial * (floor / height));

        }

        _camera.GlobalPosition = eye;

        // Rolled to the local vertical rather than to the world's: at any latitude but the equator
        // the two are different, and on the ground the difference is a horizon that leans.
        Vector3 look = (GlobalPosition - eye).Normalized();

        _camera.LookAt(GlobalPosition, Mathf.Abs(look.Dot(vertical)) > 0.999f ? Vector3.Up : vertical);

        Vessel vessel = Flight.Active?.Vessel;

        if (vessel != null && !Flight.Active.DebugPaused) {

            float altitude = (float)Flight.Active.Altitude;
            float amplitude = vessel.CurrentThrust > 0.0 ? (float)vessel.Throttle * (1.0f - Mathf.SmoothStep(80, 1200, altitude)) * 0.0012f : 0.0f;
            float time = (float)(Flight.Active.Time % 100.0);
            _camera.RotateObjectLocal(Vector3.Right, amplitude * (Mathf.Sin(time * 37.0f) + 0.4f * Mathf.Sin(time * 63.0f)));
            _camera.RotateObjectLocal(Vector3.Up, amplitude * 0.6f * Mathf.Sin(time * 43.0f));

        }

    }

}
