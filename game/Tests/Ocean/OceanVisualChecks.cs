using System;
using System.Reflection;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class OceanVisualChecks : Node {

    private Main _main;
    private int _frames;
    private double _gpu;
    private int _samples;
    private bool _aerial;

    public override void _Ready() {

        ProcessPriority = 100;
        _main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
        AddChild(_main);
        Flight flight = Flight.Active;
        flight.DebugPaused = true;
        _main.GetNode<CanvasLayer>("Hud").Hide();
        double best = double.MaxValue;
        Vector3d shore = Weather.Origin;
        for (int index = 0; index <= 400; index++) {

            double latitude = 28.52 * Math.PI / 180.0;
            double longitude = (-80.56 + index * 0.0007) * Math.PI / 180.0;
            Vector3d up = new(Math.Cos(latitude) * Math.Cos(longitude), Math.Cos(latitude) * Math.Sin(longitude), Math.Sin(latitude));
            double elevation = flight.Body.Terrain.Elevation(up);
            if (Math.Abs(elevation + 6.0) < best) { best = Math.Abs(elevation + 6.0); shore = up; }

        }
        Vector3d east = Vector3d.Cross(Vector3d.UnitZ, shore).Normalized;
        Vector3d north = Vector3d.Cross(shore, east);
        _aerial = Array.IndexOf(OS.GetCmdlineUserArgs(), "--aerial") >= 0;
        Vector3d location = shore * (flight.Body.Radius + (_aerial ? 2200.0 : 5.0));
        FreeCamera camera = FreeCamera.Active;
        camera.Take(OrbitCamera.Active);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(FreeCamera).GetField("_where", fields).SetValue(camera, location);
        Vector3 vertical = Frames.Direction(shore);
        Frames.Horizon(vertical, out Vector3 side, out Vector3 ahead);
        Vector3 forward = Frames.Direction((-east * 0.65 + north * 0.76).Normalized);
        bool glint = _aerial || Array.IndexOf(OS.GetCmdlineUserArgs(), "--glint") >= 0;
        if (glint) { forward = (Main.SunDirection - vertical * Main.SunDirection.Dot(vertical)).Normalized(); }
        typeof(FreeCamera).GetField("_yaw", fields).SetValue(camera, Mathf.Atan2(-forward.Dot(side), forward.Dot(ahead)));
        typeof(FreeCamera).GetField("_pitch", fields).SetValue(camera, glint ? -Mathf.Asin(Main.SunDirection.Dot(vertical)) : -0.13f);
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--native") >= 0) { GetViewport().Scaling3DScale = 1.0f; }
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        GD.Print($"Ocean visual shoreline residual {best:F3} m; offshore bed {flight.Body.Terrain.Elevation(location):F2} m");

    }

    public override void _Process(double delta) {

        _frames++;
        _main.GetNode<CanvasLayer>("Hud").Hide();
        if (_frames > 350 && _frames <= 410) {

            _gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
            _samples++;

        }
        if (_frames == 410) {

            using Image shot = GetViewport().GetTexture().GetImage();
            shot.SavePng(_aerial ? "res://.artifacts/ocean-aerial-fair.png" : "res://.artifacts/ocean-fair.png");
            GD.Print($"Ocean fair GPU {_gpu / _samples:F2} ms");
            Flight.Active.Body.Weather.SetPreset(WeatherPreset.Gale, -500.0);
            if (_aerial) { _main.GetNode<Node3D>("Planet/Clouds").Hide(); }

        }
        if (_frames == 580) {

            using Image shot = GetViewport().GetTexture().GetImage();
            shot.SavePng(_aerial ? "res://.artifacts/ocean-aerial-gale.png" : "res://.artifacts/ocean-gale.png");
            DebugPanel panel = _main.GetNode<DebugPanel>("Debug");
            panel._UnhandledKeyInput(new InputEventKey { Keycode = Key.F1, Pressed = true });

        }
        if (_frames == 600) {

            using Image shot = GetViewport().GetTexture().GetImage();
            shot.SavePng("res://.artifacts/ocean-f1.png");
            GD.Print($"Ocean visual checks complete; workers failed {Planet.Active.WorkerFailures}");
            GetTree().Quit();

        }

    }

}
