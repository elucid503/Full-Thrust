using System;
using System.Threading.Tasks;

using static FullThrust.Game.Checks;

using Godot;

namespace FullThrust.Game;

public sealed partial class CloudPerformanceChecks : Node {

    private async Task Frames(int count) {

        for (int i = 0; i < count; i++) {

            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        }

    }

    public override async void _Ready() {

        try {

            Node main = GD.Load<PackedScene>("res://Main.tscn").Instantiate();
            AddChild(main);
            Flight flight = Flight.Active;
            flight.DebugPaused = true;
            Viewport viewport = GetViewport();
            RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), true);
            for (int i = 0; i < 3600 && (!Planet.Active.CloudTexturesReady || !Planet.Active.CloudPass.Ready); i++) { await Frames(1); }
            if (!Planet.Active.CloudTexturesReady) { throw new InvalidOperationException("Cloud textures did not finish generating"); }
            if (!Planet.Active.CloudPass.Ready) { throw new InvalidOperationException("Cloud compute pass did not initialize"); }

            string label = OS.GetEnvironment("FT_CLOUD_BENCHMARK");
            if (string.IsNullOrEmpty(label)) { label = "review"; }
            string directory = "res://.artifacts/cloud-" + label;
            DirAccess.MakeDirRecursiveAbsolute(directory);
            GD.Print($"Cloud benchmark: {viewport.GetVisibleRect().Size}, scale {viewport.Scaling3DScale}, {RenderingServer.GetVideoAdapterName()}");
            foreach ((string name, double altitude, float pitch) in new[] {

                ("below", 600.0, -0.10f), ("inside", 1800.0, 0.0f),
                ("above", 4200.0, 0.08f), ("grazing", 10000.0, 0.10f), ("orbit", 100000.0, 0.3f),
                ("powered", 1800.0, 0.0f),

            }) {

                flight.Place(altitude, 0.0);
                flight.DebugPaused = name != "powered";
                flight.Vessel.Throttle = name == "powered" ? 1.0 : 0.0;
                OrbitCamera.Active.Pitch = pitch;
                OrbitCamera.Active.Distance = 35.0f;
                await Frames(120);
                double[] gpu = new double[120];
                for (int i = 0; i < gpu.Length; i++) {

                    await Frames(1);
                    gpu[i] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport.GetViewportRid());

                }
                Array.Sort(gpu);
                GD.Print($"BENCH {name}: GPU median {gpu[60]:F3} ms, p95 {gpu[114]:F3} ms, max {gpu[119]:F3} ms");
                using Image image = viewport.GetTexture().GetImage();
                image.SavePng($"{directory}/{name}.png");

            }
            GetTree().Quit();

        } catch (Exception exception) {

            Fail(this, exception);

        }

    }

}
