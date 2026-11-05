using System.Numerics;
using SponzaTest.Scene;

using static SDL3.SDL;

namespace SponzaTest.Graphics.Lightning;

public unsafe class CsmShadowSystem : IDisposable
{
    public const int CascadeCount = 4;

    private readonly SdlGpuContext _ctx;
    public GpuTexture ShadowMapArray { get; private set; }
    private readonly IntPtr _shadowPipeline;
    private readonly Dictionary<CpuMesh, (Vector3 Min, Vector3 Max)> _meshBounds = new();
    private (float Near, float Far) CasterDepth(SceneGraph scene, Matrix4x4 lightView)
    {
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        foreach (var inst in scene.Meshes) {
            if (!inst.Visible || !inst.CastShadow) continue;
            if (!_meshBounds.TryGetValue(inst.Mesh, out var bounds)) {
                Vector3 lo = new(float.PositiveInfinity), hi = new(float.NegativeInfinity);
                foreach (var v in inst.Mesh.Vertices) { lo = Vector3.Min(lo, v.Position); hi = Vector3.Max(hi, v.Position); }
                bounds = (lo, hi); _meshBounds[inst.Mesh] = bounds;
            }
            var transform = inst.WorldMatrix * lightView;
            for (int i=0;i<8;i++) {
                Vector3 corner = new((i&1)==0?bounds.Min.X:bounds.Max.X, (i&2)==0?bounds.Min.Y:bounds.Max.Y, (i&4)==0?bounds.Min.Z:bounds.Max.Z);
                float z = Vector3.Transform(corner, transform).Z;
                minZ = MathF.Min(minZ,z); maxZ = MathF.Max(maxZ,z);
            }
        }
        return float.IsFinite(minZ) ? (-maxZ - 1f, MathF.Max(-maxZ, -minZ) + 1f) : (0.1f,150f);
    }

    public Matrix4x4[] CascadeViewProj { get; } = new Matrix4x4[CascadeCount];
    public Vector4 CascadeSplits { get; private set; }

    public CsmShadowSystem(SdlGpuContext ctx, ShaderManager shaders, int mapSize = 2048)
    {
        _ctx = ctx;
        ShadowMapArray = CreateMap(mapSize);
        _shadowPipeline = shaders.CreatePipeline("shadow.vert", null, [], true);
    }

    private GpuTexture CreateMap(int size) => new(_ctx, size, size, GPUTextureFormat.D32Float,
        GPUTextureUsageFlags.DepthStencilTarget | GPUTextureUsageFlags.Sampler,
        depthOrArrayLayers: CascadeCount, array: true, label: "CSM");

