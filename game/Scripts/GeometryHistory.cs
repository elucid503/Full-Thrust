using System;

using Godot;

namespace FullThrust.Game;

public sealed partial class GeometryHistory : CompositorEffect {

    private readonly RDShaderFile _source = GD.Load<RDShaderFile>("res://Shaders/GeometryHistory.glsl");
    private RenderingDevice _device;
    private Rid _shader;
    private Rid _pipeline;
    private Rid _sampler;
    private readonly byte[] _parameters = new byte[16];
    private Vector2I _size;

    public GeometryHistory() {

        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        AccessResolvedColor = true;
        AccessResolvedDepth = true;

    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData) {

        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers) { return; }
        Vector2I size = buffers.GetInternalSize();
        if (size.X < 8 || size.Y < 8) { return; }
        if (size != _size) {

            // Upscalers reallocate these buffers on a size change; the previous RIDs are then dead.
            _size = size;
            return;

        }
        if (_device == null) {

            _device = RenderingServer.GetRenderingDevice();
            using RDShaderSpirV spirv = _source.GetSpirV();
            if (!string.IsNullOrEmpty(spirv.CompileErrorCompute)) {

                GD.PushError(spirv.CompileErrorCompute);
                return;

            }
            _shader = _device.ShaderCreateFromSpirV(spirv);
            if (!_shader.IsValid) { return; }
            _pipeline = _device.ComputePipelineCreate(_shader);
            using RDSamplerState sampler = new();
            _sampler = _device.SamplerCreate(sampler);

        }
        if (!_pipeline.IsValid || !_shader.IsValid || !_sampler.IsValid) { return; }
        float near = renderData.GetRenderSceneData().GetCamProjection().GetZNear();
        BitConverter.TryWriteBytes(_parameters.AsSpan(0, 4), (float)size.X);
        BitConverter.TryWriteBytes(_parameters.AsSpan(4, 4), (float)size.Y);
        BitConverter.TryWriteBytes(_parameters.AsSpan(8, 4), near / 5000.0f);
        BitConverter.TryWriteBytes(_parameters.AsSpan(12, 4), 0.08f);
        for (uint view = 0; view < buffers.GetViewCount(); view++) {

            Rid color = buffers.GetColorLayer(view, false);
            Rid depth = buffers.GetDepthLayer(view, false);
            if (!color.IsValid || !depth.IsValid) { continue; }
            RDTextureFormat colorFormat = _device.TextureGetFormat(color);
            if (colorFormat.Format != RenderingDevice.DataFormat.R16G16B16A16Sfloat
                || (colorFormat.UsageBits & RenderingDevice.TextureUsageBits.StorageBit) == 0) {

                continue;

            }
            using RDUniform colorUniform = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 0 };
            colorUniform.AddId(color);
            using RDUniform depthUniform = new() { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 1 };
            depthUniform.AddId(_sampler);
            depthUniform.AddId(depth);
            Godot.Collections.Array<RDUniform> uniforms = new() { colorUniform, depthUniform };
            Rid set = UniformSetCacheRD.GetCache(_shader, 0, uniforms);
            if (!set.IsValid) { return; }
            long commands = _device.ComputeListBegin();
            _device.ComputeListBindComputePipeline(commands, _pipeline);
            _device.ComputeListBindUniformSet(commands, set, 0);
            _device.ComputeListSetPushConstant(commands, _parameters, 16);
            _device.ComputeListDispatch(commands, (uint)(size.X + 7) / 8, (uint)(size.Y + 7) / 8, 1);
            _device.ComputeListEnd();

        }

    }

    public override void _Notification(int what) {

        if (what != NotificationPredelete) { return; }
        Rid shader = _shader;
        Rid pipeline = _pipeline;
        Rid sampler = _sampler;
        _shader = default;
        _pipeline = default;
        _sampler = default;
        _device = null;
        if (!shader.IsValid && !pipeline.IsValid && !sampler.IsValid) { return; }
        RenderingServer.CallOnRenderThread(Callable.From(() => {

            RenderingDevice device = RenderingServer.GetRenderingDevice();
            if (pipeline.IsValid) { device.FreeRid(pipeline); }
            if (shader.IsValid) { device.FreeRid(shader); }
            if (sampler.IsValid) { device.FreeRid(sampler); }

        }));

    }

}
