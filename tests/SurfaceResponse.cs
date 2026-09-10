using FullThrust.Sim;

namespace FullThrust.Sim.Tests;

public static partial class Program {

    private static void SurfaceFlowChecks() {

        Section("exhaust surface response");
        SurfaceResponse water = new();
        for (int i = 0; i < 120; i++) { water.Step(); }
        Near("unforced water stays at rest", water.PeakHeight, 0.0, 0.0);
        water.Press(0.0, 0.0, 4.0, 5000.0);
        water.Step();
        Expect("pressure drives outward water momentum", water.VelocityX(34, 32) > 0.0, $"velocity {water.VelocityX(34, 32)}");
        for (int i = 1; i < 120; i++) { water.Step(); }
        Expect("pressure depresses the surface", water.Height(32, 32) < -0.1, $"height {water.Height(32, 32)}");
        Near("central pressure remains symmetric", water.Height(28, 32), water.Height(36, 32), 1e-9);
        double before = water.Height(32, 32);
        water.ClearForcing();
        water.Step();
        Expect("shutdown does not erase the wave", Math.Abs(water.Height(32, 32)) > 0.05, "wave disappeared");
        for (int i = 0; i < 180; i++) { water.Step(); }
        Expect("water rebounds after shutdown", water.Height(32, 32) > before, "no rebound");
        Expect("waves propagate away from the footprint", Math.Abs(water.Height(42, 32)) > 0.001, "no outgoing wave");
        for (int i = 0; i < 3600; i++) { water.Step(); }
        Expect("unforced waves dissipate", water.PeakHeight < 0.01, $"peak {water.PeakHeight}");
        water.Clear();
        for (int z = 0; z < SurfaceResponse.Size; z++) {

            for (int x = 34; x < SurfaceResponse.Size; x++) { water.SetWet(x, z, false); }

        }
        water.Press(0.0, 0.0, 3.0, 10000.0);
        for (int i = 0; i < 120; i++) { water.Step(); }
        Near("shoreline blocks flux into dry cells", water.Height(35, 32), 0.0, 0.0);
        SurfaceResponse first = new();
        SurfaceResponse second = new();
        SurfaceResponse together = new();
        first.Press(-8.0, 0.0, 3.0, 1000.0);
        second.Press(8.0, 0.0, 3.0, 1000.0);
        together.Press(-8.0, 0.0, 3.0, 1000.0);
        together.Press(8.0, 0.0, 3.0, 1000.0);
        for (int i = 0; i < 60; i++) {

            first.Step();
            second.Step();
            together.Step();

        }
        Near("simultaneous strikes combine without replacing each other", together.Height(32, 32),
            first.Height(32, 32) + second.Height(32, 32), 1e-10);
        double volume = 0.0;
        for (int z = 1; z < SurfaceResponse.Size - 1; z++) {

            for (int x = 1; x < SurfaceResponse.Size - 1; x++) { volume += together.Height(x, z); }

        }
        Near("interior pressure redistributes water volume", volume, 0.0, 1e-5);
        water.Clear();
        water.Press(double.NaN, 0.0, 1.0, 1000.0);
        water.Step();
        Near("invalid pressure cannot poison the grid", water.PeakHeight, 0.0, 0.0);

    }

}
