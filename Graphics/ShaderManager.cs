using SponzaTest.Scene;
using static SDL3.SDL;

namespace SponzaTest.Graphics;

public enum PipelinePass { GBuffer, Shadow, Composite }

public sealed record PipelineDescription(
    string VertexShader,
    string FragmentShader,
    GPUTextureFormat[] ColorFormats,
    PipelinePass Pass,
    GPUCullMode CullMode = GPUCullMode.None);

public sealed class ShaderManager
{
    private readonly SdlGpuContext _context;

    public ShaderManager(SdlGpuContext ctx) => _context = ctx;

    private unsafe IntPtr Load(string name, GPUShaderStage stage, uint samplers, uint uniforms)
    {
        var formats = GetGPUShaderFormats(_context.Device);
        var format = (formats & GPUShaderFormat.SPIRV) != 0 ? GPUShaderFormat.SPIRV :
            (formats & GPUShaderFormat.DXIL) != 0 ? GPUShaderFormat.DXIL : GPUShaderFormat.MSL;
        string extension = format switch
        {
            GPUShaderFormat.SPIRV => "spv",
            GPUShaderFormat.DXIL => "dxil",
            GPUShaderFormat.MSL => "msl",
        };
        byte[] code = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "Shaders", "Compiled", $"{name}.{extension}"));

        fixed (byte* codePointer = code)
        {
            var info = new GPUShaderCreateInfo
            {
                Code = (IntPtr)codePointer,
                CodeSize = (nuint)code.Length,
                Format = format,
                Stage = stage,
                NumSamplers = samplers,
                NumUniformBuffers = uniforms,
                Entrypoint = format == GPUShaderFormat.MSL ? "main0" : "main"
            };
            return SdlGpuContext.Check(CreateGPUShader(_context.Device, in info));
        }
    }

    public unsafe IntPtr CreatePipeline(PipelineDescription description)
    {
        var colors = description.ColorFormats;
        bool mesh = description.Pass != PipelinePass.Composite;
        bool depth = description.Pass != PipelinePass.Composite;
        bool isShadowPass = description.Pass == PipelinePass.Shadow;
        var vertexShader = Load(description.VertexShader, GPUShaderStage.Vertex, 0, mesh ? 1u : 0u);
        IntPtr fragmentShader = IntPtr.Zero;
        try
        {
            uint samplerCount = isShadowPass ? 0u : mesh ? 1u : 5u;
            uint uniformCount = isShadowPass ? 0u : 1u;
            fragmentShader = Load(description.FragmentShader, GPUShaderStage.Fragment, samplerCount, uniformCount);

            var vertexBuffer = new GPUVertexBufferDescription
            {
                Slot = 0,
                Pitch = 32,
                InputRate = GPUVertexInputRate.Vertex
            };
            GPUVertexAttribute* attributes = stackalloc GPUVertexAttribute[3];
            attributes[0] = new()
            {
                Location = 0,
                BufferSlot = 0,
                Format = GPUVertexElementFormat.Float3,
                Offset = 0
            };
            attributes[1] = new()
            {
                Location = 1,
                BufferSlot = 0,
                Format = GPUVertexElementFormat.Float3,
                Offset = 12
            };
            attributes[2] = new()
            {
                Location = 2,
                BufferSlot = 0,
                Format = GPUVertexElementFormat.Float2,
                Offset = 24
            };

            var targets = new GPUColorTargetDescription[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                targets[i].Format = colors[i];
            }

            fixed (GPUColorTargetDescription* target = targets)
            {
                var info = new GPUGraphicsPipelineCreateInfo
                {
                    VertexShader = vertexShader,
                    FragmentShader = fragmentShader,
                    VertexInputState = new()
                    {
                        VertexBufferDescriptions = mesh ? (IntPtr)(&vertexBuffer) : IntPtr.Zero,
                        NumVertexBuffers = mesh ? 1u : 0u,
                        VertexAttributes = mesh ? (IntPtr)attributes : IntPtr.Zero,
                        NumVertexAttributes = mesh ? (isShadowPass ? 1u : 3u) : 0u
                    },
                    PrimitiveType = GPUPrimitiveType.TriangleList,
                    RasterizerState = new()
                    {
                        FillMode = GPUFillMode.Fill,
                        CullMode = description.CullMode,
                        FrontFace = GPUFrontFace.CounterClockwise
                    },
                    MultisampleState = new()
                    {
                        SampleCount = GPUSampleCount.SampleCount1
                    },
                    DepthStencilState = new()
                    {
                        EnableDepthTest = depth,
                        EnableDepthWrite = depth,
                        CompareOp = GPUCompareOp.LessOrEqual
                    },
                    TargetInfo = new()
                    {
                        ColorTargetDescriptions = (IntPtr)target,
                        NumColorTargets = (uint)colors.Length,
                        HasDepthStencilTarget = depth,
                        DepthStencilFormat = GPUTextureFormat.D32Float
                    }
                };
                return SdlGpuContext.Check(CreateGPUGraphicsPipeline(_context.Device, in info));
            }
        }
        finally
        {
            ReleaseGPUShader(_context.Device, vertexShader);
            if (fragmentShader != IntPtr.Zero) ReleaseGPUShader(_context.Device, fragmentShader);
        }
    }

    public static unsafe void PushVertex<T>(IntPtr command, T value) where T : unmanaged => PushGPUVertexUniformData(command, 0, (IntPtr)(&value), (uint)sizeof(T));

    public static unsafe void PushFragment<T>(IntPtr command, T value) where T : unmanaged => PushGPUFragmentUniformData(command, 0, (IntPtr)(&value), (uint)sizeof(T));

    public static unsafe void BindMesh(IntPtr pass, GpuMeshBuffers mesh)
    {
        var vertex = new GPUBufferBinding { Buffer = mesh.VertexBuffer.Handle };
        BindGPUVertexBuffers(pass, 0, (IntPtr)(&vertex), 1);
        var index = new GPUBufferBinding
        {
            Buffer = mesh.IndexBuffer.Handle
        };
        BindGPUIndexBuffer(pass, in index, GPUIndexElementSize.IndexElementSize32Bit);
    }
}
