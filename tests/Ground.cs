using System.IO;
using System.Threading;
using System.Threading.Tasks;

using FullThrust.Sim;

namespace FullThrust.Sim.Tests;

public static partial class Program {

    private const string HeightfieldPath = "game/Assets/Planet/elevation.r16";

    // The suite runs out of its build directory, and the survey is repository data rather than a
    // build output, so the tree is walked back until it turns up.
    private static string Repository(string relative) {

        for (DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent) {

            string candidate = Path.Combine(directory.FullName, relative);

            if (File.Exists(candidate)) {

                return candidate;

            }

        }

        return null;

    }

    private static Vector3d Site(double latitudeDegrees, double longitudeDegrees) {

        double latitude = latitudeDegrees * Math.PI / 180.0;
        double longitude = longitudeDegrees * Math.PI / 180.0;

        double cosine = Math.Cos(latitude);

        return new Vector3d(cosine * Math.Cos(longitude), cosine * Math.Sin(longitude), Math.Sin(latitude));

    }

    private static void GroundSurvey() {

        Section("ground survey");

        string path = Repository(HeightfieldPath);

        if (path == null) {

            Expect("heightfield present", false, $"no elevation grid at {HeightfieldPath}");

            return;

        }

        Terrain terrain;

        using (FileStream stream = File.OpenRead(path)) {

            terrain = Terrain.Load(stream, Home.Radius);

        }

        Expect("himalaya stands up", terrain.Elevation(Site(28.0, 86.9)) > 800.0, $"{terrain.Elevation(Site(28.0, 86.9)):F0} m");
        Expect("mid atlantic is deep", terrain.Elevation(Site(30.0, -40.0)) < -400.0, $"{terrain.Elevation(Site(30.0, -40.0)):F0} m");
        Expect("amazon basin is low land", terrain.Elevation(Site(-3.0, -60.0)) is > 0.0 and < 120.0, $"{terrain.Elevation(Site(-3.0, -60.0)):F0} m");
        Expect("antarctic plateau is high", terrain.Elevation(Site(-80.0, 90.0)) > 400.0, $"{terrain.Elevation(Site(-80.0, 90.0)):F0} m");

        // The 60 arc-second survey has the Cape at sea level; the imagery resolves it and the grid
        // is pulled onto that, which is the whole reason the correction exists.
        Expect("the cape is dry land", terrain.Elevation(Site(28.52, -80.62)) > 0.0, $"{terrain.Elevation(Site(28.52, -80.62)):F0} m");

        Expect("the sea floor never breaches", terrain.SurfaceRadius(Site(30.0, -40.0)) == Home.Radius,
            $"{terrain.SurfaceRadius(Site(30.0, -40.0)) - Home.Radius:F1} m over datum");

        // Detail is deterministic: the renderer's worker threads must land where the physics does.
        Expect("detail repeats exactly", terrain.Elevation(Site(12.3, 45.6)) == terrain.Elevation(Site(12.3, 45.6)), "two reads differ");

        double rolled = terrain.Elevation(Site(-3.0, -60.0)) - terrain.Elevation(Site(-3.0, -60.02));

        Expect("flat ground still rolls", Math.Abs(rolled) is > 0.01 and < 24.0, $"{rolled:F2} m over 440 m");

        GroundConcurrency(terrain);
        GroundContact(terrain);

    }

    private static void GroundConcurrency(Terrain terrain) {

        Section("terrain workers");

        double[] expected = new double[256];

        for (int index = 0; index < expected.Length; index++) {

            expected[index] = terrain.Elevation(Site(-85.0 + index * 0.66, -179.0 + index * 1.4));

        }

        int mismatches = 0;

        Parallel.For(0, 4096, index => {

            int sample = index % expected.Length;
            double actual = terrain.Elevation(Site(-85.0 + sample * 0.66, -179.0 + sample * 1.4));

            if (actual != expected[sample]) {

                Interlocked.Increment(ref mismatches);

            }

        });

        Expect("parallel samples match physics exactly", mismatches == 0, $"{mismatches} mismatches");

        var snapshot = terrain.Plateaus;

        Parallel.For(0, 32, index => terrain.Add(new Terrain.Plateau {

            Centre = Site(0.0, index),
            Height = 1.0,
            InnerRadius = 0.0,
            OuterRadius = 0.0,

        }));

        Expect("published plateau snapshots remain immutable", snapshot.Count == 0, $"snapshot grew to {snapshot.Count}");
        Expect("concurrent commissioning loses no plateaus", terrain.Plateaus.Count == 32, $"{terrain.Plateaus.Count} published");

    }

    private static void GroundContact(Terrain terrain) {

        Section("launch site");

        CelestialBody body = BodyCatalog.Home;

        body.Terrain = terrain;

        LaunchSite site = LaunchSite.Home;

        site.Commission(body);

        Expect("pad stands over the sea", site.Height > 0.0, $"{site.Height:F1} m");

        Near("pad frame is orthonormal", Vector3d.Dot(site.Up, site.East), 0.0, 1e-12);
        Near("north completes the frame", Vector3d.Dot(site.North, site.East), 0.0, 1e-12);
        Near("north points at the pole", Vector3d.Dot(site.North, Vector3d.UnitZ), Math.Cos(site.Latitude), 1e-9);

        // What the pad is for: the ground under the complex reads one height, so the mesh the
        // renderer builds and the contact the physics tests are the same surface.
        double centre = terrain.Elevation(site.Up);
        double offset = terrain.Elevation((site.Up * body.Radius + site.East * 300.0).Normalized);

        Near("the pad is level", offset, centre, 0.01);
        Near("the pad deck is where the site says", centre, site.Height, 1e-9);

        double away = terrain.Elevation((site.Up * body.Radius + site.East * 12_000.0).Normalized);

        Expect("the survey returns off the pad", Math.Abs(away - site.Height) > 0.5, $"{away:F1} m against {site.Height:F1} m");

        Vector3d pad = site.PositionAt(body, 0.0);

        Near("the pad sits on the ground", body.HeightAboveGround(pad, 0.0), 0.0, 1e-6);

        // A quarter day on and the pad has turned with the planet, but it is still on the ground.
        Vector3d turned = site.PositionAt(body, 21600.0);

        Near("the pad turns with the planet", body.HeightAboveGround(turned, 21600.0), 0.0, 1e-6);

        Expect("the pad has moved", (turned - pad).Length > 1_000_000.0, $"{(turned - pad).Length:F0} m");

        Close("body-fixed round trips", body.ToBodyFixed(body.ToInertial(site.Up, 5000.0), 5000.0), site.Up, 1e-12);

        QuaternionD attitude = site.AttitudeAt(body, 0.0);

        Close("a vehicle on the pad points up", attitude.Rotate(Vector3d.UnitZ), site.UpAt(body, 0.0), 1e-9);

        body.Terrain = null;

    }

}
