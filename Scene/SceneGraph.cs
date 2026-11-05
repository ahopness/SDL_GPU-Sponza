using System.Numerics;
using SponzaTest.Graphics;

namespace SponzaTest.Scene;

public class SceneGraph : IDisposable
{
    public List<SceneMeshInstance> Meshes { get; } = new();
    public DirectionalLightData DirLight { get; set; } = new();
    public GpuTexture? EnvironmentTexture { get; set; }
    public Vector4[] EnvironmentDiffuseSh { get; set; } = new Vector4[9];

    public void Dispose()
    {
        foreach (var buffers in Meshes
                     .Select(m => m.GpuBuffers)
                     .OfType<GpuMeshBuffers>()
                     .Distinct())
            buffers.Dispose();
        
        foreach (var mat in Meshes
                     .SelectMany(m => m.Materials)
                     .Distinct())
        {
            mat.GpuDiffuseTexture?.Dispose();
            mat.GpuDiffuseTexture = null;
        }
        
        Meshes.Clear();
    }
}
