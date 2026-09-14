using System;

using Godot;

namespace FullThrust.Game;

public static class GeometryHistoryChecks {

    public static void Run() {

        using RenderingDevice device = RenderingServer.CreateLocalRenderingDevice();
        using RDShaderFile file = GD.Load<RDShaderFile>("res://Shaders/GeometryHistory.glsl");
        using RDShaderSpirV spirv = file.GetSpirV();
        Rid shader = device.ShaderCreateFromSpirV(spirv);
        Rid pipeline = device.ComputePipelineCreate(shader);
        using RDTextureFormat format = new() {

            Width = 16, Height = 16, Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
            UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.CanCopyFromBit,

        };
        byte[] pixels = new byte[16 * 16 * 8];
        float[] depths = new float[16 * 16];
        for (int y = 0; y < 16; y++) {

            for (int x = 0; x < 16; x++) {

                int index = y * 16 + x;
                BitConverter.TryWriteBytes(pixels.AsSpan(index * 8, 2), (Half)0.2f);
                BitConverter.TryWriteBytes(pixels.AsSpan(index * 8 + 2, 2), (Half)0.4f);
                BitConverter.TryWriteBytes(pixels.AsSpan(index * 8 + 4, 2), (Half)0.7f);
                BitConverter.TryWriteBytes(pixels.AsSpan(index * 8 + 6, 2), (Half)0.9f);
                depths[index] = x < 8 ? 0.01f : 0.0f;

            }

        }
        using RDTextureView view = new();
        Rid color = device.TextureCreate(format, view, new Godot.Collections.Array<byte[]> { pixels });
        format.Format = RenderingDevice.DataFormat.R32Sfloat;
        format.UsageBits = RenderingDevice.TextureUsageBits.SamplingBit;
        byte[] depthData = new byte[depths.Length * 4];
        Buffer.BlockCopy(depths, 0, depthData, 0, depthData.Length);
        Rid depth = device.TextureCreate(format, view, new Godot.Collections.Array<byte[]> { depthData });
        using RDSamplerState samplerState = new();
        Rid sampler = device.SamplerCreate(samplerState);
        using RDUniform colorUniform = new() { Binding = 0, UniformType = RenderingDevice.UniformType.Image };
        colorUniform.AddId(color);
        using RDUniform depthUniform = new() { Binding = 1, UniformType = RenderingDevice.UniformType.SamplerWithTexture };
        depthUniform.AddId(sampler);
        depthUniform.AddId(depth);
        Rid uniforms = device.UniformSetCreate(new Godot.Collections.Array<RDUniform> { colorUniform, depthUniform }, shader, 0);
        float[] values = { 16.0f, 16.0f, 0.00008f, 0.08f };
        byte[] parameters = new byte[16];
        Buffer.BlockCopy(values, 0, parameters, 0, 16);
        long commands = device.ComputeListBegin();
        device.ComputeListBindComputePipeline(commands, pipeline);
        device.ComputeListBindUniformSet(commands, uniforms, 0);
        device.ComputeListSetPushConstant(commands, parameters, 16);
        device.ComputeListDispatch(commands, 2, 2, 1);
        device.ComputeListEnd();
        device.Submit();
        device.Sync();
        byte[] result = device.TextureGetData(color, 0);
        for (int y = 0; y < 16; y++) {

            for (int x = 0; x < 16; x++) {

                int index = (y * 16 + x) * 8;
                if (!pixels.AsSpan(index, 6).SequenceEqual(result.AsSpan(index, 6))) {

                    throw new InvalidOperationException("History correction changed visible RGB.");

                }
                float expected = x >= 6 && x <= 9 ? 0.08f : 0.9f;
                float actual = (float)BitConverter.ToHalf(result, index + 6);
                if (Math.Abs(actual - expected) > 0.001f) {

                    throw new InvalidOperationException($"History correction affected the wrong pixel ({x}, {y}): {actual}");

                }

            }

        }
        device.FreeRid(uniforms);
        device.FreeRid(pipeline);
        device.FreeRid(shader);
        device.FreeRid(color);
        device.FreeRid(depth);
        device.FreeRid(sampler);
        GD.Print("PASS history mask preserves RGB exactly and only changes the foreground silhouette neighbourhood (256 pixels)");

    }

}
