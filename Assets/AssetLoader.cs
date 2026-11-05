using System.Numerics;
using SponzaTest.Graphics;
using SponzaTest.Scene;
using static SDL3.SDL;

namespace SponzaTest.Assets;

public class AssetLoader : IDisposable
{
    private readonly SdlGpuContext _ctx;
    public string PublicRootDir { get; }
    private readonly Dictionary<string, (CpuTexture Cpu, GpuTexture Gpu)> _envCache = new();

    public AssetLoader(SdlGpuContext ctx, string? publicRootDir = null)
    {
        _ctx = ctx;
        PublicRootDir = publicRootDir ?? FindPublicDirectory();
    }
    internal static string FindPublicDirectory()
    {
        string packaged = Path.Combine(AppContext.BaseDirectory, "Resources");
        if (Directory.Exists(packaged)) return packaged;
        string? curr = Directory.GetCurrentDirectory();
        while (curr != null)
        {
            string candidate = Path.Combine(curr, "Resources");
            if (Directory.Exists(candidate)) return candidate;
            curr = Directory.GetParent(curr)?.FullName;
        }
        return Path.Combine(AppContext.BaseDirectory, "Resources");
    }

    public string ResolveAssetPath(string relativePath)
    {
        string normalized = relativePath.TrimStart('/', '\\');
        return Path.Combine(PublicRootDir, normalized);
    }

    public (CpuTexture Cpu, GpuTexture Gpu) LoadEnvironmentTexture(string relativePath)
    {
        if (_envCache.TryGetValue(relativePath, out var cached))
            return cached;

        string fullPath = ResolveAssetPath(relativePath);

        CpuTexture cpuData = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".exr" => ExrReader.Load(fullPath),
            _ => throw new NotSupportedException($"Unsupported environment format: {fullPath}")
        };

        uint mipCount = (uint)Math.Floor(Math.Log2(Math.Max(cpuData.Width, cpuData.Height))) + 1u;

        var gpuTex = new GpuTexture(
            _ctx,
            cpuData.Width,
            cpuData.Height,
            GPUTextureFormat.R16G16B16A16Float,
            GPUTextureUsageFlags.Sampler,
            mipLevelCount: mipCount,
            filter: GPUFilter.Linear,
            addressMode: GPUSamplerAddressMode.Repeat,
            label: cpuData.Name);

        float[] currentLevel = cpuData.Rgba32FloatData!;
        int mipW = cpuData.Width;
        int mipH = cpuData.Height;

        for (uint mip = 0; mip < mipCount; mip++)
        {
            ushort[] halfData = new ushort[mipW * mipH * 4];
            for (int i = 0; i < halfData.Length; i++)
            {
                float v = Math.Clamp(currentLevel[i], -65500f, 65500f);
                halfData[i] = BitConverter.HalfToUInt16Bits((Half)v);
            }
            gpuTex.UploadLayer<ushort>(halfData, 8, 0, mip, mipW, mipH);

            if (mip + 1 < mipCount)
            {
                int nextW = Math.Max(1, mipW / 2);
                int nextH = Math.Max(1, mipH / 2);
                float[] nextLevel = new float[nextW * nextH * 4];
                for (int y = 0; y < nextH; y++)
                {
                    for (int x = 0; x < nextW; x++)
                    {
                        int x0 = Math.Min(mipW - 1, x * 2);
                        int x1 = Math.Min(mipW - 1, x * 2 + 1);
                        int y0 = Math.Min(mipH - 1, y * 2);
                        int y1 = Math.Min(mipH - 1, y * 2 + 1);

                        int i00 = (y0 * mipW + x0) * 4;
                        int i10 = (y0 * mipW + x1) * 4;
                        int i01 = (y1 * mipW + x0) * 4;
                        int i11 = (y1 * mipW + x1) * 4;
                        int dst = (y * nextW + x) * 4;

                        for (int c = 0; c < 4; c++)
                        {
                            nextLevel[dst + c] = 0.25f * (
                                currentLevel[i00 + c] +
                                currentLevel[i10 + c] +
                                currentLevel[i01 + c] +
                                currentLevel[i11 + c]);
                        }
                    }
                }
                currentLevel = nextLevel;
                mipW = nextW;
                mipH = nextH;
            }
        }

