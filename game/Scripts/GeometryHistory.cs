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

    public GeometryHistory() {

        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        AccessResolvedColor = true;
        AccessResolvedDepth = true;

    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData) {

        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers) { return; }
        Vector2I size = buffers.GetInternalSize();
        if (size.X == 0 || size.Y == 0) { return; }
        if (_device == null) {

            _device = RenderingServer.GetRenderingDevice();
            using RDShaderSpirV spirv = _source.GetSpirV();
            if (!string.IsNullOrEmpty(spirv.CompileErrorCompute)) {

                GD.PushError(spirv.CompileErrorCompute);
                return;

            }
            _shader = _device.ShaderCreateFromSpirV(spirv);
            _pipeline = _device.ComputePipelineCreate(_shader);
            using RDSamplerState sampler = new();
            _sampler = _device.SamplerCreate(sampler);

        }
        if (!_pipeline.IsValid) { return; }
        float near = renderData.GetRenderSceneData().GetCamProjection().GetZNear();
        BitConverter.TryWriteBytes(_parameters.AsSpan(0, 4), (float)size.X);
        BitConverter.TryWriteBytes(_parameters.AsSpan(4, 4), (float)size.Y);
        BitConverter.TryWriteBytes(_parameters.AsSpan(8, 4), near / 5000.0f);
        BitConverter.TryWriteBytes(_parameters.AsSpan(12, 4), 0.08f);
        for (uint view = 0; view < buffers.GetViewCount(); view++) {

            using RDUniform color = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 0 };
            color.AddId(buffers.GetColorLayer(view, false));
            using RDUniform depth = new() { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 1 };
            depth.AddId(_sampler);
            depth.AddId(buffers.GetDepthLayer(view, false));
            Godot.Collections.Array<RDUniform> uniforms = new() { color, depth };
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

        if (what != NotificationPredelete || !_shader.IsValid) { return; }
        Rid shader = _shader;
        Rid sampler = _sampler;
        RenderingServer.CallOnRenderThread(Callable.From(() => {

            RenderingDevice device = RenderingServer.GetRenderingDevice();
            device.FreeRid(shader);
            device.FreeRid(sampler);

        }));

    }

}
