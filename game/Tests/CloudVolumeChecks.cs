using System;
using System.Threading.Tasks;

using Godot;

namespace FullThrust.Game;

public sealed partial class CloudVolumeChecks : Node {

    private ShaderMaterial _material;
    private SubViewport _viewport;
    private int _checks;

    private static ImageTexture3D ConstantVolume(float value) {

        using Image slice = Image.CreateEmpty(2, 2, false, Image.Format.Rf);
        slice.Fill(new Color(value, 0, 0));
        Godot.Collections.Array<Image> slices = new() { slice, slice };
        ImageTexture3D volume = new();
        if (volume.Create(Image.Format.Rf, 2, 2, 2, false, slices) != Error.Ok) { throw new InvalidOperationException("Volume upload failed"); }
        return volume;

    }

    private void Check(bool condition, string message) {

        if (!condition) { throw new InvalidOperationException(message); }
        GD.Print("PASS " + message);
        _checks++;

    }

    private async Task<float> Density(Vector3 point, int count, float strength) {

        _material.SetShaderParameter("probe", point);
        _material.SetShaderParameter("wake_count", count);
        Vector4[] states = new Vector4[8];
        states[0] = new Vector4(strength, 0.1f, 0, 0);
        _material.SetShaderParameter("wake_states", states);
        return (await Read()).R * 2.0f;

    }

    private async Task<Color> Read() {

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = _viewport.GetTexture().GetImage();
        return image.GetPixel(4, 4);

    }

