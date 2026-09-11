using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class GroundScatterChecks : Node {

    private int _checks;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public override void _Ready() {

        try {

            Run(false);
            Run(true);
            ObstacleChecks();
            ImportedTreeChecks();
            GD.Print($"Terrain scatter: {_checks} checks passed");
            GetTree().Quit();

        } catch (Exception error) {

            GD.PushError(error.ToString());
            GetTree().Quit(1);

        }

    }

    private void ImportedTreeChecks() {
        int species = 0;
        foreach (string name in new[] { "Tree_1", "Pine_1", "Birch_1" }) {
            using ArrayMesh mesh = TreeAssets.Load(name, species++);
            Check(mesh.GetSurfaceCount() == 1, name + ": one instanced surface");
            Check(Mathf.Abs(mesh.GetAabb().Size.Y - 10.5f) < 0.01f, name + ": normalized physical height");
            int triangles = mesh.SurfaceGetArrayIndexLen(0) / 3;
            GD.Print($"{name}: {triangles} triangles; {mesh.GetMeta("lod_count")} LODs");
            Check(triangles > 0 && triangles <= 2500, name + ": triangle budget");
            Check(mesh.GetMeta("lod_count").AsInt32() > 0, name + ": distant mesh LODs");
            using MultiMesh multi = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = mesh, InstanceCount = 1 };
            ScatterObstacles obstacles = new(true);
            Vector3d anchor = Vector3d.UnitX * BodyCatalog.Home.Radius;
            obstacles.Add(name, anchor, new[] { Transform3D.Identity }, multi);
            Vessel probe = new("crown probe", new[] { Aegis.BuildStage(0) });
            float width = Mathf.Max(Mathf.Abs(mesh.GetAabb().Position.X), Mathf.Abs(mesh.GetAabb().End.X));
            Vector3d from = anchor + new Vector3d(width * 0.85, -20, 7);
            probe.Position = anchor + new Vector3d(width * 0.85, 20, 7);
            probe.Velocity = Vector3d.UnitY * 100 + BodyCatalog.Home.AirVelocityAt(probe.Position);
            Check(obstacles.Resolve(BodyCatalog.Home, probe, from, QuaternionD.Identity, 0, 0),
                name + ": outer crown collides beyond trunk");
            obstacles.Remove(name);
        }
    }

    private void ObstacleChecks() {
        CelestialBody body = BodyCatalog.Home;
        Vector3d anchor = Vector3d.UnitX * (body.Radius + 100);
        using MultiMesh mesh = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh(), InstanceCount = 1 };
        Transform3D transform = new(Basis.Identity, Vector3.Zero);
        mesh.SetInstanceTransform(0, transform);
        ScatterObstacles obstacles = new(true);
        obstacles.Add((1, 2), anchor, new[] { transform }, mesh);
        Vessel vessel = new("probe", new[] { Aegis.BuildStage(0) });
        Vector3d start = anchor + new Vector3d(-20, 0, 4);
        vessel.Position = anchor + new Vector3d(20, 0, 4);
        vessel.Velocity = Vector3d.UnitX * 100 + body.AirVelocityAt(vessel.Position);
        Check(obstacles.Resolve(body, vessel, start, QuaternionD.Identity, 0, 0), "swept vessel destroys trunk");
        Check(obstacles.DestroyedCount == 1 && obstacles.EffectCount == 1, "one destruction and fall effect");
        Check(vessel.Velocity.X < 100, "breaking tree consumes impact energy");
        Check(!obstacles.Resolve(body, vessel, start, QuaternionD.Identity, 0, 0), "broken trunk stops colliding");
        obstacles.Animate(13);
        Check(obstacles.EffectCount == 0, "fall expires on simulation time");
        obstacles.Remove((1, 2));
        obstacles.Add((1, 2), anchor, new[] { transform }, mesh);
        Check(!obstacles.Resolve(body, vessel, start, QuaternionD.Identity, 0, 0), "destroyed instance stays noncolliding after reload");
        if (DisplayServer.GetName() != "headless") {
            Check(mesh.GetInstanceTransform(0).Basis.Determinant() == 0, "destroyed instance stays hidden after reload");
        }
        obstacles.Remove((1, 2));
        ScatterObstacles rocks = new(false);
        rocks.Add((3, 4), anchor, new[] { transform }, mesh);
        start = anchor + new Vector3d(-5, 0, 0.5);
        vessel.Position = anchor + new Vector3d(0, 0, 0.5);
        vessel.Velocity = Vector3d.UnitX + body.AirVelocityAt(vessel.Position);
        Check(rocks.Resolve(body, vessel, start, QuaternionD.Identity, 0, 0), "slow vessel collides with rock");
        Check(rocks.DestroyedCount == 0 && vessel.Position.X < anchor.X - 0.5, "solid rock blocks without breaking");
        rocks.Remove((3, 4));
        vessel.AngularVelocity = Vector3d.Zero;
        MultiMeshInstance3D owner = new() { Multimesh = mesh };
        AddChild(owner);
        rocks.Add((5, 6), anchor, new[] { transform }, mesh, owner);
        vessel.Position = anchor + new Vector3d(20, 0, 0.5);
        vessel.Velocity = Vector3d.UnitX * 100 + body.AirVelocityAt(vessel.Position);
        Check(rocks.Resolve(body, vessel, anchor + new Vector3d(-20, 0, 0.5), QuaternionD.Identity, 0, 0),
            "fast vessel breaks rock");
        Check(rocks.DestroyedCount == 1, "rock destruction recorded");
        Check(owner.GetChildCount() == 1 && owner.GetChild<MultiMeshInstance3D>(0).Multimesh.InstanceCount == 4,
            "broken rock emits four instanced fragments");
        rocks.Animate(2);
        Check(owner.GetChild(0).IsQueuedForDeletion(), "fragment node released at expiry");
        Check(rocks.EffectCount == 0, "rock effects expire promptly");
        rocks.Remove((5, 6));
        owner.QueueFree();
        rocks.Add((7, 8), anchor, new[] { transform }, mesh);
        vessel.Position = anchor + Vector3d.UnitX * 10000;
        rocks.Resolve(body, vessel, vessel.Position, QuaternionD.Identity, 0, 0);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { rocks.Resolve(body, vessel, vessel.Position, QuaternionD.Identity, 0, 0); }
        Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 1024, "distant collision queries avoid managed allocation");
        rocks.Remove((7, 8));
    }

    private void Check(bool condition, string label) {

        if (!condition) { throw new InvalidOperationException(label); }
        _checks++;
        GD.Print($"PASS {label}");

    }

    private void Run(bool broad) {

        CelestialBody body = BodyCatalog.Home;
        using Stream stream = File.OpenRead(ProjectSettings.GlobalizePath("res://Assets/Planet/elevation.r16"));
        body.Terrain = Terrain.Load(stream, body.Radius);
        GroundScatter scatter = new GroundScatter();
        AddChild(scatter);
        scatter.Build(body, GD.Load<Texture2D>("res://Assets/Planet/biomes.png"), broad);
        Type type = typeof(GroundScatter);
        Type keyType = type.GetNestedType("Key", BindingFlags.NonPublic);
        int rows = (int)type.GetField("_rows", Private).GetValue(scatter);
        double step = Math.PI / rows;

        object Key(double latitude, double longitude, bool grass) {

            int row = Math.Clamp((int)((latitude / 180.0 * Math.PI + Math.PI * 0.5) / step), 0, rows - 1);
            int columns = (int)type.GetMethod("Columns", Private).Invoke(scatter, new object[] { row });
            int column = (int)((longitude + 180.0) / 360.0 * columns) % columns;
            return Activator.CreateInstance(keyType, row, column, grass);

        }
        object Generate(object key, CancellationToken cancellation = default) =>
            type.GetMethod("Generate", Private).Invoke(scatter, new object[] { key, cancellation });
        Transform3D[] Transforms(object grove) => (Transform3D[])grove.GetType().GetField("Transforms").GetValue(grove);
        Vector3d Anchor(object grove) => (Vector3d)grove.GetType().GetField("Anchor").GetValue(grove);

        foreach (bool grass in new[] { false, true }) {

            object key = Key(28.7, -80.8, grass);
            object grove = Generate(key);
            Transform3D[] first = Transforms(grove);
            Transform3D[] second = Transforms(Generate(key));
            Check(first.Length > 0, $"{grass}: populated dry land");
            Check(first.Length <= (grass ? (broad ? 256 : 1600) : 49), $"{grass}: cell instance budget");
            Check(first.Length == second.Length, $"{grass}: repeat count");
            bool identical = true;
            bool grounded = true;
            bool finite = true;
            foreach (Transform3D transform in first) {

                Vector3d point = Anchor(grove) + Frames.Sim(transform.Origin);
                double burial = point.Length - body.Radius - body.Terrain.Elevation(point.Normalized);
                grounded &= burial <= 0.01 && burial >= (broad ? -0.7 : -0.25);
                finite &= transform.Origin.IsFinite() && transform.Basis.Determinant() > 0.0f;

            }
            for (int index = 0; index < first.Length; index++) { identical &= first[index] == second[index]; }
            Check(identical, $"{grass}: deterministic transforms after regeneration");
            Check(grounded, $"{grass}: roots stay inside their burial budget");
            Check(finite, $"{grass}: finite right-handed transforms");
            Check(Transforms(Generate(Key(0, -30, grass))).Length == 0, $"{grass}: no ocean scatters");

        }
        Check(Transforms(Generate(Key(-89.99, 0, true))).Length == 0, "no grass in polar snow");
        bool cancelled = false;
        try { Generate(Key(28.7, -80.8, true), new CancellationToken(true)); }
        catch (TargetInvocationException error) when (error.InnerException is OperationCanceledException) { cancelled = true; }
        Check(cancelled, "worker responds to cancellation");

        object clearedKey = Key(28.7, -80.8, true);
        Vector3d centre = (Vector3d)type.GetMethod("Centre", Private).Invoke(scatter, new[] { clearedKey });
        body.Terrain.Add(new Terrain.Plateau {

            Centre = centre,
            Height = body.Terrain.Elevation(centre),
            InnerRadius = 500.0,
            OuterRadius = 600.0,

        });
        Check(Transforms(Generate(clearedKey)).Length == 0, "launch clearing excludes grass");
        Check(Transforms(Generate(Key(28.7, -80.8, false))).Length == 0, "launch clearing excludes stones");

        foreach (double latitude in new[] { 0.0, 28.7, 89.9999, -89.9999 }) {

            object key = Key(latitude, 179.9999, false);
            Vector3d direction = (Vector3d)type.GetMethod("Centre", Private).Invoke(scatter, new[] { key });
            type.GetMethod("Select", Private).Invoke(scatter, new object[] { direction * body.Radius, true });
            ICollection queue = (ICollection)type.GetField("_queue", Private).GetValue(scatter);
            Check(queue.Count > 0 && queue.Count <= 1200, $"bounded cell selection at latitude {latitude}");

        }
        type.GetMethod("Select", Private).Invoke(scatter, new object[] { Vector3d.UnitX * body.Radius, false });
        Check(scatter.Pending == 0 && scatter.CellCount == 0, "no streaming outside close range");
        LaunchSite site = LaunchSite.Home;
        site.Commission(body);
        object siteGrove = Generate(Key(28.52, -80.62, true));
        Transform3D[] sitePlants = Transforms(siteGrove);
        Check(sitePlants.Length > 0, "vegetation grows in launch-site cell");
        double closest = double.MaxValue;
        foreach (Transform3D transform in sitePlants) {

            Vector3d direction = (Anchor(siteGrove) + Frames.Sim(transform.Origin)).Normalized;
            closest = Math.Min(closest, (direction - site.Up).Length * body.Radius);

        }
        Check(closest >= 20.0 && closest < (broad ? 300.0 : 100.0), "launch plants clear mount without a wide barren zone");
        Vector3d physics = body.Radius * Vector3d.UnitX;
        type.GetField("_physicsFocus", Private).SetValue(scatter, (Vector3d?)physics);
        type.GetMethod("Select", Private).Invoke(scatter, new object[] { -physics * 2, false });
        Check(scatter.Pending > 0 && scatter.Pending <= 9, "physical neighbourhood retained with distant camera");
        type.GetField("_physicsFocus", Private).SetValue(scatter, null);
        type.GetMethod("Select", Private).Invoke(scatter, new object[] { -physics * 2, false });
        Check(scatter.Pending == 0, "physical neighbourhood released when vessel leaves");
        scatter.Free();

    }

}
