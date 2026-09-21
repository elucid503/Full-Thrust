using System;
using System.Reflection;
using System.Threading.Tasks;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class EngineVisualChecks : Node {

    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    private Flight _flight;
    private readonly PropertyInfo _clock = typeof(Flight).GetProperty("Time");

    private Vector3d Nozzle(Vessel vessel) => vessel.Position + vessel.Orientation.Rotate(Vector3d.UnitZ * (vessel.Base - vessel.CentreOfMassZ));

    // Parks the free camera at an inertial eye looking at an inertial target, then lets the scene settle on it.
    private async Task Look(Vector3d eye, Vector3d target, int frames = 30) {

        if (!FreeCamera.Flying) { FreeCamera.Active.Take(OrbitCamera.Active); }
        FreeCamera camera = FreeCamera.Active;
        Vector3d fixedEye = _flight.Body.ToBodyFixed(eye, _flight.Time);
        typeof(FreeCamera).GetField("_where", Fields).SetValue(camera, fixedEye);
        Vector3 up = FullThrust.Game.Frames.Direction(eye.Normalized);
        Vector3 forward = FullThrust.Game.Frames.Direction((target - eye).Normalized);
        FullThrust.Game.Frames.Horizon(up, out Vector3 side, out Vector3 ahead);
        typeof(FreeCamera).GetField("_yaw", Fields).SetValue(camera, Mathf.Atan2(-forward.Dot(side), forward.Dot(ahead)));
        typeof(FreeCamera).GetField("_pitch", Fields).SetValue(camera, Mathf.Clamp(Mathf.Asin(forward.Dot(up)), -1.52f, 1.52f));
        await Frames(frames);
        Vector3 actual = -camera.GlobalTransform.Basis.Z;
        GD.Print($"ENGINE LOOK: aim={actual.Dot(forward):F3} eye-error={(camera.GlobalPosition - FullThrust.Game.Frames.Point(eye)).Length():F2}m");

    }

    private async Task Chase() {

        if (FreeCamera.Flying) { FreeCamera.Active.Release(); }
        await Frames(5);

    }

    private void Extents(string name) {

        foreach (Node node in VesselView.Active.FindChildren("Impact", "", true, false)) {

            foreach (Node child in node.GetChildren()) {

                if (child is GpuParticles3D particles) {

                    Aabb cloud = particles.CaptureAabb();
                    GD.Print($"ENGINE CLOUD {name} {particles.Name}: emitting={particles.Emitting} ratio={particles.AmountRatio:F2} extent={cloud.Size.Length():F1}m");

                }

            }

        }

    }

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

    private async Task<double> GpuMedian(int count) {

        double[] samples = new double[count];
        for (int i = 0; i < count; i++) {

            await Frames(1);
            samples[i] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());

        }
        Array.Sort(samples);
        return samples[count / 2];

    }

    // The exhaust's own cost: the same scene with every plume, shimmer, light and cloud hidden.
    private async Task Cost(string name) {

        double with = await GpuMedian(40);
        Plume.Enabled = false;
        Godot.Collections.Array<Node> particles = VesselView.Active.FindChildren("*", "GPUParticles3D", true, false);
        foreach (Node node in particles) { ((GpuParticles3D)node).Visible = false; }
        double without = await GpuMedian(40);
        Plume.Enabled = true;
        foreach (Node node in particles) { ((GpuParticles3D)node).Visible = true; }
        await Frames(10);
        GD.Print($"ENGINE COST {name}: exhaust={with - without:F2}ms scene={with:F2}ms bare={without:F2}ms");
        if (with - without > 2.0) { throw new InvalidOperationException($"Exhaust cost {with - without:F2}ms at {name} is over budget"); }

    }

    private async Task Capture(string name) {

        await Frames(3, 0.0);
        using Image shot = GetViewport().GetTexture().GetImage();
        shot.SavePng($"res://.artifacts/engine-{name}.png");
        GD.Print($"ENGINE CAPTURE {name}: thrust={_flight.Vessel.CurrentThrust:F1} GPU={RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()):F2}ms");

    }

    // Hovers on full thrust a few metres over the sea east of the site, where the water regression finds open water.
    private async Task Water(Vessel vessel) {

        double longitude = -80.64;
        double best = double.MaxValue;
        for (int i = 0; i <= 1200; i++) {

            double candidate = -80.64 + i * 0.0003;
            double latitude = 28.52 * Math.PI / 180.0;
            Vector3d direction = new(Math.Cos(latitude) * Math.Cos(candidate * Math.PI / 180.0), Math.Cos(latitude) * Math.Sin(candidate * Math.PI / 180.0), Math.Sin(latitude));
            _flight.Body.Terrain.Elevation(direction, 0.0, out double coast);
            if (Math.Abs(coast + 8.0) < best) { best = Math.Abs(coast + 8.0); longitude = candidate; }

        }
        _flight.PlaceAt(28.52, longitude, 16.0, 0.0);
        vessel.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, vessel.Position.Normalized);
        vessel.Velocity = _flight.Body.AirVelocityAt(vessel.Position);
        vessel.Throttle = 1.0;
        OrbitCamera.Active.Distance = 60.0f;
        OrbitCamera.Active.Pitch = 0.12f;
        await Settle();
        await Frames(150);
        await Capture("water");
        Vector3d nozzle = Nozzle(vessel);
        Vector3d up = nozzle.Normalized;
        Vector3d east = Vector3d.Cross(Vector3d.UnitZ, up).Normalized;
        await Look(nozzle + east * 45.0 + up * 12.0, nozzle);
        await Capture("water-close");
        await Frames(60);
        nozzle = Nozzle(vessel);
        up = nozzle.Normalized;
        east = Vector3d.Cross(Vector3d.UnitZ, up).Normalized;
        await Look(nozzle + east * 45.0 + up * 12.0, nozzle, 5);
        await Capture("water-steam");
        Extents("water");
        await Chase();
        bool steaming = false;
        foreach (Node node in VesselView.Active.FindChildren("Impact", "", true, false)) {

            if (node is ExhaustImpact impact && impact.Active) { steaming = true; }

        }
        if (!steaming) { throw new InvalidOperationException("Sea hover raised no steam"); }
        await Cost("water");
        OrbitCamera.Active.Distance = 95.0f;
        OrbitCamera.Active.Pitch = 0.15f;

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
            bool smoking = false;
            foreach (Node node in VesselView.Active.FindChildren("Impact", "", true, false)) {

                if (node is ExhaustImpact impact && impact.Active) { smoking = true; }

            }
            if (!smoking) { throw new InvalidOperationException("Pad burn raised no exhaust cloud"); }
            await Cost("pad");
            Vector3d nozzle = Nozzle(vessel);
            Vector3d up = nozzle.Normalized;
            Vector3d east = Vector3d.Cross(Vector3d.UnitZ, up).Normalized;
            await Look(nozzle + east * 18.0 + up * 2.0, nozzle - up * 3.0);
            await Capture("pad-close");
            Extents("pad");
            await Cost("pad-close");
            nozzle = Nozzle(vessel);
            up = nozzle.Normalized;
            east = Vector3d.Cross(Vector3d.UnitZ, up).Normalized;
            await Look(nozzle + east * 60.0 + up * 14.0, nozzle);
            await Capture("pad-cloud");
            await Chase();
            if (OS.GetEnvironment("FT_ENGINE_CHECK") == "pad") { GD.Print("Engine visual checks complete (pad only)"); GetTree().Quit(); return; }
            _flight.Place(6000.0, 0.0);
            vessel.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, vessel.Position.Normalized);
            await Settle();
            await Frames(90);
            await Capture("atmosphere");
            await Cost("atmosphere");
            nozzle = Nozzle(vessel);
            up = nozzle.Normalized;
            east = Vector3d.Cross(Vector3d.UnitZ, up).Normalized;
            await Look(nozzle + east * 3.0 - up * 6.0, nozzle);
            await Capture("bells");
            await Chase();
            for (int i = 1; i < vessel.EngineCount; i++) { vessel.SetEngine(i, false); }
            await Frames(90);
            await Capture("single-engine");
            nozzle = Nozzle(vessel);
            up = nozzle.Normalized;
            east = Vector3d.Cross(Vector3d.UnitZ, up).Normalized;
            await Look(nozzle + east * 9.0 - up * 2.0, nozzle - up * 3.0);
            await Capture("diamonds");
            await Chase();
            if (vessel.CurrentThrust <= 0.0) { throw new InvalidOperationException("Remaining engine lost thrust"); }
            int flames = 0;
            foreach (Node node in VesselView.Active.FindChildren("Plume*", "", true, false)) {

                if (node is not Plume plume) { continue; }
                if (plume.Visible) { flames++; }
                foreach (Node layer in plume.FindChildren("*", "MeshInstance3D", false, false)) {

                    if (((MeshInstance3D)layer).Mesh is not BoxMesh && layer.Name != "Shimmer") { throw new InvalidOperationException("Plume layers must use volume proxies"); }

                }

            }
            if (flames != 1) { throw new InvalidOperationException($"Expected one firing engine, found {flames}"); }
            vessel.Throttle = 0.0;
            await Frames(180);
            await Capture("off");
            nozzle = Nozzle(vessel);
            up = nozzle.Normalized;
            east = Vector3d.Cross(Vector3d.UnitZ, up).Normalized;
            await Look(nozzle + east * 4.0 - up * 3.0, nozzle);
            await Capture("bell-cooling");
            await Chase();
            foreach (Node node in VesselView.Active.FindChildren("Plume*", "", true, false)) {

                if (node is Plume plume && plume.Visible) { throw new InvalidOperationException("Flame survived shutdown"); }

            }
            for (int i = 0; i < vessel.EngineCount; i++) { vessel.SetEngine(i, true); }
            _flight.Place(80000.0, 0.0);
            await Settle();
            vessel.Throttle = 1.0;
            await Frames(90);
            await Capture("vacuum");
            await Cost("vacuum");
            nozzle = Nozzle(vessel);
            up = nozzle.Normalized;
            east = Vector3d.Cross(Vector3d.UnitZ, up).Normalized;
            await Look(nozzle + east * 28.0 - up * 6.0, nozzle - up * 10.0);
            await Capture("vacuum-close");
            await Chase();
            await Water(vessel);
            _flight.Separate();
            vessel = _flight.Vessel;
            vessel.ControlTorque = Vector3d.UnitX * vessel.ControlTorqueLimit;
            await Frames(60);
            await Capture("staged-rcs");
            up = vessel.Position.Normalized;
            east = Vector3d.Cross(Vector3d.UnitZ, up).Normalized;
            await Look(vessel.Position + east * 14.0 + up * 3.0, vessel.Position + up * 2.0, 20);
            await Capture("rcs-close");
            await Chase();
            int jets = 0;
            foreach (Node node in VesselView.Active.FindChildren("Jet*", "", true, false)) {

                if (node is Plume jet && jet.Visible) { jets++; }

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
