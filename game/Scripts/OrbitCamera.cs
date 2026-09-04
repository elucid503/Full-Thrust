using Godot;

namespace FullThrust.Game;

/// <summary>Chase camera on a yaw-pitch arm around the vessel.</summary>
public sealed partial class OrbitCamera : Node3D {

    private const float MinimumDistance = 6.0f;
    private const float MaximumDistance = 400.0f;

    private const float PitchLimit = 1.45f;
    private const float LookSpeed = 0.006f;

    public static OrbitCamera Active { get; private set; }

    [Export] public float Distance { get; set; } = 26.0f;
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

            Yaw -= motion.Relative.X * LookSpeed;
            Pitch = Mathf.Clamp(Pitch + motion.Relative.Y * LookSpeed, -PitchLimit, PitchLimit);

        }

    }

    public void MakeCurrent() => _camera.Current = true;

    /// <summary>Swings the arm so the camera looks along the given direction.</summary>
    public void AimAt(Vector3 direction) {

        Vector3 arm = -direction.Normalized();

        Pitch = Mathf.Clamp(Mathf.Asin(arm.Y), -PitchLimit, PitchLimit);
        Yaw = Mathf.Atan2(arm.X, arm.Z);

    }

    /// <summary>Swings the arm and then lifts the eye clear of the ground, so a chase view close to
    /// the surface cannot end up looking at the inside of a hill.</summary>
    public void Sync(Vector3 focus, Vector3 centre, float floor) {

        Vector3 vertical = (focus - centre).Normalized();

        Vector3 arm = new Vector3(

            Mathf.Cos(Pitch) * Mathf.Sin(Yaw),
            Mathf.Sin(Pitch),
            Mathf.Cos(Pitch) * Mathf.Cos(Yaw)

        );

        Position = focus;

        Vector3 eye = focus + arm * Distance;

        Vector3 radial = eye - centre;

        float height = radial.Length();

        if (height > 0.0f && height < floor) {

            eye = centre + radial * (floor / height);

        }

        _camera.GlobalPosition = eye;

        // Rolled to the local vertical rather than to the world's: at any latitude but the equator
        // the two are different, and on the ground the difference is a horizon that leans.
        Vector3 look = (GlobalPosition - eye).Normalized();

        _camera.LookAt(GlobalPosition, Mathf.Abs(look.Dot(vertical)) > 0.999f ? Vector3.Up : vertical);

    }

}
