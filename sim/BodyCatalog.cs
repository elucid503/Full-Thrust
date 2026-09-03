using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace FullThrust.Sim;

public static class BodyCatalog {

    private const string ResourceName = "FullThrust.Sim.Data.bodies.json";

    private static readonly Dictionary<string, CelestialBody> Bodies = Load();

    public static CelestialBody Home => Bodies["Terra"];

    public static CelestialBody Get(string name) => Bodies[name];

    private static Dictionary<string, CelestialBody> Load() {

        Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);

        if (stream == null) {

            throw new InvalidOperationException($"embedded resource {ResourceName} is missing");

        }

        using (stream) {

            List<CelestialBody> loaded = JsonSerializer.Deserialize<List<CelestialBody>>(stream);

            Dictionary<string, CelestialBody> catalog = new Dictionary<string, CelestialBody>();

            foreach (CelestialBody body in loaded) {

                catalog[body.Name] = body;

            }

            return catalog;

        }

    }

}
