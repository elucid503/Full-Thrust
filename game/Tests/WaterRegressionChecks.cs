using System;
using System.Collections;
using System.Reflection;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class WaterRegressionChecks : Node {

    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private Main _main;
    private Flight _flight;
    private Vector3d _shore;
    private Vector3d _offshore;
    private Vector3d _east;
    private Vector3d _north;
    private ShaderMaterial[] _faces;
    private int _frames;
    private int _checks;
    private bool _closeOnly;
    private bool _stress;
    private bool _shoreClose;

    private void Check(bool condition, string label) {

        if (!condition) { throw new InvalidOperationException(label); }
        _checks++;
        GD.Print("PASS " + label);

    }

    public override void _Ready() {

        string ground = FileAccess.GetFileAsString("res://Shaders/Ground.gdshader");
        int vertex = ground.IndexOf("void vertex()", StringComparison.Ordinal);
        int open = ground.IndexOf('{', vertex);
        int depth = 0;
        int close = -1;
        for (int i = open; i < ground.Length; i++) {

            if (ground[i] == '{') { depth++; }
            else if (ground[i] == '}') { depth--; if (depth == 0) { close = i; break; } }

        }
        Check(vertex >= 0 && open > vertex && close > open, "ground shader has a vertex stage");
        Check(!ground.Substring(open, close - open).Contains("shoreline_", StringComparison.Ordinal),
            "terrain vertices do not fetch the shoreline survey");
        ProcessPriority = 100;
        _main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
        AddChild(_main);
        _flight = Flight.Active;
        _flight.DebugPaused = true;
        _main.GetNode<CanvasLayer>("Hud").Hide();
        double bestShore = double.MaxValue;
        double bestOcean = double.MaxValue;
        for (int i = 0; i <= 1200; i++) {

            double latitude = 28.52 * Math.PI / 180.0;
            double longitude = (-80.64 + i * 0.0003) * Math.PI / 180.0;
            Vector3d direction = new(Math.Cos(latitude) * Math.Cos(longitude), Math.Cos(latitude) * Math.Sin(longitude), Math.Sin(latitude));
            _flight.Body.Terrain.Elevation(direction, 0.0, out double coast);
            if (Math.Abs(coast) < bestShore) { bestShore = Math.Abs(coast); _shore = direction; }
            if (Math.Abs(coast + 8.0) < bestOcean) { bestOcean = Math.Abs(coast + 8.0); _offshore = direction; }

        }
        _east = Vector3d.Cross(Vector3d.UnitZ, _shore).Normalized;
        _north = Vector3d.Cross(_shore, _east);
        _faces = (ShaderMaterial[])typeof(Planet).GetField("_faces", Fields).GetValue(Planet.Active);
        SetMask(true);
        FreeCamera.Active.Take(OrbitCamera.Active);
        CameraAt(_shore * (_flight.Body.Radius + 180.0), _shore * _flight.Body.Radius + _north);
        _closeOnly = Array.IndexOf(OS.GetCmdlineUserArgs(), "--close-only") >= 0;
        _stress = Array.IndexOf(OS.GetCmdlineUserArgs(), "--stress") >= 0;
        _shoreClose = Array.IndexOf(OS.GetCmdlineUserArgs(), "--shore-close") >= 0;
        if (_shoreClose) {

            SetMask(false);
            CameraAt(_shore * (_flight.Body.Radius + 8.0) - _north * 10.0, _shore * _flight.Body.Radius);

        }
        if (_stress) { SetMask(false); }
        if (_closeOnly) { _frames = 949; }

    }

    private void SetMask(bool mask) {

        foreach (ShaderMaterial face in _faces) { face.SetShaderParameter("shoreline_debug", mask); }
        foreach (string path in new[] { "Planet/Clouds", "Planet/Atmosphere", "Planet/Forest", "Planet/DistantForest", "Planet/GroundScatter", "Planet/BroadScatter" }) {

            _main.GetNodeOrNull<Node3D>(path)?.Set("visible", !mask);

        }

    }

    private void CameraAt(Vector3d fixedPosition, Vector3d fixedTarget) {

        FreeCamera camera = FreeCamera.Active;
        typeof(FreeCamera).GetField("_where", Fields).SetValue(camera, fixedPosition);
        Vector3 up = Frames.Direction(_flight.Body.ToInertial(fixedPosition.Normalized, _flight.Time));
        Vector3 forward = Frames.Direction(_flight.Body.ToInertial((fixedTarget - fixedPosition).Normalized, _flight.Time));
        Frames.Horizon(up, out Vector3 side, out Vector3 ahead);
        typeof(FreeCamera).GetField("_yaw", Fields).SetValue(camera, Mathf.Atan2(-forward.Dot(side), forward.Dot(ahead)));
        typeof(FreeCamera).GetField("_pitch", Fields).SetValue(camera, Mathf.Clamp(Mathf.Asin(forward.Dot(up)), -1.52f, 1.52f));

    }

    private void CheckShore(string label) {

        using Image shot = GetViewport().GetTexture().GetImage();
        shot.SavePng($"res://.artifacts/shore-{label}.png");
        Camera3D camera = FreeCamera.Active;
        int samples = 0;
        int failed = 0;
        int water = 0;
        int land = 0;
        for (int y = -10; y <= 10; y++) {

            for (int x = -10; x <= 10; x++) {

                Vector3d direction = (_shore * _flight.Body.Radius + _east * (x * 15.0) + _north * (y * 15.0)).Normalized;
                double height = _flight.Body.Terrain.Elevation(direction, 0.0, out double coast);
                if (Math.Abs(coast) < 0.35) { continue; }
                Vector3 target = Frames.Point(_flight.Body.ToInertial(direction * (_flight.Body.Radius + Math.Max(height, 0.0)), _flight.Time));
                if (camera.IsPositionBehind(target)) { continue; }
                Vector2 pixel = camera.UnprojectPosition(target);
                int px = (int)pixel.X;
                int py = (int)pixel.Y;
                if (px < 3 || py < 3 || px >= shot.GetWidth() - 3 || py >= shot.GetHeight() - 3) { continue; }
                bool expected = coast < 0.0;
                bool actual = shot.GetPixel(px, py).R > 0.4f;
                samples++;
                if (expected) { water++; } else { land++; }
                if (expected != actual) { failed++; }

            }

        }
        Check(samples > 40 && land > 10 && water > 10, $"{label}: samples both land and water ({land}/{water})");
        Check(failed <= samples * 0.03, $"{label}: coastline matches fixed geography ({failed}/{samples} mismatches)");

    }

    public override void _Process(double delta) {

        try {

            _frames++;
            if (_shoreClose) {

                if (_frames == 350) {

                    using Image shot = GetViewport().GetTexture().GetImage();
                    shot.SavePng("res://.artifacts/shore-close.png");
                    GD.Print("Close shoreline capture complete");
                    GetTree().Quit();

                }
                return;

            }
            if (_stress) {

                StressCamera();
                return;

            }
            _main.GetNode<CanvasLayer>("Hud").Hide();
            if (_frames == 350) {

                CheckShore("near");
                CameraAt(_shore * (_flight.Body.Radius + 2100.0) + _east * 6500.0, _shore * _flight.Body.Radius);

            }
            if (_frames == 650) {

                CheckShore("travel-rebase");
                typeof(Flight).GetProperty("Time").SetValue(_flight, 37.0);
                CameraAt(_shore * (_flight.Body.Radius + 450.0) - _north * 380.0, _shore * _flight.Body.Radius);

            }
            if (_frames == 950) {

                if (!_closeOnly) { CheckShore("time-and-direction"); }
                SetMask(false);
                _flight.PlaceAt(28.52, -80.5, 40.0, 0.0);
                _flight.Vessel.Orientation = QuaternionD.LookAlong(_flight.Body.ToInertial(_offshore, _flight.Time), Vector3d.UnitZ);
                _flight.Vessel.Position = _flight.Body.ToInertial(_offshore * (_flight.Body.Radius + _flight.Vessel.CentreOfMassZ - _flight.Vessel.Base + 5.0), _flight.Time);
                _flight.Vessel.Throttle = 0.5;
                CameraAt(_offshore * (_flight.Body.Radius + 38.0) + _east * 32.0,
                    _offshore * (_flight.Body.Radius + 12.0));

            }
            if (_frames > 950) {

                double time = 37.0 + (_frames - 950) / 60.0;
                typeof(Flight).GetProperty("Time").SetValue(_flight, time);
                Vector3d direction = (_offshore * _flight.Body.Radius + _north * Math.Sin(time * 0.3) * 9.0).Normalized;
                _flight.Vessel.Position = _flight.Body.ToInertial(direction * (_flight.Body.Radius + _flight.Vessel.CentreOfMassZ - _flight.Vessel.Base + 5.0), time);
                _flight.Vessel.Orientation = QuaternionD.LookAlong(_flight.Body.ToInertial(direction, time), Vector3d.UnitZ);

            }
            if (_frames == 1250) {

                using Image shot = GetViewport().GetTexture().GetImage();
                shot.SavePng("res://.artifacts/water-close-exhaust.png");
                IList wakes = (IList)typeof(Planet).GetField("_surfaceWakes", Fields).GetValue(Planet.Active);
                int sprays = 0;
                foreach (object wake in wakes) {

                    if (!(bool)wake.GetType().GetField("Water").GetValue(wake)) { continue; }
                    IList puffs = (IList)wake.GetType().GetField("Puffs").GetValue(wake);
                    Check(puffs.Count == 0, "water exhaust does not spawn detached spherical puffs");
                    sprays++;

                }
                Check(sprays > 0, "moving exhaust produces a bounded water spray");
                Check(Planet.Active.WorkerFailures == 0, "camera travel produces no terrain worker failures");
                GD.Print($"Water regression: {_checks} checks passed");
                GetTree().Quit();

            }

        } catch (Exception exception) {

            GD.PushError(exception.ToString());
            GetTree().Quit(1);

        }

    }

    private void StressCamera() {

        double phase = (_frames % 900) / 900.0;
        double height = phase < 0.25 ? 2000.0 * Math.Pow(1.0 - phase * 4.0, 2.0) + 1.0 : 1.0 + 4.0 * Math.Sin(phase * Math.Tau);
        double travel = Math.Min(_frames % 900, 260) + _frames / 900 * 900;
        Vector3d direction = (_offshore * _flight.Body.Radius + _east * (Math.Sin(travel * 0.007) * 8000.0)
            + _north * (Math.Cos(travel * 0.011) * 2000.0)).Normalized;
        Vector3d location = direction * (_flight.Body.Radius + height);
        CameraAt(location, direction * _flight.Body.Radius + _east * 15.0);
        if (_frames % 300 == 0) {

            GD.Print($"Water stress {_frames}: patches {Planet.Active.PatchCount}, jobs {Planet.Active.PendingJobs}, memory {System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / 1048576} MiB");

        }
        if (_frames == 4500) {

            Check(Planet.Active.WorkerFailures + Planet.Active.ForestFailures + Planet.Active.ScatterFailures == 0, "rapid coastal camera travel has no worker failures");
            GD.Print("Water stress complete: 4500 frames, repeated dives and coastal travel");
            GetTree().Quit();

        }

    }

}
