using System.Numerics;
using System.Runtime.InteropServices;
using SDL3;

namespace SponzaTest.Graphics;

public class CpuMesh
{
    public string Name { get; set; } = "";
    public MeshVertex[] Vertices { get; set; } = Array.Empty<MeshVertex>();
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
    public List<MaterialData> Materials { get; } = new();
    public Matrix4x4 WorldMatrix { get; set; } = Matrix4x4.Identity;
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
    public CpuTexture? DiffuseMap { get; set; }
    public GpuTexture? GpuDiffuseTexture { get; set; }

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