using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>Terra: the quadtree surface, the optical weather deck over it, and the scattering shell
/// over both. Reads the body; holds no sim state.</summary>
public sealed partial class Planet : Node3D {

    // Coastal cumulus shares one physical deck between view rays and ground shadows.
    private const float CloudBase = 900.0f;
    private const float CloudTop = 3800.0f;

    public static Planet Active { get; private set; }

    private CelestialBody _body;

    private Ground _ground;
    private Ground _mapGround;
    private bool _mapOpen;
    private static Terrain _shorelineSurvey;
    private static Texture2D _sharedShoreline;
    private Forest _forest;
    private Forest _canopy;
    private GroundScatter _scatter;
    private GroundScatter _broadScatter;
    private CloudShadows _cloudShadows;

    private ShaderMaterial[] _faces;
    private ShaderMaterial _clouds;
    public CloudRender CloudPass { get; } = new();
    private Texture3D _cloudShape;
    private Texture3D _cloudDetail;
    private static Texture3D _sharedCloudShape;
    private static Texture3D _sharedCloudDetail;
    private static bool _shapeReady;
    private static bool _detailReady;
    public bool CloudTexturesReady => _shapeReady && _detailReady;
    private ShaderMaterial _atmosphere;
    private readonly Dictionary<string, double> _opticalParameters = new();

    private static readonly HashSet<string> SharedOptics = new() { "rayleigh_coefficients", "mie_coefficient", "ozone_coefficients", "sun_radiance" };

    // Everything that works out its own sunlight per pixel, and so needs the air's optics.
    private readonly List<ShaderMaterial> _sunlit = new();

    public Sunlight Sunlight { get; private set; }

    // Share of the sun the weather deck lets through to the lit subject.
    public float CloudTransmission => _cloudShadows.Transmission;

    // The shells sit on the planet's centre; the quadtree places every patch on its own absolute
    // transform, so this node stays at the origin and nothing under it is offset twice.
    private MeshInstance3D _deck;
    private MeshInstance3D _air;

    public override void _ExitTree() {

        CloudPass.Release();
        if (Active == this) { Active = null; }

    }

    public int WorkerFailures => (_ground?.WorkerFailures ?? 0) + (_mapGround?.WorkerFailures ?? 0);
    public int PendingJobs => (_ground?.PendingJobs ?? 0) + (_mapGround?.PendingJobs ?? 0);
    public bool SurfaceReady => (_mapOpen ? _mapGround : _ground)?.SurfaceReady == true
        && (_mapOpen || (ForestPending == 0 && ScatterPending == 0));

    public int PatchCount => _ground?.PatchCount ?? 0;
    public int DeepestLevel => _ground?.DeepestLevel ?? 0;
    public double GroundMilliseconds => _ground?.SyncMilliseconds ?? 0.0;
    public bool ResolveScatter(Vessel vessel, Vector3d previous, QuaternionD orientation, double start, double time, bool damage = false) {
        double reach = VesselCollision.Radius(vessel) + 14.0;
        if (Math.Min(_body.HeightAboveGround(previous, start), _body.HeightAboveGround(vessel.Position, time)) > reach) {
            return false;
        }
        Vector3d from = _body.ToBodyFixed(previous, start);
        Vector3d to = _body.ToBodyFixed(vessel.Position, time);
        Vector3d centre = (from + to) * 0.5;
        double sweptReach = reach + (to - from).Length * 0.5;
        _forest.EnsureContacts(centre, sweptReach);
        _scatter.EnsureContacts(centre, sweptReach);
        _broadScatter.EnsureContacts(centre, sweptReach);
        bool hit = _forest.Obstacles.Resolve(_body, vessel, previous, orientation, start, time, damage);
        hit |= _scatter.Obstacles.Resolve(_body, vessel, previous, orientation, start, time, damage);
        hit |= _broadScatter.Obstacles.Resolve(_body, vessel, previous, orientation, start, time, damage);
        return hit;
    }

    public int DestroyedScatter => _forest.Obstacles.DestroyedCount + _scatter.Obstacles.DestroyedCount + _broadScatter.Obstacles.DestroyedCount;
    public int ScatterEffects => _forest.Obstacles.EffectCount + _scatter.Obstacles.EffectCount + _broadScatter.Obstacles.EffectCount;

