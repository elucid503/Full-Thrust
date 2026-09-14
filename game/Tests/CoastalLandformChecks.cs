using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class CoastalLandformChecks : Node {

    public override async void _Ready() {

        try {

            const int count = 48;
            const double radius = 1274200.0;
            Vector4[] probes = new Vector4[count];
            double[] expected = new double[count];
            for (int i = 0; i < count; i++) {

                double latitude = (-60.0 + i * 2.4) * Math.PI / 180.0;
                double longitude = (-174.0 + i * 7.3) * Math.PI / 180.0;
                Vector3d unit = new(Math.Cos(latitude) * Math.Cos(longitude), Math.Cos(latitude) * Math.Sin(longitude), Math.Sin(latitude));
                double surveyed = new[] { -18.0, -6.0, -1.0, 0.0, 2.0, 5.0, 15.0, 30.0 }[i % 8];
                probes[i] = new Vector4((float)(unit.X * radius), (float)(unit.Z * radius), (float)(-unit.Y * radius), (float)surveyed);
                expected[i] = CoastalLandforms.Elevation(unit, radius, surveyed);

            }
            ShaderMaterial material = new() {

                Shader = new Shader { Code = "shader_type canvas_item; render_mode unshaded;\n#include \"res://Shaders/LandscapeNoise.gdshaderinc\"\n#include \"res://Shaders/CoastalLandforms.gdshaderinc\"\nuniform vec4 probes[48];\nvoid fragment() { vec4 p = probes[int(FRAGCOORD.x)]; float h = coastal_elevation(p.xyz, p.w, normalize(p.xyz).y); COLOR = vec4((h + 40.0) / 80.0, 0.0, 0.0, 1.0); }" },

            };
            material.SetShaderParameter("probes", probes);
            SubViewport viewport = new() { Size = new Vector2I(count, 4), Disable3D = true, UseHdr2D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(viewport);
            viewport.AddChild(new ColorRect { Size = new Vector2(count, 4), Material = material });
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image image = viewport.GetTexture().GetImage();
            double worst = 0.0;
            for (int i = 0; i < count; i++) {

                double actual = image.GetPixel(i, 2).R * 80.0 - 40.0;
                worst = Math.Max(worst, Math.Abs(actual - expected[i]));

            }
            if (worst > 0.08) {

                throw new InvalidOperationException($"CPU/GPU shoreline disagreement: {worst:F4} m");

            }
            GD.Print($"Coastal landforms: {count} GPU samples agree with collision terrain; max error {worst:F4} m");
            GetTree().Quit();

        } catch (Exception exception) {

            GD.PushError(exception.ToString());
            GetTree().Quit(1);

        }

    }

}
