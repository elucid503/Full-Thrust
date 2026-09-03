using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The planning camera. It frames the whole conic and draws the path the vessel is on.</summary>
public sealed partial class MapView : Node3D {

    private const float Minimum = 250_000.0f;
    private const float Maximum = 15_000_000.0f;

    private const float PitchLimit = 1.50f;
    private const float LookSpeed = 0.006f;

    public static MapView Active { get; private set; }

    public bool Open { get; private set; }

    public Camera3D Camera => _camera;

    private Flight _flight;
    private Camera3D _camera;

    private MapPath _path;

    private float _yaw = 0.9f;
    private float _pitch = 0.55f;
    private float _distance = 4_500_000.0f;

    private bool _dragging;

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

    }

    public void Toggle() {

        Open = !Open;

        if (Open) {

            Frame();

        }

        _camera.Current = Open;

        if (!Open) {

            OrbitCamera.Active?.MakeCurrent();

        }

    }

    private void Frame() {

        Orbit orbit = _flight.Orbit;

        double reach = orbit.IsClosed ? orbit.ApoapsisRadius : _flight.Body.Radius * 3.0;

        _distance = Mathf.Clamp((float)(reach * 2.4), Minimum, Maximum);

    }

    public override void _UnhandledInput(InputEvent @event) {

        if (!Open) {

            return;

        }

        if (@event is InputEventMouseButton button) {

            if (button.ButtonIndex == MouseButton.Right) {

                _dragging = button.Pressed;

            }

            if (button.ButtonIndex == MouseButton.WheelUp) {

                _distance = Mathf.Clamp(_distance * 0.86f, Minimum, Maximum);

            }

            if (button.ButtonIndex == MouseButton.WheelDown) {

                _distance = Mathf.Clamp(_distance * 1.16f, Minimum, Maximum);

            }

        }

        if (@event is InputEventMouseMotion motion && _dragging) {

            _yaw -= motion.Relative.X * LookSpeed;
            _pitch = Mathf.Clamp(_pitch + motion.Relative.Y * LookSpeed, -PitchLimit, PitchLimit);

        }

    }

    public void Sync(double delta) {

        if (!Open) {

            return;

        }

        Vector3 focus = Frames.Point(_flight.Vessel.Position);

        Vector3 arm = new Vector3(

            Mathf.Cos(_pitch) * Mathf.Sin(_yaw),
            Mathf.Sin(_pitch),
            Mathf.Cos(_pitch) * Mathf.Cos(_yaw)

        );

        _camera.Position = focus + arm * _distance;
        _camera.LookAt(focus, Vector3.Up);

    }

}

/// <summary>The conic, projected from the map camera and stroked in 2D so the line stays even at any range.</summary>
public sealed partial class MapPath : Control {

    private const int Samples = 320;

    private static readonly Color Ink = new Color(0.72f, 0.82f, 0.92f);

    private Flight _flight;
    private MapView _map;

    public void Bind(Flight flight, MapView map) {

        _flight = flight;
        _map = map;

        SetAnchorsPreset(LayoutPreset.FullRect);

        MouseFilter = MouseFilterEnum.Ignore;

    }

    public override void _Process(double delta) {

        Visible = _map.Open;

        if (Visible) {

            QueueRedraw();

        }

    }

    public override void _Draw() {

        Orbit orbit = _flight.Orbit;

        double limit = orbit.IsClosed ? Math.PI : orbit.TrueAnomalyLimit * 0.985;
        double span = orbit.IsClosed ? Math.Tau : limit * 2.0;

        // Drawn from the vessel forward and fading the whole way round, so the line carries a direction.
        double start = orbit.IsClosed ? orbit.TrueAnomalyAt(_flight.Time) : -limit;

        List<Vector2> run = new List<Vector2>(Samples);
        List<Color> tint = new List<Color>(Samples);

        for (int sample = 0; sample <= Samples; sample++) {

            double fraction = (double)sample / Samples;

            Vector3 world = Frames.Point(orbit.PositionAtTrueAnomaly(start + span * fraction));

            if (_map.Camera.IsPositionBehind(world)) {

                Stroke(run, tint);

                continue;

            }

            run.Add(_map.Camera.UnprojectPosition(world));
            tint.Add(Ink * new Color(1.0f, 1.0f, 1.0f, Mathf.Lerp(0.95f, 0.24f, (float)fraction)));

        }

        Stroke(run, tint);

    }

    private void Stroke(List<Vector2> run, List<Color> tint) {

        if (run.Count >= 2) {

            DrawPolylineColors(run.ToArray(), tint.ToArray(), 2.0f, true);

        }

        run.Clear();
        tint.Clear();

    }

}
