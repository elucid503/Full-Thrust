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

    private void BuildWater(float radius, Texture2D cloud, Vector3 sunDirection) {

        WaterTextures.Bake();

        // Drawn before the air, which then hazes it like any other surface.
        _water = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Ocean/Water.gdshader"), RenderPriority = -1 };

        _water.SetShaderParameter("shoreline_map", Shoreline(_body.Terrain));
        _water.SetShaderParameter("wave_detail", WaterTextures.Waves);
        _water.SetShaderParameter("foam_detail", WaterTextures.Foam);
        _water.SetShaderParameter("detail_tiles", DetailTiles);
        _water.SetShaderParameter("foam_tile", FoamTile);
        _water.SetShaderParameter("cloud_map", cloud);
        _water.SetShaderParameter("shape_noise", _cloudShape);
        _water.SetShaderParameter("detail_noise", _cloudDetail);
        _water.SetShaderParameter("coastal_weather_direction", CoastalWeatherDirection());
        _water.SetShaderParameter("base_radius", radius + CloudBase);
        _water.SetShaderParameter("top_radius", radius + CloudTop);
        _water.SetShaderParameter("planet_radius", radius);
        _water.SetShaderParameter("sun_direction", sunDirection);

        Wakes = new Wakes { Name = "Wakes" };
        AddChild(Wakes);
        Wakes.Build(_body);

    }

    private void SyncWater(double time, Vector3 centre, float altitude, Vector2 rotation, Basis cloudFrame) {

        _water.SetShaderParameter("planet_centre", centre);
        _water.SetShaderParameter("camera_altitude", altitude);
        _water.SetShaderParameter("terrain_rotation", rotation);
        _water.SetShaderParameter("cloud_frame", cloudFrame);

        // Cox and Munk's clean-sea fit: the mean square slope of the whole wind-wave spectrum. Two
        // detail layers carry three quarters of it; the rest lies below the finest and is roughness.
        double wind = _body.Weather?.SeaSpeedAt(time) ?? 10.0;
        double meanSquare = 0.003 + 0.00512 * wind;
        _water.SetShaderParameter("layer_variance", (float)(meanSquare * 0.375));
        _water.SetShaderParameter("micro_variance", (float)(meanSquare * 0.25));
        _water.SetShaderParameter("detail_gain", (float)Math.Sqrt(meanSquare * 0.375 / WaterTextures.SlopeVariance));

        // Monahan and O'Muircheartaigh's whitecap cover.
        _water.SetShaderParameter("whitecaps", (float)Math.Min(3.84e-6 * Math.Pow(wind, 3.41), 0.35));

        _water.SetShaderParameter("detail_drift_near", Drift(DetailTiles.X, time));
        _water.SetShaderParameter("detail_drift_far", Drift(DetailTiles.Y, time));

        // Foam rides the wind drift, a few percent of the wind, integrated so gusts never jump it.
        double drift = 0.03 * (_body.Weather?.WindDistanceAt(time) ?? wind * time) / FoamTile;
        _water.SetShaderParameter("foam_drift", new Vector2((float)-(drift - Math.Floor(drift)), 0.0f));

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
