using System;
using System.Collections.Generic;
using System.Threading;

using Godot;

namespace FullThrust.Game;

public sealed partial class CloudRender : CompositorEffect {

    private sealed record Frame(float[] Parameters, Rid[] Textures);
    private sealed record Target(Rid Color, Rid Depth);
    private static readonly string[] TextureNames = { "cloud_map", "shape_noise", "detail_noise", "local_cloud_shadow", "local_cloud_lighting" };
    private readonly RDShaderFile _source = GD.Load<RDShaderFile>("res://Shaders/Clouds/CloudRender.glsl");
    private readonly Texture2Drd _color = new();
    private readonly Texture2Drd _depth = new();
    private Frame _frame;
    private Target _target;
    private RenderingDevice _device;
    private Rid _shader;
    private Rid _pipeline;
    private Rid _linear;
    private Rid _nearest;
    private Rid _repeat;
    private Rid _uniformBuffer;
    private Rid _colorRid;
    private Rid _depthRid;
    private Vector2I _size;
    private int _released;
    private int _renderFrame;
    private readonly Queue<(Rid Color, Rid Depth, int Frame)> _retired = new();

    public bool Ready => _color.TextureRdRid.IsValid;

    public CloudRender() {

        EffectCallbackType = EffectCallbackTypeEnum.PreTransparent;
        AccessResolvedDepth = true;

    }

    public void Sync(ShaderMaterial material, bool visible) {

        Target target = Volatile.Read(ref _target);
        if (target != null && _color.TextureRdRid != target.Color) {

            // Publish bindings before Godot prepares spatial draw lists, never inside their callback.
            _color.TextureRdRid = target.Color;
            _depth.TextureRdRid = target.Depth;

        }
        Enabled = visible;
        if (_color.TextureRdRid.IsValid) {

            material.SetShaderParameter("cloud_buffer", _color);
            material.SetShaderParameter("cloud_depth", _depth);

        }
        material.SetShaderParameter("cloud_buffer_ready", _color.TextureRdRid.IsValid);
        if (!visible) { return; }
        float[] data = new float[80];
        void Pack(int slot, string vector, string scalar, float fallback = 0.0f) {

            Vector3 v = material.GetShaderParameter(vector).AsVector3();
            Variant value = material.GetShaderParameter(scalar);
            Put(data, slot, new Vector4(v.X, v.Y, v.Z, value.VariantType == Variant.Type.Nil ? fallback : value.AsSingle()));

        }
        float Scalar(string name, float fallback) {

            Variant value = material.GetShaderParameter(name);
            return value.VariantType == Variant.Type.Nil ? fallback : value.AsSingle();

        }
        Pack(0, "planet_centre", "planet_radius");
        Pack(1, "sun_direction", "extinction", 0.009f);
        Pack(2, "eye_up", "eye_height");
        Pack(3, "shadow_centre", "shadow_ready");
        Pack(4, "shadow_east", "shadow_span", 16000.0f);
        Pack(5, "shadow_north", "sun_power", 2.0f);
        Pack(6, "coastal_weather_direction", "weather_coverage");
        Put(data, 7, new Vector4(Scalar("base_radius", 0), Scalar("top_radius", 0), Scalar("ambient_gain", 0.28f), Scalar("sun_shafts", 1)));
        Basis cloudFrame = material.GetShaderParameter("cloud_frame").AsBasis();
        Put(data, 8, new Vector4(cloudFrame.X.X, cloudFrame.X.Y, cloudFrame.X.Z, Scalar("fog_enabled", 1)));
        Put(data, 9, new Vector4(cloudFrame.Y.X, cloudFrame.Y.Y, cloudFrame.Y.Z, 0.0f));
        Put(data, 10, new Vector4(cloudFrame.Z.X, cloudFrame.Z.Y, cloudFrame.Z.Z, 0.0f));
        Rid[] textures = new Rid[TextureNames.Length];
        for (int i = 0; i < textures.Length; i++) {

            if (material.GetShaderParameter(TextureNames[i]).AsGodotObject() is not Texture texture) { return; }
            textures[i] = texture.GetRid();

        }
        // Publish a complete immutable snapshot; the renderer never reads scene nodes or live materials.
        Volatile.Write(ref _frame, new Frame(data, textures));

    }

    private static void Put(float[] data, int slot, Vector4 value) {

        for (int i = 0; i < 4; i++) { data[slot * 4 + i] = value[i]; }

    }

    private Rid Sampler(RenderingDevice.SamplerFilter filter, RenderingDevice.SamplerRepeatMode repeat) {

        using RDSamplerState state = new() { MinFilter = filter, MagFilter = filter, MipFilter = filter, RepeatU = repeat, RepeatV = repeat, RepeatW = repeat };
        return _device.SamplerCreate(state);

    }