    public void UpdateCascades(Camera camera, DirectionalLightData light, SceneGraph scene)
    {
        Vector3 lightDir = light.GetLightDirectionToSource();
        Vector3 up = MathF.Abs(lightDir.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;

        if (!light.UseCsm)
        {
            float ext = light.OrthoExtent;
            Matrix4x4 lightView = Matrix4x4.CreateLookAt(light.Target + lightDir * 50f, light.Target, up);
            var depth = CasterDepth(scene, lightView);
            Matrix4x4 lightProj = Camera.CreateOrthographicOffCenterZeroToOne(-ext, ext, -ext, ext, depth.Near, depth.Far);
            Matrix4x4 vp = lightView * lightProj;
            for (int c = 0; c < CascadeCount; c++)
                CascadeViewProj[c] = vp;
            CascadeSplits = new Vector4(1e20f);
            return;
        }

        float near = camera.Near;
        float maxFar = MathF.Max(near + 1f, light.CsmMaxFar);
        const float lambda = 0.5f;

        Span<float> splits = stackalloc float[CascadeCount + 1];
        splits[0] = near;
        for (int i = 1; i <= CascadeCount; i++)
        {
            float p = (float)i / CascadeCount;
            float logSplit = near * MathF.Pow(maxFar / near, p);
            float uniSplit = near + (maxFar - near) * p;
            splits[i] = lambda * logSplit + (1f - lambda) * uniSplit;
        }
        CascadeSplits = new Vector4(splits[1], splits[2], splits[3], splits[4]);

        float fovRad = camera.FovDegrees * (MathF.PI / 180f);
        float tanHalfFovY = MathF.Tan(fovRad * 0.5f);
        float tanHalfFovX = tanHalfFovY * camera.Aspect;
        Span<Vector3> frustumCorners = stackalloc Vector3[8];

        for (int c = 0; c < CascadeCount; c++)
        {
            float cNear = c == 0 ? splits[c] : splits[c] - (splits[c] - splits[c - 1]) * 0.1f;
            float cFar = splits[c + 1];

            frustumCorners[0] = new(-tanHalfFovX * cNear,  tanHalfFovY * cNear, -cNear);
            frustumCorners[1] = new( tanHalfFovX * cNear,  tanHalfFovY * cNear, -cNear);
            frustumCorners[2] = new( tanHalfFovX * cNear, -tanHalfFovY * cNear, -cNear);
            frustumCorners[3] = new(-tanHalfFovX * cNear, -tanHalfFovY * cNear, -cNear);
            frustumCorners[4] = new(-tanHalfFovX * cFar,   tanHalfFovY * cFar,  -cFar);
            frustumCorners[5] = new( tanHalfFovX * cFar,   tanHalfFovY * cFar,  -cFar);
            frustumCorners[6] = new( tanHalfFovX * cFar,  -tanHalfFovY * cFar,  -cFar);
            frustumCorners[7] = new(-tanHalfFovX * cFar,  -tanHalfFovY * cFar,  -cFar);

            Vector3 center = Vector3.Zero;
            for (int i = 0; i < 8; i++)
            {
                frustumCorners[i] = Vector3.Transform(frustumCorners[i], camera.CameraWorldMatrix);
                center += frustumCorners[i];
            }
            center /= 8f;

            float radius = 0f;
            for (int i = 0; i < 8; i++)
                radius = MathF.Max(radius, (frustumCorners[i] - center).Length());
            radius = MathF.Ceiling(radius * 16f) / 16f;

            float margin = MathF.Max(30f, light.CsmLightMargin);
            Matrix4x4 lightView = Matrix4x4.CreateLookAt(center + lightDir * (radius + margin), center, up);
            var depth = CasterDepth(scene, lightView);
            Matrix4x4 lightProj = Camera.CreateOrthographicOffCenterZeroToOne(
                -radius, radius, -radius, radius, depth.Near, depth.Far);

            // Snap the projection translation in light space to a shadow texel.
            Matrix4x4 vp = lightView * lightProj;
            Vector3 origin = Vector3.Transform(Vector3.Zero, vp);
            float halfMap = ShadowMapArray.Width * 0.5f;
            vp.M41 += (MathF.Round(origin.X * halfMap) - origin.X * halfMap) / halfMap;
            vp.M42 += (MathF.Round(origin.Y * halfMap) - origin.Y * halfMap) / halfMap;
            CascadeViewProj[c] = vp;
        }
    }

    public void Execute(IntPtr command, SceneGraph scene, Camera camera)
    {
        int size = Math.Clamp(scene.DirLight.ShadowMapSize, 512, 4096);
        if (ShadowMapArray.Width != size)
        {
            var replacement = CreateMap(size);
            ShadowMapArray.Dispose();
            ShadowMapArray = replacement;
        }
        UpdateCascades(camera, scene.DirLight, scene);
        // Clear all layers, including inactive cascades, before sampling the array.
        for (uint c = 0; c < CascadeCount; c++)
        {
            var depth = new GPUDepthStencilTargetInfo { Texture = ShadowMapArray.Handle, Layer = (byte)c,
                ClearDepth = 1f, LoadOp = GPULoadOp.Clear, StoreOp = GPUStoreOp.Store,
                StencilLoadOp = GPULoadOp.DontCare, StencilStoreOp = GPUStoreOp.DontCare };
            var pass = SdlGpuContext.Check(BeginGPURenderPass(command, IntPtr.Zero, 0, in depth));
            BindGPUGraphicsPipeline(pass, _shadowPipeline);
            if (scene.DirLight.CastShadow && (scene.DirLight.UseCsm || c == 0))
            foreach (var inst in scene.Meshes)
            {
                if (!inst.Visible || !inst.CastShadow || inst.GpuBuffers == null) continue;
                ShaderManager.PushVertex(command, new GBufferUniforms { ViewProj = CascadeViewProj[c], Model = inst.WorldMatrix });
                ShaderManager.BindMesh(pass, inst.GpuBuffers);
                DrawGPUIndexedPrimitives(pass, (uint)inst.GpuBuffers.IndexCount, 1, 0, 0, 0);
            }
            EndGPURenderPass(pass);
        }
    }

    public void Dispose()
    {
        ReleaseGPUGraphicsPipeline(_ctx.Device, _shadowPipeline);
        ShadowMapArray.Dispose();
    }
}
