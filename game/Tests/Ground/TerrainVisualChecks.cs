using System;
using System.Collections;
using System.Reflection;
using FullThrust.Sim;
using Godot;
namespace FullThrust.Game;

// Run with Vulkan: captures settled vegetation and reports total viewport GPU cost.
public sealed partial class TerrainVisualChecks : Node {
    private Main _main;
    private int _frames;
    private double _gpu;
    private int _samples;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public override void _Ready() {
        ProcessPriority = 100;
        _main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
        AddChild(_main);
        Flight.Active.DebugPaused = true;
        _main.GetNode<CanvasLayer>("Hud").Hide();
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
    }
    public override void _Process(double delta) {
        _frames++;
        _main.GetNode<CanvasLayer>("Hud").Hide();
        if (_frames == 450) {
            Forest forest = (Forest)typeof(Planet).GetField("_forest", Private).GetValue(Planet.Active);
            IDictionary groves = (IDictionary)typeof(Forest).GetField("_groves", Private).GetValue(forest);
            foreach (object grove in groves.Values) {
                Transform3D[] trees = (Transform3D[])grove.GetType().GetField("Trees").GetValue(grove);
                if (trees.Length == 0) { continue; }
                Vector3d anchor = (Vector3d)grove.GetType().GetField("Anchor").GetValue(grove);
                Vector3d tree = anchor + Frames.Sim(trees[0].Origin);
                Vector3d up = tree.Normalized;
                Vector3d east = Vector3d.Cross(Vector3d.UnitZ, up).Normalized;
                Vector3d at = tree - east * 22 + up * 7;
                FreeCamera camera = FreeCamera.Active;
                camera.Take(OrbitCamera.Active);
                typeof(FreeCamera).GetField("_where", Private).SetValue(camera, at);
                Vector3 vertical = Frames.Direction(Flight.Active.Body.ToInertial(up, Flight.Active.Time));
                Frames.Horizon(vertical, out Vector3 side, out Vector3 ahead);
                Vector3 forward = Frames.Direction(Flight.Active.Body.ToInertial(east, Flight.Active.Time));
                typeof(FreeCamera).GetField("_yaw", Private).SetValue(camera, Mathf.Atan2(-forward.Dot(side), forward.Dot(ahead)));
                typeof(FreeCamera).GetField("_pitch", Private).SetValue(camera, -0.10f);
                break;
            }
        }
        if (_frames >= 800 && _frames < 860) {
            _gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
            _samples++;
        }
        if (_frames == 860) {
            using Image shot = GetViewport().GetTexture().GetImage();
            shot.SavePng("res://.artifacts/tree-models.png");
            GD.Print($"TERRAIN GPU {_gpu / _samples:F3} ms; trees {Planet.Active.TreeCount}; scatter {Planet.Active.ScatterCount}; pending {Planet.Active.ForestPending + Planet.Active.ScatterPending}; failures {Planet.Active.WorkerFailures}");
            GetTree().Quit();
        }
    }
}
