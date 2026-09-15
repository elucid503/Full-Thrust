using System;
using System.Reflection;
using System.Threading.Tasks;
using FullThrust.Sim;
using Godot;

namespace FullThrust.Game;

public sealed partial class EngineVisualChecks {

    private void CloseCamera(Vector3d focus, Vector3d offset) {

        FreeCamera camera = FreeCamera.Active;
        camera.Take(OrbitCamera.Active);
        Vector3d where = focus + _flight.Vessel.Orientation.Rotate(offset);
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(FreeCamera).GetMethod("Settle", hidden).Invoke(camera, new object[] { where });
        Vector3 forward = FullThrust.Game.Frames.Direction((focus - where).Normalized);
        Vector3 up = FullThrust.Game.Frames.Direction(where.Normalized);
        FullThrust.Game.Frames.Horizon(up, out Vector3 side, out Vector3 ahead);
        typeof(FreeCamera).GetField("_yaw", hidden).SetValue(camera, Mathf.Atan2(-forward.Dot(side), forward.Dot(ahead)));
        typeof(FreeCamera).GetField("_pitch", hidden).SetValue(camera, Mathf.Asin(forward.Dot(up)));

    }

    private void CloseToSteam() {

        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var wakes = (System.Collections.IList)typeof(Planet).GetField("_surfaceWakes", flags).GetValue(Planet.Active);
        object wake = wakes[0];
        Type type = wake.GetType();
        var puffs = (System.Collections.IList)type.GetField("Puffs").GetValue(wake);
        Vector4[] centres = (Vector4[])type.GetField("Centres").GetValue(wake);
        MeshInstance3D volume = (MeshInstance3D)type.GetField("Volume").GetValue(wake);
        Vector4 centre = centres[puffs.Count / 2];
        Vector3d focus = FullThrust.Game.Frames.Origin + FullThrust.Game.Frames.Sim(volume.GlobalTransform * new Vector3(centre.X, centre.Y, centre.Z));
        CloseCamera(focus, new Vector3d(0.5, -1.5, 0.2) * centre.W);

    }