    public int TreeCount => _forest?.TreeCount ?? 0;
    public int ForestCells => _forest?.CellCount ?? 0;
    public int ForestPending => (_forest?.Pending ?? 0) + (_canopy?.Pending ?? 0);
    public int ScatterCount => (_scatter?.ScatterCount ?? 0) + (_broadScatter?.ScatterCount ?? 0);
    public int ScatterCells => (_scatter?.CellCount ?? 0) + (_broadScatter?.CellCount ?? 0);
    public int ScatterPending => (_scatter?.Pending ?? 0) + (_broadScatter?.Pending ?? 0);
    public int ScatterFailures => (_scatter?.Failures ?? 0) + (_broadScatter?.Failures ?? 0);
    public int ForestFailures => (_forest?.Failures ?? 0) + (_canopy?.Failures ?? 0);
    public int CanopyCount => _canopy?.TreeCount ?? 0;

    public void Build(CelestialBody body, Vector3 sunDirection) {

        Active = this;

        _body = body;

        float radius = (float)body.Radius;
        float atmosphereRadius = radius + (float)body.AtmosphereTop;

        Texture2D cloud = GD.Load<Texture2D>("res://Assets/Planet/clouds.jpg");

        if (_sharedCloudShape == null) {

            _sharedCloudShape = GD.Load<NoiseTexture3D>("res://Effects/Clouds/CloudShape.tres");
            _sharedCloudDetail = GD.Load<NoiseTexture3D>("res://Effects/Clouds/CloudDetail.tres");
            _sharedCloudShape.Changed += () => Callable.From(FilterCloudShape).CallDeferred();
            _sharedCloudDetail.Changed += () => Callable.From(FilterCloudDetail).CallDeferred();

        }
        _cloudShape = _sharedCloudShape;
        _cloudDetail = _sharedCloudDetail;

        BuildFaces(radius, cloud);
        BuildWater(radius, cloud);

        _ground = new Ground { Name = "Surface" };

        AddChild(_ground);

        _ground.Build(body, _faces, _water);
        _mapGround = new Ground { Name = "MapSurface", Visible = false };
        AddChild(_mapGround);
        _mapGround.Build(body, _faces, _water);
        _forest = new Forest { Name = "Forest" };
        AddChild(_forest);
        _forest.Build(body, GD.Load<Texture2D>("res://Assets/Planet/biomes.png"));
        _canopy = new Forest { Name = "DistantForest" };
        AddChild(_canopy);
        _canopy.Build(body, GD.Load<Texture2D>("res://Assets/Planet/biomes.png"), true);

        _scatter = new GroundScatter { Name = "GroundScatter" };
        AddChild(_scatter);
        _scatter.Build(body, GD.Load<Texture2D>("res://Assets/Planet/biomes.png"));
        _broadScatter = new GroundScatter { Name = "BroadScatter" };
        AddChild(_broadScatter);
        _broadScatter.Build(body, GD.Load<Texture2D>("res://Assets/Planet/biomes.png"), true);

        _cloudShadows = new CloudShadows { Name = "CloudShadows" };
        AddChild(_cloudShadows);
        _cloudShadows.Build(cloud, _cloudShape, _cloudDetail, radius, CloudBase, CloudTop, CoastalWeatherDirection());
        foreach (ShaderMaterial face in _faces) { _cloudShadows.AddReceiver(face); }
        _cloudShadows.AddReceiver(_water);
        _cloudShadows.AddReceiver(_forest.SurfaceMaterial);
        _cloudShadows.AddReceiver(_canopy.SurfaceMaterial);
        _cloudShadows.AddReceiver(_scatter.SurfaceMaterial);
        _cloudShadows.AddReceiver(_broadScatter.SurfaceMaterial);

        _atmosphere = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Atmosphere/Atmosphere.gdshader") };
        _cloudShadows.AddReceiver(_atmosphere);

        _atmosphere.SetParameter("cloud_floor", CloudBase);
        _atmosphere.SetParameter("cloud_ceiling", CloudTop);
        _opticalParameters["planet_radius"] = radius;
        _opticalParameters["atmosphere_radius"] = atmosphereRadius;
        _opticalParameters["rayleigh_height"] = body.Atmosphere.ScaleHeight;
        _opticalParameters["mie_height"] = 1200.0;
        _opticalParameters["ozone_height"] = 22000.0;
        _opticalParameters["ozone_half_width"] = 15000.0;

        // Clouds composite afterward and integrate foreground air to their optical centroid.
        _atmosphere.RenderPriority = 0;

        _clouds = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Clouds/Clouds.gdshader") };
        _cloudShadows.AddReceiver(_clouds);

        _clouds.SetParameter("cloud_map", cloud);
        _clouds.SetParameter("shape_noise", _cloudShape);
        _clouds.SetParameter("detail_noise", _cloudDetail);
        _clouds.SetParameter("coastal_weather_direction", CoastalWeatherDirection());

        _clouds.SetParameter("base_radius", radius + CloudBase);
        _clouds.SetParameter("top_radius", radius + CloudTop);

        _clouds.RenderPriority = 2;

        _sunlit.AddRange(_faces);
        _sunlit.AddRange(new[] { _water, _forest.SurfaceMaterial, _canopy.SurfaceMaterial, _scatter.SurfaceMaterial,
            _broadScatter.SurfaceMaterial, _atmosphere, _clouds });

        foreach (ShaderMaterial material in _sunlit) {

            material.SetParameter("sun_direction", sunDirection);

        }

        RefreshAtmosphereLookup();

        _deck = SkyPass("Clouds", _clouds);

        AddChild(_deck);

        if (body.HasAtmosphere) {

            _air = SkyPass("Atmosphere", _atmosphere);

            AddChild(_air);

        }

    }

