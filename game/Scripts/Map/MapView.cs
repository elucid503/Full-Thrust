using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The planning camera, and the pointer that plans with it. It frames the conic about the
/// body, hands its projection to the furniture, and turns a drag on the node into a delta-v.</summary>
public sealed partial class MapView : Node3D {

    private const float Minimum = 120_000.0f;
    private const float Maximum = 15_000_000.0f;

    private const float PitchLimit = 1.50f;
    private const float LookSpeed = 0.006f;

    // The handle is logarithmic. A pixel by the node is worth a metre a second and a pixel out at
    // arm reach is worth hundreds, because one linear scale cannot carry a 1 m/s trim and a 3 km/s
    // ejection at the same time. The ruler drawn round the node is what makes that mapping readable.
    private const float DragDead = 4.0f;
    private const float DragFold = 37.5f;

    public static MapView Active { get; private set; }

    public bool Open { get; private set; }

    public Camera3D Camera => _camera;

    /// <summary>Where the handle sits with nothing dialled in, and where the hand has taken it.</summary>
    public Vector2 DragOrigin { get; private set; }
    public Vector2 DragHandle { get; private set; }

    public bool Dragging { get; private set; }

    /// <summary>Whether the drag's vertical is the camera's depth rather than its up.</summary>
    public bool Deep { get; private set; }

    /// <summary>The node handle's place on screen, for anything that has to aim at it.</summary>
    public Vector2 NodeAt => _path.Node;
    public bool NodeLive => _path.NodeLive;

    private Flight _flight;
    private Camera3D _camera;

    private MapPath _path;
    private MapHud _hud;

    private float _yaw = 0.9f;
    private float _pitch = 0.55f;
    private float _distance = 4_500_000.0f;

    private bool _looking;
    private bool _slipping;

    private bool _chase;

    // Frozen at the grab: whatever of the impulse the two live axes cannot express is not the
    // hand's to change, so it rides through the drag untouched rather than being quietly zeroed.
    private Vector3d _residual;

    public override void _Ready() {

        Active = this;

        _flight = GetNode<Flight>("../Flight");

        _camera = new Camera3D {

            Name = "MapCamera",

            // The conic runs to a million metres and the planet is a million wide, so the frustum is opened out.
            Near = 2_000.0f,
            Far = 60_000_000.0f,

            Fov = 48.0f,
            Current = false,

        };

        AddChild(_camera);

        CanvasLayer overlay = new CanvasLayer { Name = "Overlay" };

        AddChild(overlay);

        _path = new MapPath();

        overlay.AddChild(_path);

        _path.Bind(_flight, this);

        _hud = new MapHud { Name = "MapHud", Layer = 1 };

        AddChild(_hud);

        _hud.Build(_flight, this);

    }

    public void Toggle() {

        Open = !Open;

        if (Open) {

            Frame();

        }
        else {

            _hud.Dismiss();

            Dragging = false;
            _slipping = false;
            _looking = false;

        }

        _camera.Current = Open;

        if (!Open) {

            OrbitCamera.Active?.MakeCurrent();

        }

    }

    public void Sync(double delta) {

        _hud.Visible = Open;

        if (!Open) {

            return;

        }

        Vector3 focus = Focus();

        Vector3 arm = new Vector3(

            Mathf.Cos(_pitch) * Mathf.Sin(_yaw),
            Mathf.Sin(_pitch),
            Mathf.Cos(_pitch) * Mathf.Cos(_yaw)

        );

        _camera.Position = focus + arm * _distance;
        _camera.LookAt(focus, Vector3.Up);

        _hud.Sync();

    }

    /// <summary>Pixels out from the handle's origin that stand for a given impulse.</summary>
    public static float Reach(double metresPerSecond) {

        if (metresPerSecond <= 0.0) {

            return 0.0f;

        }

        return DragDead + DragFold * (float)Math.Log(metresPerSecond + 1.0);

    }

    /// <summary>The impulse a pull of this many pixels stands for.</summary>
    public static double Impulse(float pixels) => Math.Exp(Math.Max(pixels - DragDead, 0.0f) / DragFold) - 1.0;

    // The body is what the plan is drawn against, so it is what the camera hangs off: the picture
    // holds still while the vessel travels its conic, and warp does not slide the frame away.
    private Vector3 Focus() => _chase ? Frames.Point(_flight.Vessel.Position) : Frames.Point(Vector3d.Zero);

    private void Frame() {

        Orbit orbit = _flight.Orbit;

        double reach = orbit.IsClosed ? orbit.ApoapsisRadius : _flight.Body.Radius * 3.0;

        // Perspective, not orthography: the near side of the conic is nearer than the body the
        // camera is aimed at, so framing it to the far side alone runs it off the bottom of the screen.
        _distance = Mathf.Clamp((float)(reach * 3.4), Floor(), Maximum);

    }

    private float Floor() => _chase ? Minimum : (float)(_flight.Body.Radius * 1.25);

    private void Zoom(float factor) {

        _distance = Mathf.Clamp(_distance * factor, Floor(), Maximum);

    }

