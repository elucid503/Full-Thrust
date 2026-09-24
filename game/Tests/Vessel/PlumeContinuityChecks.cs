using System;
using System.Linq;
using FullThrust.Sim;
using static FullThrust.Game.Checks;
using Godot;

namespace FullThrust.Game;

public sealed partial class PlumeContinuityChecks : Node {
    public override void _Ready() {
        Flight flight = new();
        try {
            // Supply the flight clock without starting terrain, controls, or the main scene.
            typeof(Flight).GetProperty("Active").SetValue(null, flight);
            typeof(Flight).GetProperty("Body").SetValue(flight, BodyCatalog.Home);
            typeof(Flight).GetProperty("Time").SetValue(flight, 2.0);
            Vessel vessel = new("test", new[] { Zenith.BuildStage() }) {
                Position = Vector3d.UnitZ * (BodyCatalog.Home.Radius + 1000.0), Throttle = 1.0
            };
            typeof(Flight).GetProperty("Vessel").SetValue(flight, vessel);
            vessel.Active.AdvanceEngines(1.0, 100000.0, 2.0);
            VesselView view = new();
            AddChild(view);
            view.Build(vessel);
            for (int pass = 0; pass < 3; pass++) {
                for (int i = 0; i < vessel.EngineCount; i++) {
                    vessel.Active.EngineStates[i].Traverse(new Vector3d(0.12, i % 2 == 0 ? 0.04 : -0.04, 0.0), 0.2, 0.01, 1.0);
                }
                if (pass == 1) {
                    vessel.SetEngine(0, false);
                    vessel.Active.AdvanceEngines(1.0, 100000.0, 2.0);
                }
                if (pass == 2) { vessel.Orientation = QuaternionD.FromAxisAngle(Vector3d.UnitX, Math.PI); }
                view.Sync(Frames.Point(vessel.Position), Frames.Rotation(vessel.Orientation));
                ExhaustTail tail = view.FindChildren("Tail", "", true, false).OfType<ExhaustTail>().Single();
                Plume[] nozzles = view.FindChildren("Plume*", "", true, false).OfType<Plume>().ToArray();
                Vector3 centre = Vector3.Zero, direction = Vector3.Zero;
                float power = 0.0f;
                int live = 0;
                for (int i = 0; i < nozzles.Length; i++) {
                    float throttle = (float)vessel.Active.EngineStates[i].Power;
                    if (throttle <= 0.001f) { continue; }
                    centre += nozzles[i].GlobalPosition * throttle;
                    direction -= nozzles[i].GlobalBasis.Y * throttle;
                    power += throttle;
                    live++;
                }
                if (tail.GlobalPosition.DistanceTo(centre / power) > 0.15f || (-tail.GlobalBasis.Y).Dot(direction.Normalized()) < 0.9999f) {
                    throw new InvalidOperationException("Merged tail lost the weighted nozzle origin or gimbal direction");
                }
                MeshInstance3D mesh = tail.GetChildren().OfType<MeshInstance3D>().First();
                ShaderMaterial material = (ShaderMaterial)mesh.MaterialOverride;
                if (mesh.Position != Vector3.Zero || material.GetShaderParameter("stream_count").AsInt32() != live) {
                    throw new InvalidOperationException("Merged tail lost its individual streams or acquired a detached start");
                }
                if (material.GetShaderParameter("stream_exits").AsVector4Array().Length != 32) {
                    throw new InvalidOperationException("Nozzle footprint uniforms failed to bind");
                }
            }
            foreach (string name in new[] { "Volumetric", "Cones", "Tail", "Distortion" }) {
                Shader shader = GD.Load<Shader>($"res://Shaders/Exhaust/{name}.gdshader");
                if (shader.GetShaderUniformList().Count == 0) { throw new InvalidOperationException($"{name} shader failed to parse"); }
            }
            CheckContactFields(flight, vessel, view);
            GD.Print("Plume continuity: gimbal, partial shutdown, inverted attitude, stream bindings and shader parsing passed");
            GetTree().Quit();
        } catch (Exception exception) {
            Fail(this, exception);
        } finally {
            typeof(Flight).GetProperty("Active").SetValue(null, null);
            flight.Free();
        }
    }

    private void CheckContactFields(Flight flight, Vessel source, VesselView sourceView) {
        Plume emitter = sourceView.FindChildren("Plume*", "", true, false).OfType<Plume>().First();
        Vessel receiver = new("receiver", new[] { Meridian.BuildStage() }) {
            Position = Frames.Origin + Frames.Sim(emitter.GlobalPosition - emitter.GlobalBasis.Y * 8.0f),
            Orientation = source.Orientation
        };
        VesselView receiverView = new();
        AddChild(receiverView);
        receiverView.Build(receiver, false);
        ((System.Collections.Generic.List<Flight.Tracked>)flight.Debris).Add(new Flight.Tracked { Vessel = receiver });
        EngineState engine = receiver.Active.EngineStates[0];
        if (engine.ContactProfile.Length < 3 || engine.ContactProfile.Length > 65 || receiver.Active.ContactRevision == 0) { throw new InvalidOperationException("Imported engine did not refine the contact profile"); }
        PlumeContact contact = new();
        ShaderMaterial material = new() { Shader = GD.Load<Shader>("res://Shaders/Exhaust/Tail.gdshader") };
        contact.Sync(emitter, emitter.ExitRadius, source);
        contact.Write(material, Vector3.Zero);
        if (material.GetShaderParameter("contact_engine_count0").AsInt32() != receiver.EngineCount ||
            material.GetShaderParameter("contact_hardware0").AsGodotObject() is not Texture2DArray) {
            throw new InvalidOperationException("Receiving engines are missing from the plume contact field");
        }
        Transform3D before = material.GetShaderParameter("contact_engine_transforms0").AsGodotArray()[0].AsTransform3D();
        engine.Traverse(Vector3d.UnitX * 0.1, 0.2, 0.01, 1.0);
        contact.Sync(emitter, emitter.ExitRadius, source);
        contact.Write(material, Vector3.Zero);
        Transform3D after = material.GetShaderParameter("contact_engine_transforms0").AsGodotArray()[0].AsTransform3D();
        if (before.IsEqualApprox(after)) { throw new InvalidOperationException("Receiving engine field did not follow gimbal"); }

        Vessel capsule = new("capsule", new[] { Aegis.BuildStage(0.0) });
        VesselView capsuleView = new();
        AddChild(capsuleView);
        capsuleView.Build(capsule, false);
        if (capsule.Active.ContactHull == null || capsule.Active.ContactHull.Stations.Count < 3 || capsule.Active.ContactHull.Stations.Count > 65) {
            throw new InvalidOperationException("Imported capsule did not refine the contact hull");
        }
        GD.Print("Contact geometry: imported capsule and bell profiles, hardware texture array and live receiver gimbal passed");
        material.Dispose();
    }
}
