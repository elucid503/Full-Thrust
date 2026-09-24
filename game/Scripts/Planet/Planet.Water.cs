using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class Planet {

    // Tiles divide the terrain lattice's 4096 m period, so the pattern crosses patch edges intact.
    private static readonly Vector2 DetailTiles = new Vector2(4.0f, 64.0f);
    private const float FoamTile = 8.0f;

    private ShaderMaterial _water;

    public Wakes Wakes { get; private set; }

    private void BuildWater(float radius, Texture2D cloud) {

        WaterTextures.Bake();

        // Drawn before the air, which then hazes it like any other surface.
        _water = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Ocean/Water.gdshader"), RenderPriority = -1 };

        _water.SetParameter("shoreline_map", Shoreline(_body.Terrain));
        _water.SetParameter("wave_detail", WaterTextures.Waves);
        _water.SetParameter("foam_detail", WaterTextures.Foam);
        _water.SetParameter("detail_tiles", DetailTiles);
        _water.SetParameter("foam_tile", FoamTile);
        _water.SetParameter("cloud_map", cloud);
        _water.SetParameter("shape_noise", _cloudShape);
        _water.SetParameter("detail_noise", _cloudDetail);
        _water.SetParameter("coastal_weather_direction", CoastalWeatherDirection());
        _water.SetParameter("base_radius", radius + CloudBase);
        _water.SetParameter("top_radius", radius + CloudTop);

        Wakes = new Wakes { Name = "Wakes" };
        AddChild(Wakes);
        Wakes.Build(_body);

    }

    private void SyncWater(double time, Vector3 centre, float altitude, Vector2 rotation, Basis cloudFrame) {

        _water.SetParameter("planet_centre", centre);
        _water.SetParameter("camera_altitude", altitude);
        _water.SetParameter("terrain_rotation", rotation);
        _water.SetParameter("cloud_frame", cloudFrame);

        // Cox and Munk's clean-sea fit: the mean square slope of the whole wind-wave spectrum. Two
        // detail layers carry three quarters of it; the rest lies below the finest and is roughness.
        double wind = _body.Weather?.SeaSpeedAt(time) ?? 10.0;
        double meanSquare = 0.003 + 0.00512 * wind;
        _water.SetParameter("layer_variance", (float)(meanSquare * 0.375));
        _water.SetParameter("micro_variance", (float)(meanSquare * 0.25));
        _water.SetParameter("detail_gain", (float)Math.Sqrt(meanSquare * 0.375 / WaterTextures.SlopeVariance));

        // Monahan and O'Muircheartaigh's whitecap cover.
        _water.SetParameter("whitecaps", (float)Math.Min(3.84e-6 * Math.Pow(wind, 3.41), 0.35));

        _water.SetParameter("detail_drift_near", Drift(DetailTiles.X, time));
        _water.SetParameter("detail_drift_far", Drift(DetailTiles.Y, time));

        // Foam rides the wind drift, a few percent of the wind, integrated so gusts never jump it.
        double drift = 0.03 * (_body.Weather?.WindDistanceAt(time) ?? wind * time) / FoamTile;
        _water.SetParameter("foam_drift", new Vector2((float)-(drift - Math.Floor(drift)), 0.0f));

        Wakes.Publish(_water, time);

    }

    // Each layer moves at the phase speed of its middle wavelength, an eighth of the tile. The second
    // read travels slower and a little across, so the two interfere instead of sliding as one.
    private Vector4 Drift(float tile, double time) {

        double speed = Math.Sqrt(_body.SurfaceGravity * tile / 8.0 / Math.Tau);
        double first = speed * time / tile;
        double second = 0.62 * first;
        double across = 0.21 * first;

        return new Vector4((float)-(first - Math.Floor(first)), 0.0f,
            (float)(0.37 - (second - Math.Floor(second))), (float)(0.61 - (across - Math.Floor(across))));

    }

}
