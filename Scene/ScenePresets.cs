using System.Numerics;
using SponzaTest.Assets;
using SponzaTest.Graphics;
using SponzaTest.Graphics.Lightning;

namespace SponzaTest.Scene;

public class ScenePreset
{
    public Vector3 CameraPosition { get; set; } = new(2f, 5f, 5f);
    public Vector3 CameraTarget { get; set; } = Vector3.Zero;
    public Action<SceneGraph, AssetLoader, LightConfig> Populate { get; set; } = null!;
    
    public static readonly ScenePreset Sponza = new ScenePreset
    {
        CameraPosition = new Vector3(4.2f, 0.5f, -0.5f),
        CameraTarget = new Vector3(-1.6f, 2.8f, -0.6f),
        Populate = (scene, loader, light) =>
        {
            light.Intensity = 10f;
            light.AzimuthDeg = -7.7f;
            light.ElevationDeg = 74.9f;
                
            var environment = loader.LoadEnvironmentTexture("Textures/qwantani_noon_puresky_1k.exr");
            scene.EnvironmentTexture = environment.Gpu;
            scene.EnvironmentDiffuseSh = EnvironmentLighting.ProjectDiffuse(environment.Cpu);
                
            var whiteMaterial = new MaterialData { Name = "BeastWhite", BaseColorLinear = MaterialData.SrgbHexToLinear(0x999999) };
            Matrix4x4 beastXform =
                Matrix4x4.CreateScale(2f) *
                Matrix4x4.CreateRotationY(MathF.PI * 0.5f) *
                Matrix4x4.CreateTranslation(0f, 1.63f + 0.2f, 0f);

            scene.Meshes.AddRange(loader.LoadGltf("Models/inferno-beast-from-space-from-jurafjvs-cc0-2.glb", beastXform, whiteMaterial));
            scene.Meshes.AddRange(loader.LoadGltf("Models/sponza.glb", Matrix4x4.Identity));

            scene.DirLight.ShadowMapSize = 2048;
            scene.DirLight.ShadowNear = 0.1f;
            scene.DirLight.ShadowFar = 200f;
            scene.DirLight.OrthoExtent = 5f;
            scene.DirLight.ShadowBias = -0.0002f;
            scene.DirLight.UseCsm = true;
            scene.DirLight.CsmMaxFar = 50f;
            scene.DirLight.CsmLightMargin = 15f;
        }
    };
}


