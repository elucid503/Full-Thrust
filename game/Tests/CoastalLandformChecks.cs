using System;
using System.IO;
using System.Reflection;

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
            using FileStream stream = File.OpenRead(ProjectSettings.GlobalizePath("res://Assets/Planet/elevation.r16"));
            Terrain terrain = Terrain.Load(stream, radius);
            Texture2D shoreline = (Texture2D)typeof(Planet).GetMethod("Shoreline", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { terrain });
            for (int i = 0; i < count; i++) {

                double lat = (i < count / 2 ? 28.52 : 25.30) * Math.PI / 180.0;
                double lon = (i < count / 2 ? -80.70 + i * 0.008 : -80.9 + (i - count / 2) * 0.008) * Math.PI / 180.0;
                Vector3d unit = new(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
                probes[i] = new Vector4((float)(unit.X * radius), (float)(unit.Z * radius), (float)(-unit.Y * radius), 0.0f);
                terrain.Elevation(unit, 0.0, out expected[i]);

            }
            material.Shader = new Shader { Code = "shader_type canvas_item; render_mode unshaded;\nuniform sampler2D shoreline_map : filter_linear; uniform float planet_radius; uniform vec4 probes[48];\n#include \"res://Shaders/LandscapeNoise.gdshaderinc\"\n#include \"res://Shaders/CoastalLandforms.gdshaderinc\"\n#include \"res://Shaders/ShorelineSampling.gdshaderinc\"\nvoid fragment() { float h = shoreline_height(probes[int(FRAGCOORD.x)].xyz); COLOR = vec4((h + 40.0) / 80.0, 0.0, 0.0, 1.0); }" };
            material.SetShaderParameter("shoreline_map", shoreline);
            material.SetShaderParameter("planet_radius", (float)radius);
            material.SetShaderParameter("probes", probes);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image curved = viewport.GetTexture().GetImage();
            worst = 0.0;
            for (int i = 0; i < count; i++) {

                worst = Math.Max(worst, Math.Abs(curved.GetPixel(i, 2).R * 80.0 - 40.0 - expected[i]));

            }
            if (worst > 0.08) { throw new InvalidOperationException($"Curved survey CPU/GPU disagreement: {worst:F4} m"); }
            GD.Print($"Curved shoreline: {count} GPU samples match physics; max error {worst:F4} m");
            GetTree().Quit();

        } catch (Exception exception) {

            GD.PushError(exception.ToString());
            GetTree().Quit(1);

        }

    }

}
