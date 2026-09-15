using System;
using System.Reflection;
using System.Threading.Tasks;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class EngineVisualChecks : Node {

    private Flight _flight;
    private Main _main;
    private Action _surfaceTestInjection;
    private readonly PropertyInfo _clock = typeof(Flight).GetProperty("Time");

    private async Task Frames(int count, double dt = 1.0 / 60.0) {

        for (int i = 0; i < count; i++) {

            double before = _flight.Time;
            QuaternionD spin = QuaternionD.FromAxisAngle(Vector3d.UnitZ, _flight.Body.SpinAt(before + dt) - _flight.Body.SpinAt(before));
            _flight.Vessel.Position = spin.Rotate(_flight.Vessel.Position);
            _flight.Vessel.Velocity = spin.Rotate(_flight.Vessel.Velocity);
            _flight.Vessel.Orientation = spin * _flight.Vessel.Orientation;
            foreach (Flight.Tracked tracked in _flight.Debris) {

                tracked.Vessel.Position = spin.Rotate(tracked.Vessel.Position);
                tracked.Vessel.Orientation = spin * tracked.Vessel.Orientation;
                tracked.Vessel.Velocity = spin.Rotate(tracked.Vessel.Velocity);
                tracked.Vessel.AdvanceEngines(_flight.Body, dt);

            }
            _clock.SetValue(_flight, before + dt);
            _flight.Vessel.AdvanceEngines(_flight.Body, dt);
            _surfaceTestInjection?.Invoke();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        }

    }

    private async Task Settle() {

        SceneTransition.Begin(this);
        ulong started = Time.GetTicksMsec();
        do {

            await Frames(1, 0.0);
            if (Time.GetTicksMsec() - started > 90000) { throw new InvalidOperationException("Terrain transition timed out"); }

        } while (SceneTransition.Loading);

    }

    private async Task Capture(string name) {

        await Frames(3, 0.0);
        using Image shot = GetViewport().GetTexture().GetImage();
        shot.SavePng($"res://.artifacts/engine-{name}.png");
        GD.Print($"ENGINE CAPTURE {name}: thrust={_flight.Vessel.CurrentThrust:F1} soot={VesselView.Active.SootCoverage:F3} GPU={RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()):F2}ms");

    }

    public override async void _Ready() {

        try {

            Main main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            _main = main;
            _flight = Flight.Active;
            _flight.DebugPaused = true;
            GetTree().Root.Mode = Window.ModeEnum.Windowed;
            GetTree().Root.Size = new Vector2I(1280, 720);
            main.GetNode<CanvasLayer>("Hud").Hide();
            ulong started = Time.GetTicksMsec();
            do {

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (Time.GetTicksMsec() - started > 90000) { throw new InvalidOperationException("Terrain did not settle"); }

            } while (SceneTransition.Loading);
            if (OS.GetEnvironment("FT_ENGINE_CHECK") == "audit") {

                await Audit();
                GetTree().Quit();
                return;

            }
            Vessel vessel = _flight.Vessel;
            if (OS.GetEnvironment("FT_ENGINE_CHECK") == "retro-soot") {

                await RetroSoot();
                GetTree().Quit();
                return;

            }
            if (OS.GetEnvironment("FT_ENGINE_CHECK") == "expansion") {

                _flight.Separate();
                _flight.Place(150000.0, 0.0);
                _flight.Debris[0].Vessel.Position = vessel.Position + Vector3d.UnitZ * 5000.0;
                vessel.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, vessel.Position.Normalized);
                vessel.Throttle = 1.0;
                await Frames(120);
                CloseCamera(vessel.Position - vessel.Nose * (vessel.CentreOfMassZ - vessel.Active.Hull.Base + 18.0), new Vector3d(75.0, -30.0, 0.0));
                await Frames(30, 0.0);
                await Capture("expanded-0");
                await Frames(12, 0.0);
                await Capture("expanded-paused");
                for (int i = 1; i <= 4; i++) {

                    await Frames(3);
                    await Capture("expanded-" + i);

                }
                await Measure("expanded-flow");
                vessel.TranslationCommand = Vector3d.UnitX;
                OrbitCamera.Active.Distance = 25.0f;
                FreeCamera.Active.Release();
                await Measure("expanded-with-rcs");
                GetTree().Quit();
                return;

            }
            if (OS.GetEnvironment("FT_ENGINE_CHECK") == "surface") {

                OrbitCamera.Active.Distance = 90.0f;
                OrbitCamera.Active.Pitch = 0.15f;
                vessel.Throttle = 1.0;
                await Frames(6, 1.0 / 30.0);
                await Capture("pad-onset");
                await Frames(24, 1.0 / 30.0);
                await Capture("pad-forming");
                await Frames(210, 1.0 / 30.0);
                await Measure("pad-sustained");
                CloseToSteam();
                await Measure("pad-close");
                await MeasureWithoutSmoke("pad-close-no-smoke");
                FreeCamera.Active.Release();
                vessel.Throttle = 0.0;
                await Frames(360, 1.0 / 30.0);
                await Capture("pad-cleared");

            }
            if (OS.GetEnvironment("FT_ENGINE_CHECK") == "flow") {

                _flight.Place(6000.0, 0.0);
                vessel.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, vessel.Position.Normalized);
                vessel.Throttle = 1.0;
                await Frames(150);
                string comparisonShader = OS.GetEnvironment("FT_PLUME_SHADER");
                var compared = new System.Collections.Generic.List<ShaderMaterial>();
                if (!string.IsNullOrEmpty(comparisonShader)) {

                    Shader shader = new() { Code = Godot.FileAccess.GetFileAsString(comparisonShader) };
                    foreach (Node node in _main.FindChildren("*", "MeshInstance3D", true, false)) {

                        if (((MeshInstance3D)node).MaterialOverride is ShaderMaterial material
                            && material.Shader.ResourcePath == "res://Shaders/Plume.gdshader") {

                            compared.Add(material);
                            material.Shader = shader;

                        }

                    }

                }
                CloseCamera(vessel.Position - vessel.Nose * (vessel.CentreOfMassZ + 30.0), new Vector3d(22.0, -16.0, 0.0));
                await Frames(30, 0.0);
                await Capture("cluster-tail");
                await Frames(19);
                await Capture("cluster-tail-moving");
                await Measure("cluster-tail");
                if (compared.Count > 0) {

                    Shader current = GD.Load<Shader>("res://Shaders/Plume.gdshader");
                    foreach (ShaderMaterial material in compared) { material.Shader = current; }
                    await Frames(30, 0.0);
                    await Capture("cluster-tail-current");
                    await Measure("cluster-tail-current");

                }
                GetTree().Quit();
                return;

            }
            if (OS.GetEnvironment("FT_ENGINE_CHECK") != "water" && OS.GetEnvironment("FT_ENGINE_CHECK") != "surface") {

                _flight.Place(6000.0, 0.0);
                vessel.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, vessel.Position.Normalized);
                OrbitCamera.Active.Distance = 115.0f;
                OrbitCamera.Active.Pitch = 0.05f;
                vessel.Throttle = 1.0;
                await Frames(150);
                await Capture("atmosphere");
                for (int i = 1; i < vessel.EngineCount; i++) { vessel.SetEngine(i, false); }
                OrbitCamera.Active.Distance = 50.0f;
                await Frames(60);
                await Capture("single-engine");
                FreeCamera close = FreeCamera.Active;
                close.Take(OrbitCamera.Active);
                Vector3d focus = vessel.Position - vessel.Nose * (vessel.CentreOfMassZ + 7.0);
                Vector3d where = focus + vessel.Orientation.Rotate(new Vector3d(14.0, -10.0, 0.0));
                const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(FreeCamera).GetMethod("Settle", hidden).Invoke(close, new object[] { where });
                Vector3 forward = FullThrust.Game.Frames.Direction((focus - where).Normalized);
                Vector3 up = FullThrust.Game.Frames.Direction(where.Normalized);
                FullThrust.Game.Frames.Horizon(up, out Vector3 side, out Vector3 ahead);
                typeof(FreeCamera).GetField("_yaw", hidden).SetValue(close, Mathf.Atan2(-forward.Dot(side), forward.Dot(ahead)));
                typeof(FreeCamera).GetField("_pitch", hidden).SetValue(close, Mathf.Asin(forward.Dot(up)));
                await Frames(20, 0.0);
                await Capture("diamonds");
                await Frames(17);
                await Capture("diamonds-moving");
                close.Release();
                for (int i = 1; i < vessel.EngineCount; i++) { vessel.SetEngine(i, true); }
                Propellant kerosene = vessel.Active.Fuel;
                typeof(Stage).GetProperty("Fuel").SetValue(vessel.Active, new Propellant { Name = "Liquid Hydrogen" });
                await Frames(60);
                await Capture("hydrogen");
                typeof(Stage).GetProperty("Fuel").SetValue(vessel.Active, new Propellant { Name = "Liquid Methane" });
                await Frames(60);
                await Capture("methane");
                typeof(Stage).GetProperty("Fuel").SetValue(vessel.Active, kerosene);
                OrbitCamera.Active.Distance = 75.0f;
                await Frames(60);
                vessel.Throttle = 0.0;
                await Frames(5);
                await Capture("burnoff");
                await Frames(9);
                await Capture("purge");
                await Frames(70);
                await Capture("off");
                vessel.TranslationCommand = Vector3d.UnitX;
                OrbitCamera.Active.Distance = 40.0f;
                await Frames(30);
                await Capture("rcs");
                vessel.TranslationCommand = Vector3d.Zero;
                await Frames(60);
                OrbitCamera.Active.Distance = 90.0f;
                _flight.Place(80000.0, 0.0);
                vessel.Throttle = 1.0;
                await Frames(90);
                await Capture("vacuum");
                _flight.Place(6000.0, 700.0);
                await Frames(90);
                await Capture("crossflow");
                vessel.Velocity = -vessel.Nose * 1800.0 + _flight.Body.AirVelocityAt(vessel.Position);
                await Frames(240, 1.0 / 30.0);
                await Capture("retro");
                vessel.Throttle = 0.0;
                await Frames(60);
                OrbitCamera.Active.Distance = 40.0f;
                vessel.Velocity = _flight.Body.AirVelocityAt(vessel.Position);
                await Frames(60);
                await Capture("soot");
            }

            _flight.PlaceAt(28.52, -80.50, 17.0, 0.0);
            await Settle();
            vessel.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, vessel.Position.Normalized);
            vessel.Throttle = 1.0;
            OrbitCamera.Active.Distance = 100.0f;
            OrbitCamera.Active.Pitch = 0.15f;
            await Frames(6, 1.0 / 30.0);
            await Capture("steam-onset");
            await Frames(24, 1.0 / 30.0);
            await Capture("steam-forming");
            await Frames(210, 1.0 / 30.0);
            await Capture("steam");
            if (OS.GetEnvironment("FT_ENGINE_CHECK") == "water" || OS.GetEnvironment("FT_ENGINE_CHECK") == "surface") {

                await Measure("steam-sustained");
                CloseToSteam();
                await Measure("steam-close");
                await MeasureWithoutSmoke("steam-close-no-smoke");
                FreeCamera.Active.Release();

            }
            vessel.Throttle = 0.0;
            await Frames(180, 1.0 / 30.0);
            await Capture("steam-decay");
            if (OS.GetEnvironment("FT_ENGINE_CHECK") == "surface") { await SmokeStress(); }
            _flight.PlaceAt(28.0, -81.0, 35.0, 0.0);
            await Settle();
            vessel.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, vessel.Position.Normalized);
            vessel.Throttle = 1.0;
            await Frames(6, 1.0 / 30.0);
            await Capture("dust-onset");
            await Frames(24, 1.0 / 30.0);
            await Capture("dust-forming");
            await Frames(210, 1.0 / 30.0);
            await Capture("dust");
            if (OS.GetEnvironment("FT_ENGINE_CHECK") == "surface") {

                OrbitCamera.Active.Distance = 280.0f;
                OrbitCamera.Active.Pitch = 0.45f;
                await Frames(30);
                await Capture("dust-distant");
                await Measure("dust-distant");

            }
            GD.Print("Engine visual checks complete");
            GetTree().Quit();

        } catch (Exception exception) {

            GD.PushError(exception.ToString());
            GetTree().Quit(1);

        }

    }

}
