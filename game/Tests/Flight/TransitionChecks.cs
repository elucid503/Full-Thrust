using System;
using System.Reflection;
using System.Threading.Tasks;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class TransitionChecks : Node {

    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool _restarted;
    private static ulong _restartAt;
    private static Terrain _survey;
    private static ulong _shorelineId;

    private static void Check(bool condition, string label) {

        if (!condition) { throw new InvalidOperationException(label); }
        GD.Print("PASS " + label);

    }

    private async Task Settle() {

        ulong started = Time.GetTicksMsec();
        do {

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Time.GetTicksMsec() - started > 60000) {

                throw new InvalidOperationException($"Transition timed out: terrain={Planet.Active.PendingJobs}, forest={Planet.Active.ForestPending}, scatter={Planet.Active.ScatterPending}, surface={Planet.Active.SurfaceReady}, clouds={Planet.Active.CloudTexturesReady}");

            }

        } while (SceneTransition.Loading);

        Check(Planet.Active.SurfaceReady, "cover waits for complete terrain");
        Check(Planet.Active.WorkerFailures == 0, "terrain workers remain healthy");

    }

    public override async void _Ready() {

        try {

            ulong started = Time.GetTicksMsec();
            Main main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            GD.Print($"SCENE BUILD {Time.GetTicksMsec() - started} ms");
            Flight.Active.DebugPaused = true;
            await Settle();
            Check(Flight.Active.Time == 0.0, "loading holds the simulation at liftoff");
            ShaderMaterial[] faces = (ShaderMaterial[])typeof(Planet).GetField("_faces", Fields).GetValue(Planet.Active);
            ulong shorelineId = faces[0].GetShaderParameter("shoreline_map").AsGodotObject().GetInstanceId();
            if (_restarted) {

                Check(_survey.SharesSurvey(Flight.Active.Body.Terrain), "restart shares the survey");
                Check(Flight.Active.Body.Terrain.Plateaus.Count == 1, "restart commissions exactly one launch plateau");
                Check(_shorelineId == shorelineId, "restart reuses the GPU shoreline texture");
                Check(Flight.Active.Vessel.Stages.Count == 3 && Flight.Active.Clamped, "restart restores the full clamped stack");
                GD.Print($"RESTART READY {Time.GetTicksMsec() - _restartAt} ms");
                GetTree().Quit();
                return;

            }

            GD.Print($"INITIAL READY {Time.GetTicksMsec() - started} ms");
            Ground flightGround = main.GetNode<Ground>("Planet/Surface");
            int patchCount = flightGround.GetChildCount();
            for (int i = 0; i < 2; i++) {

                started = Time.GetTicksMsec();
                MapView.Active.Toggle();
                Check(SceneTransition.Loading, "map switch presents its cover immediately");
                await Settle();
                Check(MapView.Active.Open && !flightGround.Visible, "map draws its own terrain");
                Check(flightGround.GetChildCount() == patchCount, "map retains all flight patches");
                GD.Print($"MAP READY {Time.GetTicksMsec() - started} ms");
                started = Time.GetTicksMsec();
                MapView.Active.Toggle();
                await Settle();
                Check(!MapView.Active.Open && flightGround.Visible, "flight terrain returns intact");
                GD.Print($"FLIGHT READY {Time.GetTicksMsec() - started} ms");

            }

            FreeCamera.Active.Take(OrbitCamera.Active);
            Vector3d cameraPosition = FreeCamera.Active.Where;
            Vector3d east = Vector3d.Cross(Vector3d.UnitZ, cameraPosition.Normalized).Normalized;
            cameraPosition = (cameraPosition + east * 15000.0).Normalized * cameraPosition.Length;
            typeof(FreeCamera).GetField("_where", Fields).SetValue(FreeCamera.Active, cameraPosition);
            MapView.Active.Toggle();
            await Settle();
            MapView.Active.Toggle();
            await Settle();
            Check(FreeCamera.Flying, "map returns to the previous free camera");
            Check(Frames.Point(FreeCamera.Active.Where).Length() < Frames.RebaseDistance,
                "paused camera return rebases before rendering");
            _survey = Flight.Active.Body.Terrain;
            _shorelineId = shorelineId;
            _restarted = true;
            _restartAt = Time.GetTicksMsec();
            Flight.Active.Restart();
            Check(SceneTransition.Loading, "restart presents a cover before reloading");

        } catch (Exception exception) {

            GD.PushError(exception.ToString());
            GetTree().Quit(1);

        }

    }

}
