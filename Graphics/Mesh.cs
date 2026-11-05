using System.Numerics;
using System.Runtime.InteropServices;
using SDL3;

namespace SponzaTest.Graphics;

public class CpuMesh
{
    public string Name { get; set; } = "";
    private MeshVertex[] _vertices = [];
    private BoundingBox? _bounds;

    public MeshVertex[] Vertices
    {
        get => _vertices;
        set
        {
            _vertices = value;
            InvalidateBounds();
        }
    }

    public BoundingBox Bounds => _bounds ??= BoundingBox.FromVertices(_vertices);

    // Call after editing vertex positions in place, before re-uploading the mesh.
    public void InvalidateBounds() => _bounds = null;
    public uint[] Indices { get; set; } = Array.Empty<uint>();
    public List<SubMeshGroup> Groups { get; } = new();
}
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct MeshVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 UV;

    public MeshVertex(Vector3 position, Vector3 normal, Vector2 uv)
    {
        Position = position;
        Normal = normal;
        UV = uv;
    }
}
public struct SubMeshGroup
{
    public int StartIndex;
    public int IndexCount;
    public int MaterialIndex;
}

public class GpuMeshBuffers : IDisposable
{
    public GpuBuffer<MeshVertex> VertexBuffer { get; }
    public GpuBuffer<uint> IndexBuffer { get; }
    public int IndexCount { get; }

    public GpuMeshBuffers(SdlGpuContext ctx, CpuMesh mesh)
    {
        VertexBuffer = new GpuBuffer<MeshVertex>(ctx, mesh.Vertices, SDL3.SDL.GPUBufferUsageFlags.Vertex, $"{mesh.Name}_VB");
        IndexBuffer = new GpuBuffer<uint>(ctx, mesh.Indices, SDL3.SDL.GPUBufferUsageFlags.Index, $"{mesh.Name}_IB");
        IndexCount = mesh.Indices.Length;
    }

    public void Dispose()
    {
        VertexBuffer.Dispose();
        IndexBuffer.Dispose();
    }
}
public sealed class GpuBuffer<T> : IDisposable where T : unmanaged
{
    private readonly SdlGpuContext _ctx;
    public IntPtr Handle { get; private set; }
    public uint SizeInBytes { get; }
    public GpuBuffer(SdlGpuContext ctx, T[] data, SDL.GPUBufferUsageFlags usage, string label)
    {
        _ctx = ctx;
        SizeInBytes = checked((uint)MemoryMarshal.AsBytes(data.AsSpan()).Length);
        var info = new SDL.GPUBufferCreateInfo { Usage = usage, Size = SizeInBytes };
        Handle = SdlGpuContext.Check(SDL.CreateGPUBuffer(ctx.Device, in info));
        try { ctx.Upload(MemoryMarshal.AsBytes(data.AsSpan()), (pass, transfer) => {
            var source = new SDL.GPUTransferBufferLocation { TransferBuffer = transfer };
            var dest = new SDL.GPUBufferRegion { Buffer = Handle, Size = SizeInBytes };
            SDL.UploadToGPUBuffer(pass, in source, in dest, false);
        }); } catch { Dispose(); throw; }
    }
    public void Dispose() { if (Handle != IntPtr.Zero) SDL.ReleaseGPUBuffer(_ctx.Device, Handle); Handle = IntPtr.Zero; }
}
[StructLayout(LayoutKind.Sequential)]
public struct GBufferUniforms
{
    public Matrix4x4 ViewProj, Model, NormalMat;
    public Vector4 BaseColor;
    public uint HasTexture;
    public float Roughness, Metalness, Padding;
}

public class SceneMeshInstance
{
    public CpuMesh Mesh { get; set; } = null!;
    public GpuMeshBuffers? GpuBuffers { get; set; }
    public VersionedCollection<MaterialData> Materials { get; } = new();
    private Matrix4x4 _worldMatrix = Matrix4x4.Identity;
    private Matrix4x4? _normalMatrix;

    public Matrix4x4 WorldMatrix
    {
        get => _worldMatrix;
        set
        {
            if (_worldMatrix == value) return;
            _worldMatrix = value;
            _normalMatrix = null;
        }
    }

    public Matrix4x4 NormalMatrix
    {
        get
        {
            if (_normalMatrix is not { } normal)
            {
                Matrix4x4.Invert(_worldMatrix, out var inverse);
                normal = Matrix4x4.Transpose(inverse);
                _normalMatrix = normal;
            }
            return normal;
        }
    }
    public bool Visible { get; set; } = true;
    public bool CastShadow { get; set; } = true;
}
public class MaterialData
{
    public string Uuid { get; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Default";
    public Vector3 BaseColorLinear { get; set; } = Vector3.One;
    public float Roughness { get; set; } = 0.8f;
    public float Metalness { get; set; } = 0.0f;
    public bool DoubleSided { get; set; }
    internal static long TextureRevision { get; private set; }
    private CpuTexture? _diffuseMap;
    private GpuTexture? _gpuDiffuseTexture;

    public CpuTexture? DiffuseMap
    {
        get => _diffuseMap;
        set
        {
            if (ReferenceEquals(_diffuseMap, value)) return;
            _diffuseMap = value;
            _gpuDiffuseTexture?.Dispose();
            _gpuDiffuseTexture = null;
            TextureRevision++;
        }
    }

    public GpuTexture? GpuDiffuseTexture
    {
        get => _gpuDiffuseTexture;
        set
        {
            _gpuDiffuseTexture = value;
            TextureRevision++;
        }
    }

    public static Vector3 SrgbHexToLinear(uint hex)
    {
        float r = ((hex >> 16) & 0xFF) / 255f;
        float g = ((hex >> 8) & 0xFF) / 255f;
        float b = (hex & 0xFF) / 255f;
        return new Vector3(SrgbToLinear(r), SrgbToLinear(g), SrgbToLinear(b));
    }

    public static float SrgbToLinear(float c)
    {
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }
}