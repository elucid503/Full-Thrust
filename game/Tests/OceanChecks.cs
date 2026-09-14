using System;
using System.Reflection;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class OceanChecks : Node {

    private int _checks;

    private void Check(bool condition, string label) {

        if (!condition) { throw new InvalidOperationException(label); }
        GD.Print("PASS " + label);
        _checks++;

    }

    public override async void _Ready() {

        try {

            CelestialBody body = BodyCatalog.Home;
            body.Weather = new Weather();
            ShaderMaterial material = new() {

                Shader = new Shader { Code = "shader_type canvas_item; render_mode unshaded;\n#include \"res://Shaders/OceanField.gdshaderinc\"\nuniform vec3 probe; uniform vec4 expected; uniform float elevation; uniform float spacing; void fragment() { vec4 wave = ocean_wave(probe, spacing, elevation); COLOR = vec4(abs(wave.x - expected.x), length(wave.yzw - expected.yzw), 0.0, 1.0); }" },

            };
            SubViewport viewport = new() { Size = new Vector2I(8, 8), Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(viewport);
            viewport.AddChild(new ColorRect { Size = new Vector2(8, 8), Material = material });
            Vector3[] along = new Vector3[4];
            Vector4[] modes = new Vector4[4];
            foreach (Vector3d direction in new[] { Weather.Origin, Vector3d.UnitX, Vector3d.UnitY, Vector3d.UnitZ, -Weather.Origin }) {

                foreach (double time in new[] { 0.0, 127.0, 1e7 }) {

                    foreach (double elevation in new[] { -100.0, -0.3, 0.15 }) {

                        Ocean.Surface sample = Ocean.Sample(body, direction * body.Radius, elevation, time);
                        Vector3 gradient = Frames.Direction(sample.Gradient);
                        Planet.SetOcean(material, body, time, along, modes);
                        material.SetShaderParameter("probe", Frames.Direction(direction * body.Radius));
                        material.SetShaderParameter("expected", new Vector4((float)sample.Height, gradient.X, gradient.Y, gradient.Z));
                        material.SetShaderParameter("elevation", (float)elevation);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using Image result = viewport.GetTexture().GetImage();
                        Color error = result.GetPixel(4, 4);
                        Check(error.R < 0.12f && error.G < 0.035f, $"GPU water matches physics at {time} s / bed {elevation}: {error.R:F4} m, slope {error.G:F4}");

                    }

                }

            }
            material.Shader = new Shader { Code = "shader_type canvas_item; render_mode unshaded;\n#include \"res://Shaders/OceanSurface.gdshaderinc\"\nuniform vec3 probe; void fragment() { COLOR = vec4(vec3(0.5) + ocean_ripples(probe, 0.03).xyz, 1.0); }" };
            Vector4[] rippleDirections = new Vector4[Planet.RippleCount];
            Vector4[] rippleStates = new Vector4[Planet.RippleCount];
            Vector3d ripplePoint = body.ToInertial(Weather.Origin * body.Radius, 1234.0);
            Color reference = default;
            foreach (double offset in new[] { 0.0, 500.0, 6500.0 }) {

                Vector3d origin = ripplePoint + Vector3d.UnitX * offset;
                Planet.SetRipples(material, body, 1234.0, origin, rippleDirections, rippleStates);
                material.SetShaderParameter("probe", Frames.Direction(body.ToBodyFixed(ripplePoint - origin, 1234.0)));
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image result = viewport.GetTexture().GetImage();
                Color colour = result.GetPixel(4, 4);
                if (offset == 0.0) { reference = colour; }
                Check(Math.Abs(colour.R - reference.R) + Math.Abs(colour.G - reference.G) + Math.Abs(colour.B - reference.B) < 0.025,
                    $"short water waves preserve phase across {offset} m origin movement");

            }
            viewport.Size = new Vector2I(512, 8);
            viewport.GetChild<ColorRect>(0).Size = new Vector2(512, 8);
            using Image ramp = Image.CreateEmpty(2, 2, false, Image.Format.Rf);
            ramp.SetPixel(1, 0, Colors.White);
            ramp.SetPixel(1, 1, Colors.White);
            material.Shader = new Shader { Code = "shader_type canvas_item; render_mode unshaded;\nuniform sampler2D shoreline_map : filter_linear; uniform float planet_radius = 1.0; float coastal_elevation(vec3 p, float h, float latitude) { return h; }\n#include \"res://Shaders/ShorelineSampling.gdshaderinc\"\nvoid fragment() { COLOR = vec4(vec3(shoreline_survey(vec2(0.251 + UV.x * 0.01, 0.5)) * 25.0), 1.0); }" };
            material.SetShaderParameter("shoreline_map", ImageTexture.CreateFromImage(ramp));
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image coastRamp = viewport.GetTexture().GetImage();
            float largestStep = 0.0f;
            for (int x = 1; x < 512; x++) {

                largestStep = Math.Max(largestStep, Math.Abs(coastRamp.GetPixel(x, 4).R - coastRamp.GetPixel(x - 1, 4).R));

            }
            Check(largestStep < 0.01f && coastRamp.GetPixel(511, 4).R - coastRamp.GetPixel(0, 4).R > 0.45f,
                $"shoreline survey resolves sub-texel gradients without steps ({largestStep:F4})");
            viewport.QueueFree();
            Main main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            main.SetProcess(false);
            Flight flight = Flight.Active;
            while (flight.Vessel.CanSeparate) { flight.Separate(); }
            flight.PlaceAt(30.0, -40.0, 0.5, 0.0);
            flight.Body.Weather.SetPreset(WeatherPreset.Calm, flight.Time - 1000.0);
            Vector3d up = flight.Vessel.Position.Normalized;
            flight.Vessel.Orientation = QuaternionD.LookAlong(up, Vector3d.UnitZ);
            flight.Vessel.Position = up * (body.Radius + flight.Vessel.CentreOfMassZ - flight.Vessel.Base + 0.35);
            flight.Vessel.Velocity = body.SurfaceVelocityAt(flight.Vessel.Position) - up * 3.0;
            flight.Autopilot.Hold = AttitudeHold.Off;
            for (int step = 0; step < 1200 && !flight.Ended; step++) { flight.Advance(1.0 / 120.0); }
            Check(!flight.Ended && flight.Vessel.InWater, $"Flight.Advance and Judge preserve a soft splashdown ({flight.Fate}, {flight.Altitude:F2} m)");
            Check(Math.Abs(flight.Altitude) < 2.0, "actual flight capsule floats at the rendered ocean");
            DebugPanel panel = main.GetNode<DebugPanel>("Debug");
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            OptionButton presets = (OptionButton)typeof(DebugPanel).GetField("_weatherPreset", fields).GetValue(panel);
            presets.EmitSignal(OptionButton.SignalName.ItemSelected, (long)WeatherPreset.Gale);
            Check(body.Weather.TargetWindSpeed == 32.0, "F1 preset updates shared weather");
            panel._UnhandledKeyInput(new InputEventKey { Keycode = Key.F1, Pressed = true });
            panel.Sync();
            PanelContainer frame = (PanelContainer)typeof(DebugPanel).GetField("_frame", fields).GetValue(panel);
            Check(frame.Visible && frame.Size.Y <= GetViewport().GetVisibleRect().Size.Y - 40.0f, "F1 weather controls fit the viewport");
            GD.Print($"Ocean: {_checks} checks passed");
            GetTree().Quit();

        } catch (Exception exception) {

            GD.PushError(exception.ToString());
            GetTree().Quit(1);

        }

    }

}
