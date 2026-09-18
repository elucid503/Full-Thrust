using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class CloudShadows : Node {

    private const int LightingResolution = 256;
    private readonly List<ShaderMaterial> _receivers = new();
    private SubViewport _viewport;
    private ShaderMaterial _material;
    private SubViewport _backViewport;
    private ShaderMaterial _backMaterial;
    private SubViewport _lightingViewport;
    private SubViewport _backLightingViewport;
    private ShaderMaterial _lightingMaterial;
    private ShaderMaterial _backLightingMaterial;
    private ShaderMaterial[] _materials;
    private Vector3d _visibleAnchor;
    private Vector3d _visibleEast;
    private Vector3d _visibleNorth;
    private ulong _pendingFrame;
    private bool _pending;
    private Vector3d _anchor;
    private Vector3d _east;
    private Vector3d _north;
    private ulong _updated;
    private bool _hasMap;
    private double _updatedTime = double.NaN;
    private double _visibleTime;

    public void Build(Texture2D weather, Texture3D shape, Texture3D detail, float radius, float cloudBase, float cloudTop, Vector3 coastalWeather) {

        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Clouds/CloudShadow.gdshader") };
        _material.SetShaderParameter("cloud_map", weather);
        _material.SetShaderParameter("shape_noise", shape);
        _material.SetShaderParameter("detail_noise", detail);
        _material.SetShaderParameter("base_radius", radius + cloudBase);
        _material.SetShaderParameter("top_radius", radius + cloudTop);
        _material.SetShaderParameter("map_radius", radius);
        _material.SetShaderParameter("coastal_weather_direction", coastalWeather);
        _viewport = new SubViewport {

            Size = new Vector2I(512, 512),
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,

        };
        AddChild(_viewport);
        _viewport.AddChild(new ColorRect { Size = new Vector2(512, 512), Material = _material, Color = Colors.White });
        _backMaterial = (ShaderMaterial)_material.Duplicate();
        _backViewport = new SubViewport {

            Size = new Vector2I(512, 512),
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,

        };
        AddChild(_backViewport);
        _backViewport.AddChild(new ColorRect { Size = new Vector2(512, 512), Material = _backMaterial, Color = Colors.White });
        ShaderMaterial filter = new() { Shader = GD.Load<Shader>("res://Shaders/Clouds/CloudShadowFilter.gdshader") };
        foreach (SubViewport viewport in new[] { _viewport, _backViewport }) {

            viewport.AddChild(new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport });
            viewport.AddChild(new ColorRect { Size = new Vector2(512, 512), Material = filter, Color = Colors.White });

        }
        _lightingMaterial = (ShaderMaterial)_material.Duplicate();
        _lightingMaterial.Shader = GD.Load<Shader>("res://Shaders/Clouds/CloudLighting.gdshader");
        _lightingMaterial.SetShaderParameter("map_span", 64000.0f);
        _backLightingMaterial = (ShaderMaterial)_lightingMaterial.Duplicate();
        _lightingViewport = LightingViewport(_lightingMaterial);
        _backLightingViewport = LightingViewport(_backLightingMaterial);
        _materials = new[] { _material, _backMaterial, _lightingMaterial, _backLightingMaterial };

    }

    private SubViewport LightingViewport(ShaderMaterial material) {

        SubViewport viewport = new() {

            Size = new Vector2I(LightingResolution, LightingResolution),
            Disable3D = true,
            UseHdr2D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,

        };
        AddChild(viewport);
        viewport.AddChild(new ColorRect { Size = new Vector2(LightingResolution, LightingResolution), Material = material, Color = Colors.White });
        return viewport;

    }

    public void AddReceiver(ShaderMaterial material) {

        _receivers.Add(material);
        material.SetShaderParameter("local_cloud_shadow", _viewport.GetTexture());
        material.SetShaderParameter("local_cloud_lighting", _lightingViewport.GetTexture());

    }

    public void SetShape(Texture3D shape) {

        foreach (ShaderMaterial material in _materials) {

            material.SetShaderParameter("shape_noise", shape);

        }

    }

    public void SetWeather(float coverage) {

        foreach (ShaderMaterial material in _materials) { material.SetShaderParameter("weather_coverage", coverage); }

    }

    public void Sync(CelestialBody body, double time, Vector3d eye, Vector3 sun) {

        if (_pending && Engine.GetProcessFrames() > _pendingFrame) {

            (_viewport, _backViewport) = (_backViewport, _viewport);
            (_material, _backMaterial) = (_backMaterial, _material);
            (_lightingViewport, _backLightingViewport) = (_backLightingViewport, _lightingViewport);
            (_lightingMaterial, _backLightingMaterial) = (_backLightingMaterial, _lightingMaterial);
            _visibleAnchor = _anchor;
            _visibleEast = _east;
            _visibleNorth = _north;
            _visibleTime = _updatedTime;
            foreach (ShaderMaterial receiver in _receivers) {

                receiver.SetShaderParameter("local_cloud_shadow", _viewport.GetTexture());
                receiver.SetShaderParameter("local_cloud_lighting", _lightingViewport.GetTexture());

            }
            _pending = false;
            _hasMap = true;

        }
        if (eye.Length - body.Radius > 12000.0) {

            foreach (ShaderMaterial receiver in _receivers) { receiver.SetShaderParameter("shadow_ready", 0.0f); }
            return;

        }

        Vector3d focus = body.ToBodyFixed(eye, time).Normalized * body.Radius;
        bool moved = !_hasMap || (focus - _anchor).LengthSquared > 2000.0 * 2000.0;
        ulong now = Time.GetTicksMsec();
        double weatherElapsed = Math.Abs(time - _updatedTime);
        bool weatherChanged = weatherElapsed > 0.001 && (now - _updated > 500 || weatherElapsed * (body.Weather?.WindSpeedAt(time) ?? CloudWind.Speed) > 64.0);
        if (!_pending && (moved || weatherChanged)) {

            if (moved) {

                _anchor = focus;
                Vector3d up = focus.Normalized;
                _east = Vector3d.Cross(Math.Abs(up.Z) < 0.98 ? Vector3d.UnitZ : Vector3d.UnitX, up).Normalized;
                _north = Vector3d.Cross(up, _east);

            }
            _backMaterial.SetShaderParameter("map_centre", Frames.Direction(_anchor));
            _backMaterial.SetShaderParameter("map_east", Frames.Direction(_east));
            _backMaterial.SetShaderParameter("map_north", Frames.Direction(_north));
            _backMaterial.SetShaderParameter("map_sun", Frames.Direction(body.ToBodyFixed(Frames.Sim(sun), time)));
            _backMaterial.SetShaderParameter("cloud_frame", CloudWind.Frame(body, time, true));
            _backLightingMaterial.SetShaderParameter("map_centre", Frames.Direction(_anchor));
            _backLightingMaterial.SetShaderParameter("map_east", Frames.Direction(_east));
            _backLightingMaterial.SetShaderParameter("map_north", Frames.Direction(_north));
            _backLightingMaterial.SetShaderParameter("map_sun", Frames.Direction(body.ToBodyFixed(Frames.Sim(sun), time)));
            _backLightingMaterial.SetShaderParameter("cloud_frame", CloudWind.Frame(body, time, true));
            _backViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            _backLightingViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            _updated = now;
            _updatedTime = time;
            _pendingFrame = Engine.GetProcessFrames();
            _pending = true;

        }
        // Carry the published cache with its clouds between refreshes. This keeps shadows moving
        // continuously at normal speed, and prevents a stale, stationary shadow during time warp.
        double elapsed = time - _visibleTime;
        Vector3 centre = Frames.Point(body.ToInertial(CloudWind.Advect(_visibleAnchor, elapsed, body.Radius, body.Weather, _visibleTime), time));
        Vector3 east = Frames.Direction(body.ToInertial(CloudWind.Advect(_visibleEast, elapsed, body.Radius, body.Weather, _visibleTime), time));
        Vector3 north = Frames.Direction(body.ToInertial(CloudWind.Advect(_visibleNorth, elapsed, body.Radius, body.Weather, _visibleTime), time));
        foreach (ShaderMaterial receiver in _receivers) {

            receiver.SetShaderParameter("shadow_centre", centre);
            receiver.SetShaderParameter("shadow_east", east);
            receiver.SetShaderParameter("shadow_north", north);
            receiver.SetShaderParameter("shadow_ready", _hasMap ? (float)(1.0 - Landscape.Smooth(10000.0, 12000.0, eye.Length - body.Radius)) : 0.0f);

        }

    }

}