    private Rid Texture(Vector2I size, RenderingDevice.DataFormat format) {

        using RDTextureFormat spec = new() {

            Width = (uint)size.X, Height = (uint)size.Y, Format = format,
            TextureType = RenderingDevice.TextureType.Type2D,
            UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit,

        };
        using RDTextureView view = new();
        return _device.TextureCreate(spec, view);

    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData) {

        Frame frame = Volatile.Read(ref _frame);
        if (frame == null || renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers || buffers.GetViewCount() != 1) { return; }
        Vector2I full = buffers.GetInternalSize();
        if (full.X < 8 || full.Y < 8) { return; }
        _renderFrame++;
        while (_retired.TryPeek(out var old) && _renderFrame - old.Frame > 3) {

            _device.FreeRid(old.Color);
            _device.FreeRid(old.Depth);
            _retired.Dequeue();

        }
        if (_device == null) {

            _device = RenderingServer.GetRenderingDevice();
            using RDShaderSpirV spirv = _source.GetSpirV();
            if (!string.IsNullOrEmpty(spirv.CompileErrorCompute)) { GD.PushError(spirv.CompileErrorCompute); return; }
            _shader = _device.ShaderCreateFromSpirV(spirv);
            _pipeline = _device.ComputePipelineCreate(_shader);
            _linear = Sampler(RenderingDevice.SamplerFilter.Linear, RenderingDevice.SamplerRepeatMode.ClampToEdge);
            _nearest = Sampler(RenderingDevice.SamplerFilter.Nearest, RenderingDevice.SamplerRepeatMode.ClampToEdge);
            _repeat = Sampler(RenderingDevice.SamplerFilter.Linear, RenderingDevice.SamplerRepeatMode.Repeat);
            _uniformBuffer = _device.UniformBufferCreate(320);

        }
        if (!_pipeline.IsValid) { return; }
        Vector2I size = (full + Vector2I.One) / 2;
        if (size != _size) {

            // This frame's spatial draw can still hold the old material bindings.
            if (_colorRid.IsValid) { _retired.Enqueue((_colorRid, _depthRid, _renderFrame)); }
            _colorRid = Texture(size, RenderingDevice.DataFormat.R16G16B16A16Sfloat);
            _depthRid = Texture(size, RenderingDevice.DataFormat.R32Sfloat);
            Volatile.Write(ref _target, new Target(_colorRid, _depthRid));
            _size = size;

        }
        float[] data = (float[])frame.Parameters.Clone();
        RenderSceneData scene = renderData.GetRenderSceneData();
        Projection projection = scene.GetCamProjection();
        Transform3D camera = scene.GetCamTransform();
        Projection inverse = projection.Inverse();
        for (int i = 0; i < 4; i++) { Put(data, 16 + i, inverse[i]); }
        Put(data, 12, new Vector4(camera.Basis.X.X, camera.Basis.X.Y, camera.Basis.X.Z, 0));
        Put(data, 13, new Vector4(camera.Basis.Y.X, camera.Basis.Y.Y, camera.Basis.Y.Z, 0));
        Put(data, 14, new Vector4(camera.Basis.Z.X, camera.Basis.Z.Y, camera.Basis.Z.Z, 0));
        Put(data, 15, new Vector4(camera.Origin.X, camera.Origin.Y, camera.Origin.Z, 0));
        byte[] bytes = new byte[320];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        _device.BufferUpdate(_uniformBuffer, 0, 320, bytes);
        Godot.Collections.Array<RDUniform> uniforms = new();
        void Bind(int binding, RenderingDevice.UniformType type, params Rid[] ids) {

            RDUniform uniform = new() { Binding = binding, UniformType = type };
            foreach (Rid id in ids) { uniform.AddId(id); }
            uniforms.Add(uniform);

        }
        Bind(0, RenderingDevice.UniformType.Image, _colorRid);
        Bind(1, RenderingDevice.UniformType.Image, _depthRid);
        Bind(2, RenderingDevice.UniformType.SamplerWithTexture, _nearest, buffers.GetDepthLayer(0));
        for (int i = 0; i < frame.Textures.Length; i++) {

            Bind(i + 3, RenderingDevice.UniformType.SamplerWithTexture, i < 3 ? _repeat : _linear, RenderingServer.TextureGetRdTexture(frame.Textures[i]));

        }
        Bind(8, RenderingDevice.UniformType.UniformBuffer, _uniformBuffer);
        Rid set = UniformSetCacheRD.GetCache(_shader, 0, uniforms);
        foreach (RDUniform uniform in uniforms) { uniform.Dispose(); }
        long commands = _device.ComputeListBegin();
        _device.ComputeListBindComputePipeline(commands, _pipeline);
        _device.ComputeListBindUniformSet(commands, set, 0);
        _device.ComputeListDispatch(commands, (uint)(size.X + 7) / 8, (uint)(size.Y + 7) / 8, 1);
        _device.ComputeListEnd();

    }

    public void Release() {

        if (Interlocked.Exchange(ref _released, 1) != 0) { return; }
        Enabled = false;
        Volatile.Write(ref _frame, null);
        RenderingServer.CallOnRenderThread(Callable.From(() => {

            Rid[] resources = { _colorRid, _depthRid, _pipeline, _shader, _uniformBuffer, _linear, _nearest, _repeat };
            _color.TextureRdRid = default;
            _depth.TextureRdRid = default;
            _color.Dispose();
            _depth.Dispose();
            RenderingDevice device = RenderingServer.GetRenderingDevice();
            foreach (Rid resource in resources) { if (resource.IsValid) { device.FreeRid(resource); } }
            while (_retired.TryDequeue(out var old)) {

                device.FreeRid(old.Color);
                device.FreeRid(old.Depth);

            }

        }));

    }

    public override void _Notification(int what) {

        if (what == NotificationPredelete) { Release(); }

    }

}
