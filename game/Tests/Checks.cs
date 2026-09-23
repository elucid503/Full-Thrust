using System;

using Godot;

namespace FullThrust.Game;

// Every check scene runs in its own process, so one static tally per run is exact.
internal static class Checks {

    public static int Passed { get; private set; }

    public static void Check(bool condition, string label) {

        if (!condition) { throw new InvalidOperationException(label); }
        Passed++;
        GD.Print("PASS " + label);

    }

    public static void Fail(Node scene, Exception exception) {

        GD.PushError(exception.ToString());
        scene.GetTree().Quit(1);

    }

}
