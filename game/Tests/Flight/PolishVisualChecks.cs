using System;
using System.Reflection;
using System.Threading.Tasks;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class PolishVisualChecks : Node {

    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    private async Task Settle() {

        ulong started = Time.GetTicksMsec();
        do {

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Time.GetTicksMsec() - started > 60000) { throw new InvalidOperationException("Terrain did not settle"); }

        } while (SceneTransition.Loading);

        for (int i = 0; i < 40; i++) { await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }

    }

    public override async void _Ready() {

        try {

            Main main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            Flight.Active.DebugPaused = true;
            main.GetNode<CanvasLayer>("Hud").Hide();
            await Settle();
            FreeCamera camera = FreeCamera.Active;
            camera.Take(OrbitCamera.Active);
            foreach (var site in new[] {

                (Name: "coastal-water", Latitude: 28.52, Longitude: -80.50, Height: 120.0, Pitch: -0.40f, Yaw: 1.57f),
                (Name: "south-florida", Latitude: 25.3, Longitude: -80.8, Height: 350.0, Pitch: -0.60f, Yaw: 0.4f),
                (Name: "island-water", Latitude: 24.60, Longitude: -81.65, Height: 550.0, Pitch: -0.9f, Yaw: 0.4f),

            }) {

                double lat = site.Latitude * Math.PI / 180.0;
                double lon = site.Longitude * Math.PI / 180.0;
                Vector3d up = new(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
                double ground = Math.Max(Flight.Active.Body.Terrain.Elevation(up), 0.0);
                typeof(FreeCamera).GetField("_where", Fields).SetValue(camera, up * (Flight.Active.Body.Radius + ground + site.Height));
                typeof(FreeCamera).GetField("_yaw", Fields).SetValue(camera, site.Yaw);
                typeof(FreeCamera).GetField("_pitch", Fields).SetValue(camera, site.Pitch);
                SceneTransition.Begin(this);
                await Settle();
                if (Planet.Active.WorkerFailures + Planet.Active.ForestFailures + Planet.Active.ScatterFailures != 0) {

                    throw new InvalidOperationException("Visual streaming reported a worker failure");

                }
                using Image shot = GetViewport().GetTexture().GetImage();
                shot.SavePng($"res://.artifacts/polish-{site.Name}.png");
                GD.Print($"CAPTURE {site.Name}: ground {ground:F2} m, {Planet.Active.PatchCount} patches, {Planet.Active.WorkerFailures} failures");

            }
            GetTree().Quit();

        } catch (Exception exception) {

            GD.PushError(exception.ToString());
            GetTree().Quit(1);

        }

    }

}
