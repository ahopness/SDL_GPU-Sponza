using System.Numerics;
using System.Runtime.InteropServices;
using SponzaTest.Graphics.Lightning;
using SponzaTest.Scene;
using static SDL3.SDL;

namespace SponzaTest.Graphics;

[StructLayout(LayoutKind.Sequential)]
internal struct CompositeUniforms
{

    public Matrix4x4 ProjInv, CamWorld, Cascade0, Cascade1, Cascade2, Cascade3;

    public Vector4 Splits, LightDir, LightColor, CamPos;

    public float Exposure;

    public uint CsmEnabled;

    public uint HasEnvironment;

    public float AmbientIntensity;

    public Vector4 EnvironmentSh0, EnvironmentSh1, EnvironmentSh2, EnvironmentSh3, EnvironmentSh4,
        EnvironmentSh5, EnvironmentSh6, EnvironmentSh7, EnvironmentSh8;

    public float SkyboxExposure;

    public uint EncodeSrgb;

    public Vector2 Padding;
}
public sealed class SceneRenderer : IDisposable
{
    private readonly SdlGpuContext _context;

    private readonly bool _encodeSrgb;

    private readonly IntPtr _gbufferPipeline, _doubleSidedPipeline, _compositePipeline;

    private readonly CsmShadowSystem _shadows;

    private readonly GpuTexture _white;

    private GpuTexture? _depth, _normal, _albedo;

    public SceneRenderer(SdlGpuContext ctx)
    {
        _context = ctx;
        var swapchainFormat = GetGPUSwapchainTextureFormat(ctx.Device, ctx.Window);
        _encodeSrgb = swapchainFormat is not (GPUTextureFormat.R8G8B8A8UnormSRGB or GPUTextureFormat.B8G8R8A8UnormSRGB);
        var shaders = new ShaderManager(ctx);
        _gbufferPipeline = shaders.CreatePipeline("gbuffer.vert", "gbuffer.frag", [GPUTextureFormat.R16G16B16A16Float, GPUTextureFormat.R8G8B8A8Unorm], true, cull: true);
        _doubleSidedPipeline = shaders.CreatePipeline("gbuffer.vert", "gbuffer.frag", [GPUTextureFormat.R16G16B16A16Float, GPUTextureFormat.R8G8B8A8Unorm], true);
        _compositePipeline = shaders.CreatePipeline("direct_csm_composite.vert", "direct_csm_composite.frag", [GetGPUSwapchainTextureFormat(ctx.Device, ctx.Window)], false, mesh: false);
        _shadows = new CsmShadowSystem(ctx, shaders);
        _white = new GpuTexture(ctx, 1, 1, GPUTextureFormat.R8G8B8A8Unorm, GPUTextureUsageFlags.Sampler);
        _white.UploadLayer<byte>([255, 255, 255, 255], 4);
    }

    public void PrepareMaterials(SceneGraph scene)
    {
        foreach (var material in scene.Meshes
                     .SelectMany(m => m.Materials)
                     .Distinct())
        {
            if (material.GpuDiffuseTexture != null || material.DiffuseMap?.Rgba8Data == null)
            {
                continue;
            }
            var data = material.DiffuseMap;
            var texture = new GpuTexture(_context, data.Width, data.Height, GPUTextureFormat.R8G8B8A8UnormSRGB,
                GPUTextureUsageFlags.Sampler, filter: GPUFilter.Linear, addressMode: GPUSamplerAddressMode.Repeat);
            try
            {
                texture.UploadLayer(data.Rgba8Data, 4);
                material.GpuDiffuseTexture = texture;
            }
            catch
            {
                texture.Dispose();
                throw;
            }
        }
    }

    private void Resize(int width, int height)
    {
        if (_depth?.Width == width && _depth.Height == height)
        {
            return;
        }
        _depth?.Dispose();
        _normal?.Dispose();
        _albedo?.Dispose();
        _depth = new(_context, width, height, GPUTextureFormat.D32Float, GPUTextureUsageFlags.DepthStencilTarget | GPUTextureUsageFlags.Sampler);
        _normal = new(_context, width, height, GPUTextureFormat.R16G16B16A16Float, GPUTextureUsageFlags.ColorTarget | GPUTextureUsageFlags.Sampler);
        _albedo = new(_context, width, height, GPUTextureFormat.R8G8B8A8Unorm, GPUTextureUsageFlags.ColorTarget | GPUTextureUsageFlags.Sampler);
    }

    public void Draw(SceneGraph scene, Camera camera, float exposure = 1f, float ambientIntensity = 1f, float skyboxExposure = 1f)
    {
        PrepareMaterials(scene);
        var command = SdlGpuContext.Check(AcquireGPUCommandBuffer(_context.Device));
        if (!WaitAndAcquireGPUSwapchainTexture(command, _context.Window, out var swapchain, out uint width, out uint height))
        {
            CancelGPUCommandBuffer(command);
            throw new InvalidOperationException(GetError());
        }
        if (swapchain == IntPtr.Zero)
        {
            SdlGpuContext.Check(SubmitGPUCommandBuffer(command));
            return;
        }

        Resize((int)width, (int)height);
        camera.Aspect = (float)width / height;
        camera.UpdateMatrices();

        _shadows.Execute(command, scene, camera);
        DrawGBuffer(command, scene, camera);
        DrawComposite(command, swapchain, scene, camera, exposure, ambientIntensity, skyboxExposure);
        SdlGpuContext.Check(SubmitGPUCommandBuffer(command));
    }

