using System;
using System.Threading.Tasks;

using static FullThrust.Game.Checks;

using Godot;

namespace FullThrust.Game;

/// <summary>Checks the tower footprint, launch deck and exhaust shaft, then captures it in flight.</summary>
public sealed partial class LaunchSiteChecks : Node {

    private Main _main;
    private Camera3D _camera;

    public override void _Process(double delta) {

        _main?.GetNode<CanvasLayer>("Hud").Hide();
        _camera?.MakeCurrent();

    }

    public override async void _Ready() {

        try {

            Node3D asset = GD.Load<PackedScene>("res://Assets/LaunchSite/Gantry.glb").Instantiate<Node3D>();
            int triangles = 0;
            int surfaces = 0;
            int deckVertices = 0;
            Inspect(asset, Transform3D.Identity, ref triangles, ref surfaces, ref deckVertices);
            asset.Free();
            Check(triangles > 40000 && triangles < 65000, "Authored geometry budget");
            Check(surfaces == 6, "Six distinct tower materials");
            Check(deckVertices == 0, "Tower contains no mobile launcher deck");
            GD.Print($"Launch asset: {triangles} triangles, {surfaces} surfaces, clear exhaust shaft");

            _main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(_main);
            Flight.Active.DebugPaused = true;
            ulong started = Time.GetTicksMsec();
            while (SceneTransition.Loading) {

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Check(Time.GetTicksMsec() - started < 60000, "Flight scene becomes ready");

            }
            Node3D complex = _main.GetNode<Node3D>("Complex");
            int fittingTriangles = 0, fittingSurfaces = 0, fittingDeckVertices = 0;
            foreach (Node child in complex.GetNode("Mount").GetChildren()) {

                if (child is MeshInstance3D) {

                    Inspect(child, Transform3D.Identity, ref fittingTriangles, ref fittingSurfaces, ref fittingDeckVertices);

                }

            }
            Check(fittingDeckVertices > 20, "Launch stand meets the 4.5 m support datum");
            _camera = new Camera3D { Near = 0.1f, Far = 200000.0f, Fov = 60 };
            complex.AddChild(_camera);
            foreach (var view in new[] {

                (Name: "overview", Eye: new Vector3(30, 26, -42), Target: new Vector3(-4, 17, 2)),
                (Name: "platform", Eye: new Vector3(13, 8, -12), Target: new Vector3(0, 4, 1)),
                (Name: "gantry", Eye: new Vector3(-2, 18, -5), Target: new Vector3(-10, 17, 3)),

            }) {

                _camera.Transform = new Transform3D(Basis.LookingAt(view.Target - view.Eye, Vector3.Up), view.Eye);
                _camera.MakeCurrent();
                await DrawFrames();
                using Image shot = GetViewport().GetTexture().GetImage();
                Check(shot.SavePng($"res://.artifacts/launch-{view.Name}.png") == Error.Ok, "Screenshot saved");
                GD.Print($"CAPTURE launch-{view.Name}");

            }
            GetTree().Quit();

        } catch (Exception exception) {

            Fail(this, exception);

        }

    }

    private async Task DrawFrames() {

        for (int i = 0; i < 35; i++) {

            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        }

    }

    private static void Inspect(Node node, Transform3D parent, ref int triangles, ref int surfaces, ref int deckVertices) {

        Transform3D transform = node is Node3D spatial ? parent * spatial.Transform : parent;
        if (node is MeshInstance3D instance) {

            surfaces += instance.Mesh.GetSurfaceCount();
            Vector3[] faces = instance.Mesh.GetFaces();
            triangles += faces.Length / 3;
            for (int i = 0; i < faces.Length; i++) {

                faces[i] = transform * faces[i];
                Vector3 v = faces[i];
                Check(Math.Abs(v.X) <= LaunchComplex.ApronWidth / 2 + 0.01f && Math.Abs(v.Z) <= LaunchComplex.ApronDepth / 2 + 0.01f,
                    "Gantry stays on the cleared apron");
                if (Math.Abs(v.Y - 4.5f) < 0.01f) { deckVertices++; }

            }
            // Probe the middle and edges of the vehicle's exhaust envelope through every face.
            foreach (float x in new[] { -2.9f, 0, 2.9f }) {

                foreach (float z in new[] { -2.9f, 0, 2.9f }) {

                    Vector3 from = new(x, 4.4f, z);
                    Vector3 to = new(x, 0.3f, z);
                    for (int i = 0; i < faces.Length; i += 3) {

                        Variant hit = Geometry3D.SegmentIntersectsTriangle(from, to, faces[i], faces[i + 1], faces[i + 2]);
                        Check(hit.VariantType == Variant.Type.Nil, "Exhaust opening contains no platform faces");

                    }

                }

            }

        }
        foreach (Node child in node.GetChildren()) {

            Inspect(child, transform, ref triangles, ref surfaces, ref deckVertices);

        }

    }

    private static void Check(bool condition, string label) {

        if (!condition) { throw new InvalidOperationException(label); }

    }

}