        var bundle = (cpuData, gpuTex);
        _envCache[relativePath] = bundle;
        return bundle;
    }
    public List<SceneMeshInstance> LoadGltf(string relativePath, Matrix4x4 rootTransform, MaterialData? overrideMaterial = null)
    {
        string fullPath = ResolveAssetPath(relativePath);
        var parsedMeshes = GltfReader.ParseGltfFile(fullPath, rootTransform, overrideMaterial);
        var instances = new List<SceneMeshInstance>(parsedMeshes.Count);

        foreach (var (cpuMesh, materials, worldMatrix) in parsedMeshes)
        {
            var inst = new SceneMeshInstance
            {
                Mesh = cpuMesh,
                GpuBuffers = new GpuMeshBuffers(_ctx, cpuMesh),
                WorldMatrix = worldMatrix
            };
            inst.Materials.AddRange(materials);
            instances.Add(inst);
        }

        return instances;
    }
    
    public SceneMeshInstance CreateBoxInstance(Vector3 size, MaterialData mat, Matrix4x4 worldMatrix)
    {
        float hx = size.X * 0.5f;
        float hy = size.Y * 0.5f;
        float hz = size.Z * 0.5f;

        MeshVertex[] verts =
        {
            // +Z
            new(new(-hx, -hy,  hz), new(0, 0, 1), new(0, 0)),
            new(new( hx, -hy,  hz), new(0, 0, 1), new(1, 0)),
            new(new( hx,  hy,  hz), new(0, 0, 1), new(1, 1)),
            new(new(-hx,  hy,  hz), new(0, 0, 1), new(0, 1)),
            // -Z
            new(new( hx, -hy, -hz), new(0, 0, -1), new(0, 0)),
            new(new(-hx, -hy, -hz), new(0, 0, -1), new(1, 0)),
            new(new(-hx,  hy, -hz), new(0, 0, -1), new(1, 1)),
            new(new( hx,  hy, -hz), new(0, 0, -1), new(0, 1)),
            // +Y
            new(new(-hx,  hy,  hz), new(0, 1, 0), new(0, 0)),
            new(new( hx,  hy,  hz), new(0, 1, 0), new(1, 0)),
            new(new( hx,  hy, -hz), new(0, 1, 0), new(1, 1)),
            new(new(-hx,  hy, -hz), new(0, 1, 0), new(0, 1)),
            // -Y
            new(new(-hx, -hy, -hz), new(0, -1, 0), new(0, 0)),
            new(new( hx, -hy, -hz), new(0, -1, 0), new(1, 0)),
            new(new( hx, -hy,  hz), new(0, -1, 0), new(1, 1)),
            new(new(-hx, -hy,  hz), new(0, -1, 0), new(0, 1)),
            // +X
            new(new( hx, -hy,  hz), new(1, 0, 0), new(0, 0)),
            new(new( hx, -hy, -hz), new(1, 0, 0), new(1, 0)),
            new(new( hx,  hy, -hz), new(1, 0, 0), new(1, 1)),
            new(new( hx,  hy,  hz), new(1, 0, 0), new(0, 1)),
            // -X
            new(new(-hx, -hy, -hz), new(-1, 0, 0), new(0, 0)),
            new(new(-hx, -hy,  hz), new(-1, 0, 0), new(1, 0)),
            new(new(-hx,  hy,  hz), new(-1, 0, 0), new(1, 1)),
            new(new(-hx,  hy, -hz), new(-1, 0, 0), new(0, 1)),
        };

        uint[] idx = new uint[36];
        for (uint f = 0; f < 6; f++)
        {
            uint b = f * 4;
            idx[f * 6 + 0] = b + 0;
            idx[f * 6 + 1] = b + 1;
            idx[f * 6 + 2] = b + 2;
            idx[f * 6 + 3] = b + 0;
            idx[f * 6 + 4] = b + 2;
            idx[f * 6 + 5] = b + 3;
        }

        var mesh = new CpuMesh { Name = "Box", Vertices = verts, Indices = idx };
        mesh.Groups.Add(new SubMeshGroup { StartIndex = 0, IndexCount = 36, MaterialIndex = 0 });

        var inst = new SceneMeshInstance
        {
            Mesh = mesh,
            GpuBuffers = new GpuMeshBuffers(_ctx, mesh),
            WorldMatrix = worldMatrix
        };
        inst.Materials.Add(mat);
        return inst;
    }
    public SceneMeshInstance CreatePlaneInstance(float width, float height, MaterialData mat, Matrix4x4 worldMatrix)
    {
        float hw = width * 0.5f;
        float hh = height * 0.5f;

        MeshVertex[] verts =
        {
            new(new(-hw, -hh, 0f), new(0, 0, 1), new(0, 0)),
            new(new( hw, -hh, 0f), new(0, 0, 1), new(1, 0)),
            new(new( hw,  hh, 0f), new(0, 0, 1), new(1, 1)),
            new(new(-hw,  hh, 0f), new(0, 0, 1), new(0, 1)),
        };
        uint[] idx = { 0, 1, 2, 0, 2, 3 };

        var mesh = new CpuMesh { Name = "Plane", Vertices = verts, Indices = idx };
        mesh.Groups.Add(new SubMeshGroup { StartIndex = 0, IndexCount = 6, MaterialIndex = 0 });

        var inst = new SceneMeshInstance
        {
            Mesh = mesh,
            GpuBuffers = new GpuMeshBuffers(_ctx, mesh),
            WorldMatrix = worldMatrix
        };
        inst.Materials.Add(mat);
        return inst;
    }
    
    public void Dispose()
    {
        foreach (var entry in _envCache.Values) 
            entry.Gpu.Dispose();
        
        _envCache.Clear();
    }
}
