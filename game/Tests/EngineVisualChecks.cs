using System;
using System.Reflection;
using System.Threading.Tasks;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class EngineVisualChecks : Node {

    private Flight _flight;
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
        GD.Print($"ENGINE CAPTURE {name}: thrust={_flight.Vessel.CurrentThrust:F1} GPU={RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()):F2}ms");

    }

    public override async void _Ready() {

        try {

            Main main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            _flight = Flight.Active;
            _flight.DebugPaused = true;
            GetTree().Root.Mode = Window.ModeEnum.Windowed;
            GetTree().Root.Size = new Vector2I(1280, 720);
            main.GetNode<CanvasLayer>("Hud").Hide();
            await Settle();
            Vessel vessel = _flight.Vessel;
            OrbitCamera.Active.Distance = 95.0f;
            OrbitCamera.Active.Pitch = 0.15f;
            vessel.Throttle = 1.0;
            await Frames(90);
            await Capture("pad");
            _flight.Place(6000.0, 0.0);
            vessel.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, vessel.Position.Normalized);
            await Settle();
            await Frames(90);
            await Capture("atmosphere");
            for (int i = 1; i < vessel.EngineCount; i++) { vessel.SetEngine(i, false); }
            await Frames(90);
            await Capture("single-engine");
            if (vessel.CurrentThrust <= 0.0) { throw new InvalidOperationException("Remaining engine lost thrust"); }
            int flames = 0;
            foreach (Node node in VesselView.Active.FindChildren("Plume*", "MeshInstance3D", true, false)) {

                MeshInstance3D flame = (MeshInstance3D)node;
                if (flame.Visible) { flames++; }
                if (flame.Mesh is not CylinderMesh) { throw new InvalidOperationException("Engine flame must use native mesh geometry"); }

            }
            if (flames != 1) { throw new InvalidOperationException($"Expected one firing engine, found {flames}"); }
            vessel.Throttle = 0.0;
            await Frames(180);
            await Capture("off");
            foreach (Node node in VesselView.Active.FindChildren("Plume*", "MeshInstance3D", true, false)) {

                if (((MeshInstance3D)node).Visible) { throw new InvalidOperationException("Flame survived shutdown"); }

            }
            for (int i = 0; i < vessel.EngineCount; i++) { vessel.SetEngine(i, true); }
            _flight.Place(80000.0, 0.0);
            await Settle();
            vessel.Throttle = 1.0;
            await Frames(90);
            await Capture("vacuum");
            _flight.Separate();
            vessel = _flight.Vessel;
            vessel.ControlTorque = Vector3d.UnitX * vessel.ControlTorqueLimit;
            await Frames(60);
            await Capture("staged-rcs");
            int jets = 0;
            foreach (Node node in VesselView.Active.FindChildren("*", "MeshInstance3D", true, false)) {

                MeshInstance3D jet = (MeshInstance3D)node;
                if (jet.Name.ToString().StartsWith("Jet") && jet.Visible) { jets++; }

            }
            if (jets == 0) { throw new InvalidOperationException("Staged vehicle lost RCS visuals"); }
            vessel.ControlTorque = Vector3d.Zero;
            vessel.Throttle = 0.0;
            _flight.Place(12000.0, 4500.0);
            vessel.Velocity -= vessel.Position.Normalized * 500.0;
            OrbitCamera.Active.Distance = 35.0f;
            await Settle();
            await Frames(90);
            await Capture("entry");
            MeshInstance3D sheath = (MeshInstance3D)typeof(VesselView).GetField("_sheath", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(VesselView.Active);
            if (!sheath.Visible) { throw new InvalidOperationException($"Entry sheath inactive at heat flux {vessel.Aero.HeatFlux}"); }
            GD.Print("Engine visual checks complete");
            GetTree().Quit();

        } catch (Exception exception) {

            GD.PushError(exception.ToString());
            GetTree().Quit(1);

        }

    }

}
