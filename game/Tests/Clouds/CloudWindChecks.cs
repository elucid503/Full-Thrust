using System;

using FullThrust.Sim;
using static FullThrust.Game.Checks;

using Godot;

namespace FullThrust.Game;

public sealed partial class CloudWindChecks : Node {


    public override async void _Ready() {

        try {

            CelestialBody body = BodyCatalog.Home;
            LaunchSite site = LaunchSite.Home;
            double latitude = site.Latitude;
            Vector3d up = new(Math.Cos(latitude) * Math.Cos(site.Longitude), Math.Cos(latitude) * Math.Sin(site.Longitude), Math.Sin(latitude));
            Vector3d origin = up * (body.Radius + 2000.0);
            Vector3d moved = CloudWind.Advect(origin, 60.0, body.Radius);
            Check(Math.Abs((moved - origin).Length - 600.0) < 2.0, "launch clouds travel about 600 m per minute");
            Vector3d east = Vector3d.Cross(Vector3d.UnitZ, up).Normalized;
            Vector3d north = Vector3d.Cross(up, east);
            Check(Vector3d.Dot(moved - origin, east) > 400.0 && Vector3d.Dot(moved - origin, north) > 400.0, "southwesterly carries clouds northeast");
            Check(Math.Abs(moved.Length - origin.Length) < 1e-6, "wind preserves cloud altitude");
            Check((CloudWind.Advect(moved, -60.0, body.Radius) - origin).Length < 1e-6, "rewinding time exactly reverses advection");
            Vector3d stepped = origin;
            for (int i = 0; i < 60; i++) { stepped = CloudWind.Advect(stepped, 1.0, body.Radius); }
            Check((stepped - moved).Length < 1e-6, "wind is independent of timestep and time acceleration");
            Check(CloudWind.Frame(body, 60.0) == CloudWind.Frame(body, 60.0), "paused cloud frame is deterministic");

            ShaderMaterial material = new() {

                Shader = new Shader { Code = "shader_type canvas_item; render_mode unshaded;\n#include \"res://Shaders/Clouds/CloudField.gdshaderinc\"\nuniform vec3 probe; uniform vec3 expected; void fragment() { COLOR = vec4(length(cloud_unspin(probe) - expected), 0.0, 0.0, 1.0); }" },

            };
            SubViewport viewport = new() { Size = new Vector2I(8, 8), Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(viewport);
            viewport.AddChild(new ColorRect { Size = new Vector2(8, 8), Material = material });
            foreach (Vector3d point in new[] { origin, Vector3d.UnitZ * origin.Length, -Vector3d.UnitX * origin.Length }) {

                foreach (double time in new[] { 0.0, 60.0, 3600.0, 1e9 }) {

                    Vector3d advected = CloudWind.Advect(point, time, body.Radius);
                    material.SetShaderParameter("expected", Frames.Direction(point));
                    foreach (bool bodyFixed in new[] { false, true }) {

                        material.SetShaderParameter("cloud_frame", CloudWind.Frame(body, time, bodyFixed));
                        material.SetShaderParameter("probe", Frames.Direction(bodyFixed ? advected : body.ToInertial(advected, time)));
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using Image result = viewport.GetTexture().GetImage();
                        Check(result.GetPixel(4, 4).R < 0.5f, $"GPU {(bodyFixed ? "shadow" : "view")} frame follows wind and planet rotation at {time} s");

                    }

                }

            }
            GD.Print($"Cloud wind: {Passed} checks passed");
            GetTree().Quit();

        } catch (Exception exception) {

            Fail(this, exception);

        }

    }

}