    private static Texture2D Shoreline(Terrain terrain) {

        if (terrain.SharesSurvey(_shorelineSurvey) && _sharedShoreline != null) {

            return _sharedShoreline;

        }

        int width = terrain.SurveyWidth;
        int height = terrain.SurveyHeight;
        byte[] pixels = new byte[width * height * 2];

        for (int row = 0; row < height; row++) {

            for (int column = 0; column < width; column++) {

                Half elevation = (Half)terrain.SurveyElevation(column, row);
                BitConverter.TryWriteBytes(pixels.AsSpan((row * width + column) * 2, 2), elevation);

            }

        }

        // One shared half-float survey keeps coastlines independent of mesh LOD.
        using Image image = Image.CreateFromData(width, height, false, Image.Format.Rh, pixels);
        _shorelineSurvey = terrain;
        _sharedShoreline = ImageTexture.CreateFromImage(image);
        return _sharedShoreline;

    }

    private void BuildFaces(float radius, Texture2D cloud) {

        Shader shader = GD.Load<Shader>("res://Shaders/Ground/Ground.gdshader");

        Texture2D rockColour = GD.Load<Texture2D>("res://Assets/Planet/Ground/Rock/rock_colour.jpg");
        Texture2D rockNormal = GD.Load<Texture2D>("res://Assets/Planet/Ground/Rock/rock_normal.jpg");
        Texture2D soilColour = GD.Load<Texture2D>("res://Assets/Planet/Ground/Soil/soil_colour.jpg");
        Texture2D soilNormal = GD.Load<Texture2D>("res://Assets/Planet/Ground/Soil/soil_normal.jpg");
        Texture2D biomes = GD.Load<Texture2D>("res://Assets/Planet/biomes.png");
        Texture2D shoreline = Shoreline(_body.Terrain);

        Texture2D[] closeMaps = new Texture2D[2];
        string[] closeNames = { "soil_detail", "rock_detail" };
        for (int index = 0; index < closeNames.Length; index++) {

            string folder = closeNames[index].StartsWith("soil", StringComparison.Ordinal) ? "Soil" : "Rock";
            closeMaps[index] = GD.Load<Texture2D>($"res://Assets/Planet/Ground/{folder}/{closeNames[index]}.png");

        }
        _faces = new ShaderMaterial[6];

        for (int face = 0; face < 6; face++) {

            ShaderMaterial material = new ShaderMaterial { Shader = shader };

            material.SetParameter("biome_map", biomes);
            material.SetParameter("shoreline_map", shoreline);
            material.SetParameter("night_map", GD.Load<Texture2D>($"res://Assets/Planet/Night/night_{face}.jpg"));
            material.SetParameter("cloud_map", cloud);
            material.SetParameter("shape_noise", _cloudShape);
            material.SetParameter("detail_noise", _cloudDetail);
            material.SetParameter("coastal_weather_direction", CoastalWeatherDirection());
            material.SetParameter("base_radius", radius + CloudBase);
            material.SetParameter("top_radius", radius + CloudTop);

            for (int index = 0; index < closeNames.Length; index++) {

                material.SetParameter(closeNames[index], closeMaps[index]);

            }
            material.SetParameter("rock_colour", rockColour);
            material.SetParameter("rock_normal", rockNormal);
            material.SetParameter("soil_colour", soilColour);
            material.SetParameter("soil_normal", soilNormal);

            _faces[face] = material;

        }

    }