    private unsafe void DrawGBuffer(IntPtr command, SceneGraph scene, Camera camera)
    {
        GPUColorTargetInfo* targets = stackalloc GPUColorTargetInfo[2];
        targets[0] = ColorTarget(_normal!.Handle);
        targets[1] = ColorTarget(_albedo!.Handle);
        var depth = new GPUDepthStencilTargetInfo
        {
            Texture = _depth!.Handle,
            ClearDepth = 1,
            LoadOp = GPULoadOp.Clear,
            StoreOp = GPUStoreOp.Store,
            StencilLoadOp = GPULoadOp.DontCare,
            StencilStoreOp = GPUStoreOp.DontCare
        };
        var pass = SdlGpuContext.Check(BeginGPURenderPass(command, (IntPtr)targets, 2, in depth));
        foreach (var instance in scene.Meshes)
        {
            if (!instance.Visible || instance.GpuBuffers == null)
            {
                continue;
            }
            ShaderManager.BindMesh(pass, instance.GpuBuffers);
            Matrix4x4.Invert(instance.WorldMatrix, out var inverse);
            foreach (var group in instance.Mesh.Groups)
            {
                var material = instance.Materials[group.MaterialIndex];
                BindGPUGraphicsPipeline(pass, material.DoubleSided ? _doubleSidedPipeline : _gbufferPipeline);
                var uniforms = new GBufferUniforms
                {
                    ViewProj = camera.ViewProjectionMatrix,
                    Model = instance.WorldMatrix,
                    NormalMat = Matrix4x4.Transpose(inverse),
                    BaseColor = new(material.BaseColorLinear, 1),
                    HasTexture = material.GpuDiffuseTexture != null ? 1u : 0u,
                    Roughness = material.Roughness,
                    Metalness = material.Metalness
                };
                ShaderManager.PushVertex(command, uniforms);
                ShaderManager.PushFragment(command, uniforms);
                BindGPUFragmentSamplers(pass, 0,
                    new[] { (material.GpuDiffuseTexture ?? _white).Binding }, 1);
                DrawGPUIndexedPrimitives(pass, (uint)group.IndexCount, 1, (uint)group.StartIndex, 0, 0);
            }
        }
        EndGPURenderPass(pass);
    }

    private unsafe void DrawComposite(
        IntPtr command,
        IntPtr swapchain,
        SceneGraph scene,
        Camera camera,
        float exposure,
        float ambientIntensity,
        float skyboxExposure)
    {
        var output = ColorTarget(swapchain);
        var pass = SdlGpuContext.Check(BeginGPURenderPass(command, (IntPtr)(&output), 1, IntPtr.Zero));
        BindGPUGraphicsPipeline(pass, _compositePipeline);
        var light = scene.DirLight;
        ShaderManager.PushFragment(command, new CompositeUniforms
        {
            ProjInv = camera.ProjectionMatrixInverse,
            CamWorld = camera.CameraWorldMatrix,
            Cascade0 = _shadows.CascadeViewProj[0],
            Cascade1 = _shadows.CascadeViewProj[1],
            Cascade2 = _shadows.CascadeViewProj[2],
            Cascade3 = _shadows.CascadeViewProj[3],
            Splits = _shadows.CascadeSplits,
            LightDir = new(light.GetLightDirectionToSource(), 0),
            LightColor = new(light.Color * light.Intensity, 1),
            CamPos = new(camera.Position, 1),
            Exposure = exposure,
            CsmEnabled = light.CastShadow ? 1u : 0u,
            HasEnvironment = scene.EnvironmentTexture != null ? 1u : 0u,
            AmbientIntensity = MathF.Max(0, ambientIntensity),
            EnvironmentSh0 = scene.EnvironmentDiffuseSh[0],
            EnvironmentSh1 = scene.EnvironmentDiffuseSh[1],
            EnvironmentSh2 = scene.EnvironmentDiffuseSh[2],
            EnvironmentSh3 = scene.EnvironmentDiffuseSh[3],
            EnvironmentSh4 = scene.EnvironmentDiffuseSh[4],
            EnvironmentSh5 = scene.EnvironmentDiffuseSh[5],
            EnvironmentSh6 = scene.EnvironmentDiffuseSh[6],
            EnvironmentSh7 = scene.EnvironmentDiffuseSh[7],
            EnvironmentSh8 = scene.EnvironmentDiffuseSh[8],
            SkyboxExposure = MathF.Max(0, skyboxExposure),
            EncodeSrgb = _encodeSrgb ? 1u : 0u
        });
        var samplers = new[]
        {
            _depth!.Binding,
            _normal!.Binding,
            _albedo!.Binding,
            _shadows.ShadowMapArray.Binding,
            (scene.EnvironmentTexture ?? _white).Binding
        };
        BindGPUFragmentSamplers(pass, 0, samplers, (uint)samplers.Length);
        DrawGPUPrimitives(pass, 3, 1, 0, 0);
        EndGPURenderPass(pass);
    }

    private static GPUColorTargetInfo ColorTarget(IntPtr texture) => new()
    {
        Texture = texture,
        ClearColor = new()
        {
            A = 1
        },
        LoadOp = GPULoadOp.Clear,
        StoreOp = GPUStoreOp.Store
    };

    public void Dispose()
    {
        WaitForGPUIdle(_context.Device);
        _depth?.Dispose();
        _normal?.Dispose();
        _albedo?.Dispose();
        _white.Dispose();
        _shadows.Dispose();
        ReleaseGPUGraphicsPipeline(_context.Device, _gbufferPipeline);
        ReleaseGPUGraphicsPipeline(_context.Device, _doubleSidedPipeline);
        ReleaseGPUGraphicsPipeline(_context.Device, _compositePipeline);
    }
}