    private async Task Measure(string name, bool moveTarget = false, bool advance = true) {

        await Frames(30, advance ? 1.0 / 60.0 : 0.0);
        int blocked = 0;
        double[] gpu = new double[120], cpu = new double[120], vessel = new double[120], loads = new double[120];
        for (int i = 0; i < gpu.Length; i++) {

            if (moveTarget) {

                Vessel target = _flight.Debris[0].Vessel;
                target.Position += _flight.Vessel.Orientation.Rotate(Vector3d.UnitY * (Math.Sin(i * 0.07) * 0.005));
                target.Orientation *= QuaternionD.FromAxisAngle(Vector3d.UnitZ, 0.002);

            }
            await Frames(1, advance ? 1.0 / 60.0 : 0.0);
            blocked = Math.Max(blocked, VesselView.Active.PlumeObstacles);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            VesselView.UpdateExhaustLoads();
            loads[i] = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            gpu[i] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
            cpu[i] = _main.UpdateMilliseconds;
            vessel[i] = _main.VesselMilliseconds;

        }
        Array.Sort(gpu); Array.Sort(cpu); Array.Sort(vessel); Array.Sort(loads);
        GD.Print($"AUDIT {name}: GPU p50={gpu[60]:F2} p95={gpu[114]:F2}; CPU p50={cpu[60]:F2} p95={cpu[114]:F2}; vessels p50={vessel[60]:F2} p95={vessel[114]:F2} ms; loads p50={loads[60]:F2} p95={loads[114]:F2} ms; blocked={blocked}");
        var wakes = (System.Collections.IList)typeof(Planet).GetField("_surfaceWakes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Planet.Active);
        float longestBuild = 0.0f;
        foreach (object wake in wakes) {

            if (wake.GetType().GetField("Field").GetValue(wake) is SurfaceCloudVolume field) { longestBuild = Math.Max(longestBuild, field.BuildMilliseconds); }

        }
        if (wakes.Count > 0) { GD.Print($"SURFACE {name}: wakes={wakes.Count}; longest latest background build={longestBuild:F2}ms"); }
        await Capture("audit-" + name);

    }

    private async Task MeasureWithoutSmoke(string name) {

        await Measure(name.Replace("-no-smoke", "-held"), false, false);
        var volumes = new System.Collections.Generic.List<MeshInstance3D>();
        foreach (Node node in Planet.Active.GetChildren()) {

            if (node is MeshInstance3D mesh && mesh.MaterialOverride is ShaderMaterial material
                && material.Shader.ResourcePath == "res://Shaders/Steam.gdshader") {

                mesh.Layers = 0;
                volumes.Add(mesh);

            }

        }
        await Measure(name, false, false);
        foreach (MeshInstance3D volume in volumes) { volume.Layers = 2; }

    }

    private async Task SmokeStress() {

        Vector3d up = _flight.Body.ToBodyFixed(_flight.Vessel.Position, _flight.Time).Normalized;
        Vector3d side = Vector3d.Cross(up, Vector3d.UnitZ).Normalized;
        Vector3d forward = Vector3d.Cross(up, side);
        Vector3d[] sources = new Vector3d[8];
        for (int i = 0; i < sources.Length; i++) {

            double angle = i * Math.Tau / sources.Length;
            sources[i] = (up * _flight.Body.Radius + (side * Math.Cos(angle) + forward * Math.Sin(angle)) * 38.0).Normalized * _flight.Body.Radius;

        }
        _surfaceTestInjection = () => {

            foreach (Vector3d source in sources) {

                Vector3d point = _flight.Body.ToInertial(source, _flight.Time);
                Vector3d normal = point.Normalized;
                Planet.Active.Disturb(FullThrust.Game.Frames.Point(point + normal * 8.0), FullThrust.Game.Frames.Direction(-normal),
                    30.0f, 1.5f, 2.0f, 0.12f, new ExhaustInteraction.SurfaceHit(point, normal, 8.0, true));

            }

        };
        CloseCamera(_flight.Vessel.Position, new Vector3d(60.0, -60.0, 20.0));
        await Frames(360, 1.0 / 30.0);
        await Measure("eight-water-impacts");
        await MeasureWithoutSmoke("eight-water-impacts-no-smoke");
        _surfaceTestInjection = null;
        FreeCamera.Active.Release();
        await Frames(390, 1.0 / 30.0);
        await Capture("eight-water-impacts-cleared");

    }

    private async Task RetroSoot() {

        Vessel vessel = _flight.Vessel;
        _flight.Separate();
        foreach (double altitude in new[] { 6000.0, 1000.0, 35000.0 }) {

            FreeCamera.Active.Release();
            _flight.Place(altitude, 0.0);
            _flight.Debris[0].Vessel.Position = vessel.Position + Vector3d.UnitZ * 5000.0;
            vessel.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, vessel.Position.Normalized);
            vessel.Velocity = -vessel.Nose * 1800.0 + _flight.Body.AirVelocityAt(vessel.Position);
            vessel.Throttle = 1.0;
            await Frames(120);
            foreach (Node node in VesselView.Active.FindChildren("*", "MeshInstance3D", true, false)) {

                if (node is not MeshInstance3D mesh || !mesh.Visible
                    || mesh.MaterialOverride is not ShaderMaterial material
                    || material.Shader.ResourcePath != "res://Shaders/Plume.gdshader"
                    || material.GetShaderParameter("reaction_control").AsBool()) { continue; }
                float standoff = material.GetShaderParameter("standoff").AsSingle();
                float radius = material.GetShaderParameter("blanket_radius").AsSingle();
                Vector3 centre = mesh.GlobalTransform * new Vector3(0.0f, -standoff, 0.0f);
                Vector3d focus = FullThrust.Game.Frames.Origin + FullThrust.Game.Frames.Sim(centre);
                GD.Print($"RETRO {altitude}: standoff={standoff:F2} radius={radius:F2} opposing={material.GetShaderParameter("opposing")}");
                CloseCamera(focus, new Vector3d(3.5, -2.0, 0.0) * Math.Max(radius, 2.0f));
                await Frames(30, 0.0);
                await Capture($"retro-{altitude}-side");
                await Frames(15);
                centre = mesh.GlobalTransform * new Vector3(0.0f, -standoff, 0.0f);
                focus = FullThrust.Game.Frames.Origin + FullThrust.Game.Frames.Sim(centre);
                CloseCamera(focus, new Vector3d(3.5, -2.0, 0.0) * Math.Max(radius, 2.0f));
                await Frames(30, 0.0);
                await Capture($"retro-{altitude}-moving");
                CloseCamera(focus, new Vector3d(2.8, -1.5, -2.0) * Math.Max(radius, 2.0f));
                await Frames(30, 0.0);
                await Capture($"retro-{altitude}-oblique");
                break;

            }
            await Frames(240, 1.0 / 30.0);
            vessel.Throttle = 0.0;
            vessel.Velocity = _flight.Body.AirVelocityAt(vessel.Position);
            await Frames(60);
            CloseCamera(vessel.Position, new Vector3d(14.0, -10.0, 1.0));
            await Frames(30, 0.0);
            await Capture($"soot-{altitude}");
            CloseCamera(vessel.Position, new Vector3d(-14.0, 10.0, 1.0));
            await Frames(30, 0.0);
            await Capture($"soot-{altitude}-reverse");

        }
        GD.Print("Retrograde and soot checks complete");

    }