    /// <summary>Live shader tuning from the debug bridge; scalars, or comma-separated vectors.</summary>
    public bool Tune(string target, string parameter, string value) {

        if (target == "atmosphere" && _opticalParameters.ContainsKey(parameter)) {

            if (!double.TryParse(value, out double number) || !double.IsFinite(number) || number <= 0.0) { return false; }
            double radius = parameter == "planet_radius" ? number : _opticalParameters["planet_radius"];
            double top = parameter == "atmosphere_radius" ? number : _opticalParameters["atmosphere_radius"];
            if (top <= radius) { return false; }
            _opticalParameters[parameter] = number;
            RefreshAtmosphereLookup();

        }

        string[] parts = value.Split(',');

        Variant setting = parts.Length == 3
            ? new Vector3(parts[0].ToFloat(), parts[1].ToFloat(), parts[2].ToFloat())
            : value.ToFloat();

        if (target == "surface" || target == "ground") {

            foreach (ShaderMaterial face in _faces) {

                face.SetParameter(parameter, setting);

            }

            return true;

        }

        // Every sunlit surface reads the same optics, or the ground and the sky disagree.
        if (target == "atmosphere" && SharedOptics.Contains(parameter)) {

            foreach (ShaderMaterial sunlit in _sunlit) {

                sunlit.SetParameter(parameter, setting);

            }

            return true;

        }

        ShaderMaterial material = target switch {

            "clouds" => _clouds,
            "atmosphere" => _atmosphere,
            "water" => _water,

            _ => null,

        };

        if (material == null) {

            return false;

        }

        material.SetParameter(parameter, setting);

        return true;

    }

    private static Vector3 CoastalWeatherDirection() {

        LaunchSite site = LaunchSite.Home;
        double cosine = Math.Cos(site.Latitude);
        return Frames.Direction(new Vector3d(cosine * Math.Cos(site.Longitude), cosine * Math.Sin(site.Longitude), Math.Sin(site.Latitude)));

    }

    private void RefreshAtmosphereLookup() {

        foreach ((string name, double value) in _opticalParameters) {

            _atmosphere.SetParameter(name, (float)value);

        }

        double radius = _opticalParameters["planet_radius"];
        double top = _opticalParameters["atmosphere_radius"];

        Sunlight = new Sunlight(radius, top, _opticalParameters["rayleigh_height"], _opticalParameters["mie_height"],
            _opticalParameters["ozone_height"], _opticalParameters["ozone_half_width"]);

        Texture2D table = AtmosphereLookup.Build(radius, top,
            _opticalParameters["rayleigh_height"], _opticalParameters["mie_height"],
            _opticalParameters["ozone_height"], _opticalParameters["ozone_half_width"]);

        foreach (ShaderMaterial material in _sunlit) {

            material.SetParameter("sun_optical_depth", table);
            material.SetParameter("planet_radius", (float)radius);
            material.SetParameter("atmosphere_radius", (float)top);
            material.SetParameter("rayleigh_height", (float)_opticalParameters["rayleigh_height"]);
            material.SetParameter("mie_height", (float)_opticalParameters["mie_height"]);
            material.SetParameter("ozone_half_width", (float)_opticalParameters["ozone_half_width"]);
            material.SetParameter("rayleigh_coefficients", Sunlight.RayleighCoefficients);
            material.SetParameter("mie_coefficient", Sunlight.MieCoefficient);
            material.SetParameter("ozone_coefficients", Sunlight.OzoneCoefficients);
            material.SetParameter("sun_radiance", Sunlight.Radiance);

        }

    }

