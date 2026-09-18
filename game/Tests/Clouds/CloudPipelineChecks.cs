using System;
using System.Reflection;
using System.Threading.Tasks;

using Godot;

namespace FullThrust.Game;

public sealed partial class CloudPipelineChecks : Node {

    private async Task Frames(int count) {

        for (int i = 0; i < count; i++) { await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }

    }

    private static void Check(bool condition, string message) {

        if (!condition) { throw new InvalidOperationException(message); }
        GD.Print("PASS " + message);

    }

    private Image Capture() {

        Image image = GetViewport().GetTexture().GetImage();
        image.Convert(Image.Format.Rgb8);
        return image;

    }

    public override async void _Ready() {

        try {

            Main main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            Check(DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen, "game starts fullscreen");
            Check(main.GetNodeOrNull("GraphicsOptions") == null, "graphics settings controls are absent");
            Flight.Active.DebugPaused = true;
            main.GetNode<WorldEnvironment>("WorldEnvironment").CameraAttributes.AutoExposureEnabled = false;
            GetTree().Root.Mode = Window.ModeEnum.Windowed;
            GetTree().Root.Size = new Vector2I(1280, 720);
            for (int i = 0; i < 3600 && SceneTransition.Loading; i++) { await Frames(1); }
            Check(!SceneTransition.Loading && Planet.Active.CloudPass.Ready, "cloud compositor initializes before flight");
            ShaderMaterial material = (ShaderMaterial)typeof(Planet).GetField("_clouds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Planet.Active);
            Shader production = material.Shader;
            using Shader reference = new() { Code = FileAccess.GetFileAsString("res://Shaders/Clouds/Clouds.gdshader").Replace("if (cloud_buffer_ready)", "if (false)") };
            foreach (double height in new[] { 600.0, 1800.0, 4200.0, 100000.0 }) {

                Flight.Active.Place(height, 0.0);
                OrbitCamera.Active.Distance = 35.0f;
                OrbitCamera.Active.Pitch = height < 1000 ? -0.1f : 0.1f;
                await Frames(100);
                using Image reduced = Capture();
                reduced.SavePng($"res://.artifacts/cloud-pipeline-{height}.png");
                material.Shader = reference;
                await Frames(100);
                using Image full = Capture();
                full.SavePng($"res://.artifacts/cloud-reference-{height}.png");
                byte[] a = reduced.GetData();
                byte[] b = full.GetData();
                double error = 0;
                int outliers = 0;
                for (int i = 0; i < a.Length; i++) {

                    int difference = Math.Abs(a[i] - b[i]);
                    error += difference;
                    if (difference > 64) { outliers++; }

                }
                error /= a.Length * 255.0;
                Check(error < 0.025 && outliers < a.Length * 0.01, $"half-resolution view matches full-resolution reference at {height} m (mean {error:F4}, outliers {(double)outliers / a.Length:P2})");
                material.Shader = production;
                await Frames(10);

            }
            GetTree().Root.Size = new Vector2I(960, 540);
            await Frames(60);
            Texture2D buffer = (Texture2D)material.GetShaderParameter("cloud_buffer").AsGodotObject();
            Vector2 viewport = GetViewport().GetVisibleRect().Size;
            int expected = (int)Math.Ceiling(Math.Floor(viewport.X * GetViewport().Scaling3DScale) / 2.0);
            Check(Math.Abs(buffer.GetWidth() - expected) <= 1, "cloud buffers follow render-target resize");
            GD.Print("Cloud pipeline checks complete");
            GetTree().Quit();

        } catch (Exception exception) {

            GD.PushError(exception.ToString());
            GetTree().Quit(1);

        }

    }

}
