using System;
using System.Threading.Tasks;

using Godot;

namespace FullThrust.Game;

public sealed partial class RenderingStabilityChecks : Node {

    private SubViewport _viewport;
    private ShaderMaterial _material;
    private int _checks;

    private void Check(bool condition, string message) {

        if (!condition) {

            throw new InvalidOperationException(message);

        }
        GD.Print("PASS " + message);
        _checks++;

    }

    private async Task<Color> Read() {

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image result = _viewport.GetTexture().GetImage();
        return result.GetPixel(4, 4);

    }

    public override async void _Ready() {

        try {

            _material = new ShaderMaterial {

                Shader = new Shader { Code = "shader_type canvas_item; render_mode unshaded; uniform sampler3D volume : filter_linear_mipmap; uniform float lod; void fragment() { COLOR = vec4(vec3(textureLod(volume, vec3(UV, 0.125), lod).r), 1.0); }" },

            };
            _viewport = new SubViewport { Size = new Vector2I(8, 8), Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(_viewport);
            _viewport.AddChild(new ColorRect { Size = new Vector2(8, 8), Material = _material });

            // A checker on each axis catches mip chains built from only the 2D slices.
            for (int axis = 0; axis < 3; axis++) {

                Godot.Collections.Array<Image> slices = new();
                for (int z = 0; z < 4; z++) {

                    byte[] pixels = new byte[16];
                    for (int y = 0; y < 4; y++) {

                        for (int x = 0; x < 4; x++) {

                            int coordinate = axis == 0 ? x : axis == 1 ? y : z;
                            pixels[y * 4 + x] = (byte)((coordinate & 1) * 255);

                        }

                    }
                    slices.Add(Image.CreateFromData(4, 4, false, Image.Format.L8, pixels));

                }
                using ImageTexture3D source = new();
                Check(source.Create(Image.Format.L8, 4, 4, 4, false, slices) == Error.Ok, $"axis {axis} volume uploads");
                using ImageTexture3D filtered = FilteredVolume.Build(source);
                _material.SetShaderParameter("volume", filtered);
                foreach (float lod in new[] { 1.0f, 1.5f, 2.0f }) {

                    _material.SetShaderParameter("lod", lod);
                    Check(Math.Abs((await Read()).R - 128.0f / 255.0f) < 0.01f, $"axis {axis} retains average through GPU mip LOD {lod}");

                }

            }

            _material.Shader = new Shader {

                Code = "shader_type canvas_item; render_mode unshaded;\n#include \"res://Shaders/AtmosphereComposite.gdshaderinc\"\nuniform vec3 transmission; void fragment() { vec3 background = vec3(0.8, 0.6, 0.4); vec3 light = vec3(0.02, 0.04, 0.06); vec4 air = atmosphere_composite(background, transmission, light); vec3 expected = background * transmission + light; vec3 resolved = air.rgb + background * (1.0 - air.a); COLOR = vec4(max(max(abs(resolved.r - expected.r), abs(resolved.g - expected.g)), abs(resolved.b - expected.b)) * 100.0, air.a, 0.0, 1.0); }",

            };
            foreach (Vector3 transmission in new[] { Vector3.One, new Vector3(0.998f, 0.996f, 0.995f), new Vector3(0.8f, 0.4f, 0.1f), Vector3.Zero }) {

                _material.SetShaderParameter("transmission", transmission);
                Color result = await Read();
                Check(result.R < 0.01f, $"premultiplied air preserves coloured extinction at {transmission}");
                float alpha = 1.0f - Math.Min(transmission.X, Math.Min(transmission.Y, transmission.Z));
                Check(Math.Abs(result.G - alpha) < 0.01f, $"air opacity tracks extinction at {transmission}");

            }
            _material.Shader = new Shader {

                Code = "shader_type canvas_item; render_mode unshaded;\n#include \"res://Shaders/AtmosphereComposite.gdshaderinc\"\nuniform float cloud_alpha; void fragment() { vec4 cloud = vec4(vec3(0.6) * cloud_alpha, cloud_alpha); vec4 front = vec4(0.02, 0.03, 0.04, 0.1); vec4 back = vec4(0.1, 0.12, 0.15, 0.7); vec4 full = front + back * (1.0 - front.a); vec4 expected = front + (cloud + back * (1.0 - cloud.a)) * (1.0 - front.a); vec4 error = abs(cloud_fog_composite(cloud, front, full) - expected); COLOR = vec4(max(max(error.r, error.g), max(error.b, error.a)) * 100.0, 0.0, 0.0, 1.0); }",

            };
            foreach (float alpha in new[] { 0.0f, 0.25f, 0.75f, 1.0f }) {

                _material.SetShaderParameter("cloud_alpha", alpha);
                Check((await Read()).R < 0.01f, $"cloud opacity {alpha} occludes only fog behind it");

            }
            GD.Print($"Rendering stability: {_checks} GPU checks passed");
            GetTree().Quit();

        } catch (Exception exception) {

            GD.PushError(exception.ToString());
            GetTree().Quit(1);

        }

    }

}