    public void Sync(double time, Vector3d eye, Vector3d subject, Vector3d? flightEye = null) {

        if (_shapeReady && _cloudShape != _sharedCloudShape) {

            _cloudShape = _sharedCloudShape;
            Publish("shape_noise", _cloudShape);

        }

        if (_detailReady && _cloudDetail != _sharedCloudDetail) {

            _cloudDetail = _sharedCloudDetail;
            Publish("detail_noise", _cloudDetail);

        }

        Vector3 centre = Frames.Point(Vector3d.Zero);

        _deck.Position = centre;
        _clouds.SetParameter("fog_enabled", _body.HasAtmosphere ? 1.0f : 0.0f);
        _clouds.SetParameter("sun_shafts", 1.0f);

        if (_air != null) {

            _air.Position = centre;

        }

        SyncWeather(time);
        _mapOpen = flightEye.HasValue;
        _ground.Visible = !_mapOpen;
        _mapGround.Visible = _mapOpen;
        _ground.Sync(time, flightEye ?? eye);
        if (_mapOpen) { _mapGround.Sync(time, eye); }
        _cloudShadows.Sync(_body, time, eye, subject, Main.SunDirection);

        Basis cloudFrame = CloudWind.Frame(_body, time);
        Vector3d scatterEye = flightEye ?? eye;
        float altitude = (float)Math.Max(0.0, _body.HeightAboveGround(scatterEye, time));
        float materialAltitude = (float)Math.Max(0.0, eye.Length - _body.Radius);
        _forest.Sync(time, scatterEye, altitude);
        _canopy.Sync(time, scatterEye, altitude);
        _scatter.Sync(time, scatterEye, altitude);
        _broadScatter.Sync(time, scatterEye, altitude);
        double spin = _body.SpinAt(time);
        Vector2 rotation = new Vector2((float)Math.Cos(spin), (float)Math.Sin(spin));

        foreach (ShaderMaterial face in _faces) {

            face.SetParameter("planet_centre", centre);
            face.SetParameter("camera_altitude", materialAltitude);
            face.SetParameter("terrain_rotation", rotation);
            face.SetParameter("cloud_frame", cloudFrame);

        }

        SyncWater(time, centre, materialAltitude, rotation, cloudFrame);

        foreach (ShaderMaterial material in new[] { _forest.SurfaceMaterial, _canopy.SurfaceMaterial, _scatter.SurfaceMaterial, _broadScatter.SurfaceMaterial }) {

            material.SetParameter("planet_centre", centre);

        }

        _clouds.SetParameter("planet_centre", centre);
        _clouds.SetParameter("cloud_frame", cloudFrame);
        _clouds.SetParameter("eye_height", (float)(eye.Length - _body.Radius));
        _clouds.SetParameter("eye_up", Frames.Direction(eye.Normalized));

        CloudPass.Sync(_clouds, _deck.Visible);

        _atmosphere.SetParameter("planet_centre", centre);
        _atmosphere.SetParameter("sun_shafts", 1.0f);

    }

    private void Publish(string name, Texture3D volume) {

        _clouds.SetParameter(name, volume);
        _water.SetParameter(name, volume);
        foreach (ShaderMaterial face in _faces) {

            face.SetParameter(name, volume);

        }
        _cloudShadows.SetVolume(name, volume);

    }

    private static void FilterCloudShape() => Filter(ref _sharedCloudShape, ref _shapeReady);

    private static void FilterCloudDetail() => Filter(ref _sharedCloudDetail, ref _detailReady);

    // Every octave is fetched at a mip matched to its footprint; without one the fine detail is
    // sub-pixel noise from any height and re-rolls the cloud fringes with each frame's motion.
    private static void Filter(ref Texture3D volume, ref bool ready) {

        if (ready || volume == null || volume.HasMipmaps()) {

            ready = volume != null;
            return;

        }

        try {

            volume = FilteredVolume.Build(volume);
            ready = true;

        }
        catch (Exception exception) {

            GD.PushError($"Cloud mip volume failed: {exception.Message}");

        }

    }

    private static MeshInstance3D SkyPass(string name, Material material) {

        return new MeshInstance3D {

            Name = name,
            Mesh = new QuadMesh { Size = new Vector2(2.0f, 2.0f) },
            CustomAabb = new Aabb(-Vector3.One * 100_000_000.0f, Vector3.One * 200_000_000.0f),
            Layers = 2,

            MaterialOverride = material,

            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

        };

    }

}
