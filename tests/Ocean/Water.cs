using System.IO;

using FullThrust.Sim;

namespace FullThrust.Sim.Tests;

public static partial class Program {

    private static void WaterAndWind() {

        Section("water and shared weather");
        CelestialBody body = new() { Radius = Home.Radius, Mu = Home.Mu,
            RotationPeriodSeconds = Home.RotationPeriodSeconds, Atmosphere = Home.Atmosphere, Weather = new Weather() };
        using (FileStream stream = File.OpenRead(Repository(HeightfieldPath))) { body.Terrain = Terrain.Load(stream, body.Radius); }
        Vector3d fixedUp = Site(30.0, -40.0);
        Vector3d point = fixedUp * body.Radius;
        double bed = body.Terrain.Elevation(point);
        Expect("ocean test is offshore", bed < -100.0, $"{bed} m");
        double distance = body.Weather.WindDistanceAt(100.0);
        double height = Ocean.Sample(body, point, bed, 100.0).Height;
        body.Weather.SetPreset(WeatherPreset.Gale, 100.0);
        Near("cloud caches retain earlier wind history", body.Weather.WindDistanceAt(90.0), 900.0, 1e-12);
        Near("wind edits preserve cloud phase", body.Weather.WindDistanceAt(100.0), distance, 1e-12);
        Near("wind edits preserve wave height", Ocean.Sample(body, point, bed, 100.0).Height, height, 1e-12);
        Expect("sea responds more slowly than wind", body.Weather.SeaSpeedAt(105.0) < body.Weather.WindSpeedAt(105.0), "sea jumped");
        Vector3d wind = body.Weather.VelocityAt(body, body.ToInertial(Weather.Origin * body.Radius, 300.0), 300.0);
        Near("gale reaches requested surface speed", wind.Length, 32.0, 0.01);
        Near("wind is tangent to planet", Vector3d.Dot(wind, body.ToInertial(Weather.Origin, 300.0)), 0.0, 1e-9);
        Near("wind vanishes outside atmosphere", body.Weather.VelocityAt(body, fixedUp * (body.Radius + body.AtmosphereTop + 1.0), 300.0).Length, 0.0, 1e-12);
        Vessel airProbe = new("wind probe", new[] { Aegis.BuildStage(0.0) });
        airProbe.Position = body.ToInertial(Weather.Origin * (body.Radius + 100.0), 300.0);
        airProbe.Velocity = body.SurfaceVelocityAt(airProbe.Position);
        Expect("wind loads a surface-stationary vessel", Aerodynamics.Compute(airProbe, body, 300.0).Force.Length > 100.0, "missing aerodynamic wind");

        for (int index = 0; index < 40; index++) {

            double time = index * 3.7;
            Ocean.Surface shallow = Ocean.Sample(body, point, -0.2, time);
            Expect("shallow waves stay above seabed", shallow.Height > -0.2, $"{shallow.Height}");

        }
        foreach (double shoreTime in new[] { 0.0, 4.0, 1000.0, 4000.0 }) {

            Near("waves never flood dry land", Ocean.Sample(body, point, 0.15, shoreTime).Height, 0.0, 0.0);
            Near("shoreline stays at the datum", Ocean.Sample(body, point, 0.0, shoreTime).Height, 0.0, 0.0);

        }
        Near("paused wave sampling repeats", Ocean.Sample(body, point, bed, 123.0).Height,
            Ocean.Sample(body, point, bed, 123.0).Height, 0.0);
        double dt = 0.001;
        Ocean.Surface wave = Ocean.Sample(body, point, bed, 123.0);
        double numerical = (Ocean.Sample(body, point, bed, 123.0 + dt).Height - Ocean.Sample(body, point, bed, 123.0 - dt).Height) / (2.0 * dt);
        // Trochoid particles move sideways, so the surface kinematic condition replaces a plain rate check.
        Vector3d sideways = wave.Velocity - fixedUp * Vector3d.Dot(wave.Velocity, fixedUp);
        Near("water particles stay on the visible surface", Vector3d.Dot(wave.Velocity, fixedUp),
            numerical + Vector3d.Dot(sideways, wave.Gradient), 1e-5);
        Ocean.Particle particle = Ocean.Displace(body, fixedUp * body.Radius, bed, 123.0);
        Expect("trochoid swell moves particles sideways", particle.Shift.Length > 0.01, $"{particle.Shift.Length} m");
        Near("surface sampling inverts the trochoid", Ocean.Sample(body, fixedUp * body.Radius + particle.Shift, bed, 123.0).Height,
            particle.Height, 1e-6);
        Vector3d east = Vector3d.Cross(Vector3d.UnitZ, fixedUp).Normalized;
        foreach (Vector3d direction in new[] { east, Vector3d.Cross(fixedUp, east) }) {

            double slope = (Ocean.Sample(body, point + direction * 0.02, bed, 123.0).Height
                - Ocean.Sample(body, point - direction * 0.02, bed, 123.0).Height) / 0.04;
            Near("curved swell slope follows wave height", Vector3d.Dot(wave.Gradient, direction), slope, 2e-5);

        }

        body.Weather = new Weather();
        body.Weather.SetPreset(WeatherPreset.Calm, -1000.0);
        Vessel capsule = new("floating capsule", new[] { Aegis.BuildStage(0.0) });
        capsule.Orientation = QuaternionD.LookAlong(fixedUp, Vector3d.UnitZ);
        capsule.Position = fixedUp * (body.Radius + capsule.CentreOfMassZ + 0.35);
        capsule.Velocity = body.SurfaceVelocityAt(capsule.Position) - fixedUp * 3.0;
        bool touched = false;
        for (int step = 0; step < 3600 && capsule.Intact; step++) {

            double time = step / 120.0;
            Vector3d previous = capsule.Position;
            QuaternionD attitude = capsule.Orientation;
            Integrator.Step(capsule, body, 1.0 / 120.0, time);
            touched |= WaterPhysics.Apply(body, capsule, time + 1.0 / 120.0, 1.0 / 120.0);
            GroundCollision.Resolve(body, capsule, previous, attitude, time, time + 1.0 / 120.0);

        }
        Expect("soft splashdown remains playable", capsule.Intact && touched && capsule.InWater, $"{capsule.Fate}, volume {capsule.SubmergedVolume}");
        Expect("capsule floats near the surface", Math.Abs(body.AltitudeOf(capsule.Position)) < 2.0, $"altitude {body.AltitudeOf(capsule.Position)}");
        Near("buoyancy balances weight", capsule.SubmergedVolume * Ocean.Density / capsule.Mass, 1.0, 0.12);
        Expect("floating capsule stays stable", capsule.AngularVelocity.Length < 0.2, $"spin {capsule.AngularVelocity.Length}");

        capsule.Position = point;
        capsule.Velocity = body.SurfaceVelocityAt(point) - fixedUp * 35.0;
        WaterPhysics.Apply(body, capsule, 0.0, 1.0 / 120.0);
        Expect("hard splashdown still damages vessel", capsule.Fate == VesselFate.Impacted, capsule.Fate.ToString());
        capsule.Fate = VesselFate.Flying;
        WaterPhysics.Apply(body, capsule, 0.0, 1.0 / 120.0, false);
        Expect("invulnerable splashdown still gets water forces", capsule.Intact && capsule.InWater, capsule.Fate.ToString());

        Vessel stage = new("floating spent stage", new[] { Zenith.BuildStage() }) { IsDebris = true };
        stage.PropellantMass = 0.0;
        stage.RecomputeMassProperties();
        stage.Position = point;
        stage.Orientation = QuaternionD.LookAlong(Vector3d.Cross(Vector3d.UnitZ, fixedUp).Normalized, fixedUp);
        stage.Velocity = body.SurfaceVelocityAt(point);
        stage.AngularVelocity = new Vector3d(0.1, 0.1, 0.1);
        for (int step = 0; step < 1200 && stage.Intact; step++) {

            double time = step / 120.0;
            Integrator.Step(stage, body, 1.0 / 120.0, time);
            WaterPhysics.Apply(body, stage, time + 1.0 / 120.0, 1.0 / 120.0);

        }
        Expect("detached empty stage remains afloat", stage.Intact && stage.InWater && Math.Abs(body.AltitudeOf(stage.Position)) < 5.0,
            $"{stage.Fate}, height {body.AltitudeOf(stage.Position)}");
        Expect("water damps detached stage rotation", stage.AngularVelocity.Length < 0.15, $"spin {stage.AngularVelocity.Length}");

        Section("resting surface motion");
        CelestialBody ground = new() { Radius = Home.Radius, Mu = Home.Mu, RotationPeriodSeconds = Home.RotationPeriodSeconds };
        Vessel resting = new("resting capsule", new[] { Aegis.BuildStage(0.0) });
        resting.Orientation = QuaternionD.LookAlong(fixedUp, Vector3d.UnitZ);
        resting.Position = fixedUp * (ground.Radius + resting.CentreOfMassZ + 0.021);
        resting.Velocity = ground.SurfaceVelocityAt(resting.Position);
        resting.AngularVelocity = resting.Orientation.Conjugate.Rotate(Vector3d.UnitZ * ground.SpinRate);
        Vector3d start = resting.Position;
        for (int step = 0; step < 2400; step++) {

            double time = step / 120.0;
            Vector3d previous = resting.Position;
            QuaternionD attitude = resting.Orientation;
            Integrator.Step(resting, ground, 1.0 / 120.0, time);
            GroundCollision.Resolve(ground, resting, previous, attitude, time, time + 1.0 / 120.0);

        }
        Expect("resting vessel follows rotating ground without drift", (ground.ToBodyFixed(resting.Position, 20.0) - start).Length < 0.3,
            $"drift {(ground.ToBodyFixed(resting.Position, 20.0) - start).Length} m");
        Expect("resting vessel does not acquire random spin", resting.AngularVelocity.Length < 0.01, $"spin {resting.AngularVelocity.Length}");
        Vector3d slide = Vector3d.Cross(Vector3d.UnitZ, resting.Position.Normalized).Normalized;
        resting.Velocity += slide * 2.0;
        for (int step = 0; step < 1200; step++) {

            double time = 20.0 + step / 120.0;
            Vector3d previous = resting.Position;
            QuaternionD attitude = resting.Orientation;
            Integrator.Step(resting, ground, 1.0 / 120.0, time);
            GroundCollision.Resolve(ground, resting, previous, attitude, time, time + 1.0 / 120.0);

        }
        Expect("ground friction arrests surface sliding", (resting.Velocity - ground.SurfaceVelocityAt(resting.Position)).Length < 0.2,
            $"slip {(resting.Velocity - ground.SurfaceVelocityAt(resting.Position)).Length}");
        string shader = File.ReadAllText(Repository("game/Shaders/Ground/Ground.gdshader"));
        int vertex = shader.IndexOf("void vertex()", StringComparison.Ordinal);
        int open = shader.IndexOf('{', vertex);
        int depth = 0;
        int close = -1;
        for (int i = open; i < shader.Length; i++) {

            if (shader[i] == '{') { depth++; }
            else if (shader[i] == '}') { depth--; if (depth == 0) { close = i; break; } }

        }
        Expect("terrain vertices do not fetch the shoreline survey",
            close > open && !shader.AsSpan(open, close - open).Contains("shoreline_", StringComparison.Ordinal),
            "Ground.gdshader vertex samples shoreline_map");

    }

}
