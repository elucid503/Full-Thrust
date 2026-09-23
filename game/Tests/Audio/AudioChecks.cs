using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using FullThrust.Sim;
using static FullThrust.Game.Checks;

using Godot;

namespace FullThrust.Game;

public sealed partial class AudioChecks : Node {

    private readonly Dictionary<string, int> _heard = new();

    // Counts every one-shot the world bus is handed, by file name.
    private void Counted(Node node) {

        if (node is AudioStreamPlayer3D player && player is not SoundLoop && player.Bus == Soundscape.World && player.Stream != null) {

            string name = player.Stream.ResourcePath.GetFile().GetBaseName();
            _heard[name] = Heard(name) + 1;

        }

    }

    private int Heard(string prefix) {

        int total = 0;
        foreach (KeyValuePair<string, int> entry in _heard) { if (entry.Key.StartsWith(prefix)) { total += entry.Value; } }
        return total;

    }

    private async Task Seconds(double seconds) {

        double end = Soundscape.Active.Clock + seconds;
        while (Soundscape.Active.Clock < end) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    }

    private async Task Settle() {

        ulong started = Time.GetTicksMsec();
        do {

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Time.GetTicksMsec() - started > 90000) { throw new InvalidOperationException("Transition timed out"); }

        } while (SceneTransition.Loading);

    }

    private static SoundLoop Loop(string name) => (SoundLoop)VesselView.Active.FindChild(name, true, false);

    private static bool Sounding(SoundLoop loop) => loop.Playing && loop.VolumeDb > -40.0f;

    public override async void _Ready() {

        try {

            GetTree().NodeAdded += Counted;
            Main main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            GetTree().Root.Mode = Window.ModeEnum.Windowed;
            GetTree().Root.Size = new Vector2I(1280, 720);
            Flight flight = Flight.Active;
            flight.DebugPaused = true;
            await Settle();

            Check(AudioServer.GetBusIndex(Soundscape.World) > 0 && AudioServer.GetBusIndex(Soundscape.Hull) > 0, "bus layout loads");

            // One engine at a throttle that cannot lift the stack, so the clamps keep hold.
            Vessel vessel = flight.Vessel;
            double each = vessel.Active.ThrustNewtons / vessel.EngineCount;
            for (int i = 1; i < vessel.EngineCount; i++) { vessel.SetEngine(i, false); }
            flight.DebugPaused = false;
            vessel.Throttle = Math.Min(1.0, 0.8 * vessel.Mass * flight.Body.SurfaceGravity / each);
            await Seconds(1.5);

            Check(Sounding(Loop("Near")), "engine roar on the pad");
            Check(Sounding(Loop("Inside")), "chase camera hears the hull");
            Check(Sounding(Loop("Pad")), "exhaust roars off the pad");
            Check(flight.Clamped && Heard("clamp_release") == 0, "clamps hold below lift-off thrust");

            // At 400 m the roar keeps arriving for 400 / 340 s after the engine has stopped.
            OrbitCamera.Active.Distance = 400.0f;
            await Seconds(1.0);
            Check(Sounding(Loop("Near")), "roar audible at 400 m");
            vessel.Throttle = 0.0;
            double cut = Soundscape.Active.Clock;
            while (Sounding(Loop("Near")) && Soundscape.Active.Clock - cut < 5.0) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            double lag = Soundscape.Active.Clock - cut;
            GD.Print($"AUDIO LAG {lag:F2}s");
            Check(lag > 0.9 && lag < 1.8, "roar outlives the cutoff by the flight time of sound");
            Check(Heard("shutdown") == 1, "commanded cutoff heard as shutdown");

            OrbitCamera.Active.Distance = 95.0f;
            for (int i = 1; i < vessel.EngineCount; i++) { vessel.SetEngine(i, true); }
            vessel.Throttle = 1.0;
            await Seconds(3.0);
            Check(!flight.Clamped && Heard("clamp_release") == 1, "clamp release heard once");

            // Half the thrust either way should land 7.8 dB down: a half to the loudness exponent.
            float full = Loop("Near").VolumeDb;
            for (int i = vessel.EngineCount / 2; i < vessel.EngineCount; i++) { vessel.SetEngine(i, false); }
            await Seconds(0.5);
            float halfCluster = Loop("Near").VolumeDb - full;
            for (int i = vessel.EngineCount / 2; i < vessel.EngineCount; i++) { vessel.SetEngine(i, true); }
            await Seconds(0.5);
            full = Loop("Near").VolumeDb;
            vessel.Throttle = 0.5;
            await Seconds(0.5);
            float halfThrottle = Loop("Near").VolumeDb - full;
            vessel.Throttle = 1.0;
            GD.Print($"AUDIO HALF CLUSTER {halfCluster:F1} dB, HALF THROTTLE {halfThrottle:F1} dB");
            Check(halfCluster < -6.0f && halfCluster > -10.0f, "half the engines sound about half as loud");
            Check(halfThrottle < -6.0f && halfThrottle > -10.0f, "half throttle sounds about half as loud");

            flight.Place(80_000.0, 0.0);
            await Seconds(1.5);
            Check(!Loop("Near").Playing && !Loop("Far").Playing, "vacuum carries no roar");
            Check(Sounding(Loop("Inside")), "vacuum keeps the hull rumble");

            // A fast, low, unpowered dive: the air should be heard from outside and felt through the hull.
            vessel.Throttle = 0.0;
            flight.Place(3000.0, 250.0);
            await Seconds(1.0);
            Check(Sounding(Loop("Airflow")) && Sounding(Loop("Gust")) && Sounding(Loop("Buffet")), "fast descent roars");
            Check(Sounding(Loop("Rattle")), "fast descent rattles the hull");

            flight.Invulnerable = true;
            flight.Place(flight.Site.Height + 120.0, 0.0);
            double drop = Soundscape.Active.Clock;
            while (Heard("impact") == 0 && Soundscape.Active.Clock - drop < 12.0) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            Check(Heard("impact_heavy") > 0, "hard landing heard as a heavy impact");

            int knocks = Heard("impact");
            flight.Invulnerable = false;
            flight.Place(flight.Site.Height + 150.0, 0.0);
            drop = Soundscape.Active.Clock;
            while (Heard("explosion") == 0 && Soundscape.Active.Clock - drop < 12.0) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            await Seconds(0.5);
            Check(Heard("explosion") == 1 && Heard("impact") == knocks, "crash heard as the explosion alone");

            GD.Print("Audio checks complete");
            GetTree().Quit();

        } catch (Exception exception) {

            Fail(this, exception);

        }

    }

}