    public override void _UnhandledInput(InputEvent @event) {

        if (!Open) {

            return;

        }

        if (@event is InputEventMouseButton button) {

            Press(button);

        }

        if (@event is InputEventMouseMotion motion) {

            Motion(motion);

        }

    }

    private void Press(InputEventMouseButton button) {

        Vector2 at = button.Position;

        if (button.ButtonIndex == MouseButton.WheelUp) {

            Zoom(0.86f);

            return;

        }

        if (button.ButtonIndex == MouseButton.WheelDown) {

            Zoom(1.16f);

            return;

        }

        if (button.ButtonIndex == MouseButton.Right) {

            if (!button.Pressed) {

                _slipping = false;
                _looking = false;

                return;

            }

            // A grab that lands on the node slips the burn; anything else is the camera, which is on
            // the same button. The node is tested first because it is the smaller of the two targets.
            if (_flight.Node != null && _path.PickNode(at)) {

                _slipping = true;

                return;

            }

            _looking = true;

            return;

        }

        if (button.ButtonIndex != MouseButton.Left) {

            return;

        }

        if (!button.Pressed) {

            Dragging = false;

            return;

        }

        if (button.DoubleClick) {

            _chase = _path.PickVessel(at);

            _distance = Mathf.Max(_distance, Floor());

            return;

        }

        if (_flight.Node != null && _path.PickNode(at)) {

            Deep = Input.IsKeyPressed(Key.Shift);

            Grab(at);

            return;

        }

        MapPath.Mark mark = _path.PickMark(at);

        if (mark != null) {

            _hud.Open(mark);

            return;

        }

        if (_path.PickConic(at, out double anomaly)) {

            _flight.PlaceNode(anomaly);

        }

        _hud.Dismiss();

    }

    private void Motion(InputEventMouseMotion motion) {

        // The node retires itself a couple of seconds after the vessel passes it, so a drag can
        // outlive what it was dragging.
        if (_flight.Node == null) {

            Dragging = false;
            _slipping = false;

        }

        if (Dragging) {

            bool deep = Input.IsKeyPressed(Key.Shift);

            // Swapping the plane mid-drag re-seats the handle on what is already dialled in, so the
            // impulse carries across the change rather than jumping to whatever the old pull meant.
            if (deep != Deep) {

                Deep = deep;

                Grab(motion.Position);

            }

            Haul(motion.Position);

            return;

        }

        if (_slipping) {

            if (_path.PickSlip(motion.Position, out double anomaly)) {

                _flight.RetimeNode(anomaly);

            }

            return;

        }

        if (_looking) {

            _yaw -= motion.Relative.X * LookSpeed;
            _pitch = Mathf.Clamp(_pitch + motion.Relative.Y * LookSpeed, -PitchLimit, PitchLimit);

        }

    }

    private (Vector3d Horizontal, Vector3d Vertical) Axes() {

        Basis basis = _camera.GlobalTransform.Basis;

        Vector3d horizontal = Frames.Sim(basis.X.Normalized());

        return (horizontal, Frames.Sim(Deep ? (-basis.Z).Normalized() : basis.Y.Normalized()));

    }

    private void Grab(Vector2 at) {

        (Vector3d horizontal, Vector3d vertical) = Axes();

        Vector3d impulse = _flight.Node.WorldDeltaV(_flight.Orbit);

        double along = Vector3d.Dot(impulse, horizontal);
        double across = Vector3d.Dot(impulse, vertical);

        _residual = impulse - horizontal * along - vertical * across;

        double magnitude = Math.Sqrt(along * along + across * across);

        Vector2 pull = Vector2.Zero;

        if (magnitude > 0.0) {

            pull = new Vector2((float)(along / magnitude), (float)(-across / magnitude)) * Reach(magnitude);

        }

        DragOrigin = at - pull;
        DragHandle = at;

        Dragging = true;

    }

    private void Haul(Vector2 at) {

        (Vector3d horizontal, Vector3d vertical) = Axes();

        Vector2 pull = at - DragOrigin;

        float length = pull.Length();

        Vector3d impulse = _residual;

        if (length > DragDead) {

            double magnitude = Impulse(length);

            Vector2 unit = pull / length;

            impulse += horizontal * (unit.X * magnitude) + vertical * (-unit.Y * magnitude);

        }

        DragHandle = at;

        Dial(impulse);

    }

    // The plan is carried in the orbital triad, not in world axes, so a node slipped round the orbit
    // keeps the burn it was given instead of swinging with the frame it happened to be dialled in.
    private void Dial(Vector3d impulse) {

        Maneuver node = _flight.Node;

        (Vector3d position, Vector3d velocity) = _flight.Orbit.StateAt(node.Time);

        (Vector3d prograde, Vector3d normal, Vector3d radial) = Maneuver.Frame(position, velocity);

        node.Prograde = Vector3d.Dot(impulse, prograde);
        node.Normal = Vector3d.Dot(impulse, normal);
        node.Radial = Vector3d.Dot(impulse, radial);

    }

}