    public override async void _Ready() {

        try {

            _material = new ShaderMaterial {

                Shader = new Shader { Code = "shader_type canvas_item; render_mode unshaded;\n#include \"res://Shaders/CloudField.gdshaderinc\"\nuniform vec3 probe;\nvoid fragment() { float d = density_in_weather(probe, 16.0, vec2(0.75, 0.65)); COLOR = vec4(d * 0.5, 0.0, 0.0, 1.0); }" },

            };
            using ImageTexture3D shape = ConstantVolume(0.60f);
            using ImageTexture3D detail = ConstantVolume(0.25f);
            _material.SetShaderParameter("shape_noise", shape);
            _material.SetShaderParameter("detail_noise", detail);
            _material.SetShaderParameter("base_radius", 1275100.0f);
            _material.SetShaderParameter("top_radius", 1278000.0f);
            Vector3 point = new(0, 1276000, 0);
            Vector4[] centres = new Vector4[8];
            Vector4[] axes = new Vector4[8];
            centres[0] = new Vector4(-30, point.Y, 0, 25);
            axes[0] = new Vector4(1, 0, 0, 60);
            _material.SetShaderParameter("wake_centres", centres);
            _material.SetShaderParameter("wake_axes", axes);
            _viewport = new SubViewport { Size = new Vector2I(8, 8), Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(_viewport);
            _viewport.AddChild(new ColorRect { Size = new Vector2(8, 8), Material = _material });
            float baseline = await Density(point, 0, 0);
            Check(baseline > 0.1f, "interior cloud density remains substantial");
            Check(await Density(point, 1, 1) < 0.01f, "plume clears the cloud at its core");
            float recovering = await Density(point, 1, 0.5f);
            Check(recovering > baseline * 0.4f && recovering < baseline * 0.6f, "wake recovery is continuous");
            Check(Math.Abs(await Density(point, 1, 0) - baseline) < 0.01f, "expired wake leaves no residual cone");
            Check(await Density(point + Vector3.Back * 31, 1, 1) > baseline * 1.12f, "displaced cloud is compressed at the wake rim");
            Check(Math.Abs(await Density(point + Vector3.Back * 300, 1, 1) - baseline) < 0.01f, "wake preserves cloud outside its bounds");
            Check(await Density(new Vector3(0, 1275000, 0), 0, 0) < 0.01f, "cloud field is empty below its deck");
            Check(await Density(new Vector3(0, 1278100, 0), 0, 0) < 0.01f, "cloud field is empty above its deck");
            _material.Shader = new Shader { Code = "shader_type canvas_item; render_mode unshaded;\nuniform float planet_radius = 1274200.0;\n#include \"res://Shaders/CloudRayGeometry.gdshaderinc\"\nuniform vec3 probe; uniform float expected;\nvoid fragment() { COLOR = vec4(clamp(0.5 + (ray_height(probe) - expected) * 10.0, 0.0, 1.0), 0.0, 0.0, 1.0); }" };
            _material.SetShaderParameter("eye_up", Vector3.Up);
            _material.SetShaderParameter("eye_height", 800.03125f);
            foreach (Vector3 offset in new[] { new Vector3(0, 0.01f, 0), new Vector3(20000, -300, 0), new Vector3(100000, -3000, 0) }) {

                double radius = 1274200.0 + 800.03125;
                double expected = Math.Sqrt((radius + offset.Y) * (radius + offset.Y) + (double)offset.X * offset.X) - 1274200.0;
                _material.SetShaderParameter("probe", offset);
                _material.SetShaderParameter("expected", (float)expected);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image result = _viewport.GetTexture().GetImage();
                Check(Math.Abs(result.GetPixel(4, 4).R - 0.5f) < 0.1f, $"cloud ray height agrees with double precision within 1 cm at {offset}");

            }
            // Compile the production marcher so the regression also catches incorrect clipping in its caller.
            string clouds = FileAccess.GetFileAsString("res://Shaders/Clouds.gdshader");
            int start = clouds.IndexOf("uniform vec3 planet_centre", StringComparison.Ordinal);
            int finish = clouds.IndexOf("#include \"res://Shaders/CoastalFog", StringComparison.Ordinal);
            _material.Shader = new Shader {

                Code = "shader_type canvas_item; render_mode unshaded;\n" + clouds[start..finish]
                    + "\nuniform vec3 probe; uniform float scene_distance = 4000000.0; void fragment() { float distance; vec4 cloud = cloud_radiance(vec3(0.0, planet_radius + eye_height, 0.0), normalize(probe), scene_distance, 0.00001, 0.5, distance); COLOR = vec4(cloud.a, distance / 200000.0, 0.0, 1.0); }",

            };
            using Image weatherImage = Image.CreateEmpty(2, 2, false, Image.Format.Rgb8);
            weatherImage.Fill(new Color(0.75f, 0.75f, 0.75f));
            using ImageTexture weather = ImageTexture.CreateFromImage(weatherImage);
            _material.SetShaderParameter("cloud_map", weather);
            _material.SetShaderParameter("wake_count", 0);
            _material.SetShaderParameter("eye_height", 1100.0f);
            _material.SetShaderParameter("eye_up", Vector3.Up);
            _material.SetShaderParameter("extinction", 0.00001f);
            _material.SetShaderParameter("probe", new Vector3(1, 0.00001f, 0));
            float aboveHorizon = (await Read()).R;
            _material.SetShaderParameter("probe", new Vector3(1, -0.00001f, 0));
            float belowHorizon = (await Read()).R;
            Check(aboveHorizon > 0.05f, "horizon regression samples an occupied cloud deck");
            Check(Math.Abs(aboveHorizon - belowHorizon) < 0.008f, "cloud transmission is continuous across the local horizon");
            _material.SetShaderParameter("extinction", 0.009f);
            Check((await Read()).R > 0.999f, "opaque cloud termination leaves no background horizon leak");
            _material.SetShaderParameter("scene_distance", 0.0f);
            Check((await Read()).R < 0.001f, "foreground scene depth clips cloud integration");
            GD.Print($"Cloud volume: {_checks} GPU checks passed");
            GetTree().Quit();

        } catch (Exception exception) {

            GD.PushError(exception.ToString());
            GetTree().Quit(1);

        }

    }

}