    private async Task Audit() {

        Vessel vessel = _flight.Vessel;
        _flight.Place(6000.0, 0.0);
        vessel.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, vessel.Position.Normalized);
        vessel.Throttle = 1.0;
        await Frames(60);
        CloseCamera(vessel.Position - vessel.Nose * (vessel.CentreOfMassZ + 7.0), new Vector3d(5.0, -4.0, 0.0));
        await Measure("cluster-close");
        FreeCamera.Active.Release();
        _flight.Separate();
        Vessel other = _flight.Debris[0].Vessel;
        _flight.Place(35000.0, 0.0);
        other.Position = vessel.Position + Vector3d.UnitZ * 5000.0;
        vessel.Orientation = QuaternionD.FromTo(Vector3d.UnitZ, vessel.Position.Normalized);
        vessel.Velocity = -vessel.Nose * 1800.0 + _flight.Body.AirVelocityAt(vessel.Position);
        vessel.Throttle = 1.0;
        OrbitCamera.Active.Distance = 80.0f;
        await Measure("retro-upper");
        vessel.Velocity = _flight.Body.AirVelocityAt(vessel.Position);
        CloseCamera(vessel.Position - vessel.Nose * (vessel.CentreOfMassZ - vessel.Active.Hull.Base + 3.0), new Vector3d(5.0, -4.0, 0.0));
        vessel.Throttle = 0.0;
        await Frames(14);
        await Capture("audit-purge");
        await Frames(60);
        FreeCamera.Active.Release();
        _flight.Place(80000.0, 0.0);
        other.Position = vessel.Position + Vector3d.UnitZ * 5000.0;
        vessel.TranslationCommand = new Vector3d(1.0, 1.0, 1.0);
        OrbitCamera.Active.Distance = 12.0f;
        await Measure("rcs-alone");
        other.Position = vessel.Position + vessel.Orientation.Rotate(new Vector3d(-5.0, -4.0, 0.0));
        other.Orientation = vessel.Orientation;
        await Measure("rcs-near-vessel");
        other.Position = vessel.Position + vessel.Nose * ((vessel.Base + vessel.Tip) * 0.5 - vessel.CentreOfMassZ - ((other.Base + other.Tip) * 0.5 - other.CentreOfMassZ))
            + vessel.Orientation.Rotate(new Vector3d(-3.0, -2.5, 0.0));
        await Measure("rcs-moving-contact", true);
        GD.Print("Engine audit complete");

    }

}
