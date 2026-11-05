using SponzaTest.Scene;
using static SDL3.SDL;

namespace SponzaTest.Graphics;

public sealed class ShaderManager
{
    private readonly SdlGpuContext _ctx;
    public ShaderManager(SdlGpuContext ctx) => _ctx = ctx;
    private unsafe IntPtr Load(string name, GPUShaderStage stage, uint samplers, uint uniforms)
    {
        var formats = GetGPUShaderFormats(_ctx.Device);
        var format = (formats & GPUShaderFormat.SPIRV) != 0 ? GPUShaderFormat.SPIRV :
            (formats & GPUShaderFormat.DXIL) != 0 ? GPUShaderFormat.DXIL : GPUShaderFormat.MSL;
        string ext = format == GPUShaderFormat.SPIRV ? "spv" : format == GPUShaderFormat.DXIL ? "dxil" : "msl";
        byte[] code = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "Shaders", "Compiled", $"{name}.{ext}"));
        fixed (byte* p = code)
        {
            var info = new GPUShaderCreateInfo { Code = (IntPtr)p, CodeSize = (nuint)code.Length,
                Format = format, Stage = stage, NumSamplers = samplers, NumUniformBuffers = uniforms,
                Entrypoint = format == GPUShaderFormat.MSL ? "main0" : "main" };
            return SdlGpuContext.Check(CreateGPUShader(_ctx.Device, in info));
        }
    }
    public unsafe IntPtr CreatePipeline(string vertex, string? fragment, GPUTextureFormat[] colors, bool depth, bool mesh = true, bool cull = false)
    {
        var vs = Load(vertex, GPUShaderStage.Vertex, 0, mesh ? 1u : 0u);
        IntPtr fs = IntPtr.Zero;
        try
        {
            fs = Load(fragment ?? "shadow.frag", GPUShaderStage.Fragment, fragment == null ? 0u : mesh ? 1u : 5u, fragment == null ? 0u : 1u);
            var vb = new GPUVertexBufferDescription { Slot = 0, Pitch = 32, InputRate = GPUVertexInputRate.Vertex };
            GPUVertexAttribute* attrs = stackalloc GPUVertexAttribute[3];
            attrs[0] = new() { Location = 0, BufferSlot = 0, Format = GPUVertexElementFormat.Float3, Offset = 0 };
            attrs[1] = new() { Location = 1, BufferSlot = 0, Format = GPUVertexElementFormat.Float3, Offset = 12 };
            attrs[2] = new() { Location = 2, BufferSlot = 0, Format = GPUVertexElementFormat.Float2, Offset = 24 };
            var targets = new GPUColorTargetDescription[colors.Length];
            for (int i = 0; i < colors.Length; i++) targets[i].Format = colors[i];
            fixed (GPUColorTargetDescription* target = targets)
            {
                var info = new GPUGraphicsPipelineCreateInfo {
                    VertexShader = vs, FragmentShader = fs,
                    VertexInputState = new() { VertexBufferDescriptions = mesh ? (IntPtr)(&vb) : IntPtr.Zero,
                        NumVertexBuffers = mesh ? 1u : 0u, VertexAttributes = mesh ? (IntPtr)attrs : IntPtr.Zero,
                        NumVertexAttributes = mesh ? (fragment == null ? 1u : 3u) : 0u },
                    PrimitiveType = GPUPrimitiveType.TriangleList,
                    RasterizerState = new() { FillMode = GPUFillMode.Fill, CullMode = cull ? GPUCullMode.Back : GPUCullMode.None,
                        FrontFace = GPUFrontFace.CounterClockwise },
                    MultisampleState = new() { SampleCount = GPUSampleCount.SampleCount1 },
                    DepthStencilState = new() { EnableDepthTest = depth, EnableDepthWrite = depth, CompareOp = GPUCompareOp.LessOrEqual },
                    TargetInfo = new() { ColorTargetDescriptions = (IntPtr)target, NumColorTargets = (uint)colors.Length,
                        HasDepthStencilTarget = depth, DepthStencilFormat = GPUTextureFormat.D32Float }
                };
                return SdlGpuContext.Check(CreateGPUGraphicsPipeline(_ctx.Device, in info));
            }
        }
        finally { ReleaseGPUShader(_ctx.Device, vs); if (fs != IntPtr.Zero) ReleaseGPUShader(_ctx.Device, fs); }
    }
    public static unsafe void PushVertex<T>(IntPtr command, T value) where T : unmanaged => PushGPUVertexUniformData(command, 0, (IntPtr)(&value), (uint)sizeof(T));
    public static unsafe void PushFragment<T>(IntPtr command, T value) where T : unmanaged => PushGPUFragmentUniformData(command, 0, (IntPtr)(&value), (uint)sizeof(T));
    public static void BindMesh(IntPtr pass, GpuMeshBuffers mesh)
    {
        BindGPUVertexBuffers(pass, 0, new[] { new GPUBufferBinding { Buffer = mesh.VertexBuffer.Handle } }, 1);
        var index = new GPUBufferBinding { Buffer = mesh.IndexBuffer.Handle };
        BindGPUIndexBuffer(pass, in index, GPUIndexElementSize.IndexElementSize32Bit);
    }
}
