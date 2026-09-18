using FullThrust.Sim;
namespace FullThrust.Sim.Tests;
public static partial class Program {
    private static void ScatterSweeps() {
        Section("scatter continuous contacts");
        Vector3d a = Vector3d.Zero, b = Vector3d.UnitZ * 10;
        bool hit = ScatterCollision.Sweep(new(-100, 0, 5), new(100, 0, 5), a, b, 1, out double t, out Vector3d n);
        Near("fast trunk crossing hits", hit ? 1 : 0, 1, 0);
        Near("first cylinder contact", t, 0.495, 1e-10);
        Close("cylinder normal", n, -Vector3d.UnitX, 1e-10);
        hit = ScatterCollision.Sweep(new(0, 0, 20), new(0, 0, 0), a, b, 1, out t, out n);
        Near("parallel cap crossing", hit ? t : -1, 0.45, 1e-10);
        Close("cap normal", n, Vector3d.UnitZ, 1e-10);
        Near("above canopy misses", ScatterCollision.Sweep(new(-10, 0, 12), new(10, 0, 12), a, b, 1, out _, out _) ? 1 : 0, 0, 0);
        Near("stationary outside misses", ScatterCollision.Sweep(new(5, 0, 5), new(5, 0, 5), a, b, 1, out _, out _) ? 1 : 0, 0, 0);
        hit = ScatterCollision.Sweep(new(0, 0, 5), new(0, 0, 5), a, b, 1, out t, out n);
        Near("initial overlap", hit ? t : -1, 0, 0);
        Near("overlap normal finite", n.Length, 1, 1e-10);
        hit = ScatterCollision.Sweep(new(-10, 0, 0), new(10, 0, 0), a, a, 2, out t, out n);
        Near("degenerate capsule is sphere", hit ? t : -1, 0.4, 1e-10);
        Vector3d shift = new(1e6, -2e6, 3e6);
        hit = ScatterCollision.Sweep(shift + new Vector3d(-100, 0, 5), shift + new Vector3d(100, 0, 5), shift + a, shift + b, 1, out t, out n);
        Near("planet-scale translation invariant", hit ? t : -1, 0.495, 1e-10);
        Near("moving away misses", ScatterCollision.Sweep(new(3, 0, 5), new(10, 0, 5), a, b, 1, out _, out _) ? 1 : 0, 0, 0);
    }
}
