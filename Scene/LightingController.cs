using System.Numerics;

namespace SponzaTest.Scene;

public class LightConfig
{
    public float AzimuthDeg = 35f;
    public float ElevationDeg = 50f;
    public float Intensity = 1.0f;
    public float Exposure = 1.0f;
    public float SkyboxExposure = 1.0f;
    public float AmbientIntensity = 0.1f;
    public float Speed = 0.5f;

    public LightConfig Clone() => new()
    {
        AzimuthDeg = AzimuthDeg,
        ElevationDeg = ElevationDeg,
        Intensity = Intensity,
        Exposure = Exposure,
        SkyboxExposure = SkyboxExposure,
        AmbientIntensity = AmbientIntensity,
        Speed = Speed
    };
}

public class LightingController
{
    public LightConfig Config { get; } = new();
    private readonly LightConfig _defaults = new();
    private DirectionalLightData _light;

    public LightingController(DirectionalLightData initialLight)
    {
        _light = initialLight;
        UpdateLightFromAngles();
    }

    public void SetLight(DirectionalLightData light)
    {
        _light = light;
        UpdateLightFromAngles();
    }
    
    public void UpdateLightFromAngles()
    {
        float az = Config.AzimuthDeg * (MathF.PI / 180f);
        float el = Config.ElevationDeg * (MathF.PI / 180f);
        const float r = 40f;
        float x = r * MathF.Cos(el) * MathF.Cos(az);
        float y = r * MathF.Sin(el);
        float z = r * MathF.Cos(el) * MathF.Sin(az);

        _light.Position = new Vector3(x, y, z);
        _light.Intensity = Config.Intensity;
    }
}
public class DirectionalLightData
{
    public Vector3 Color { get; set; } = Vector3.One;
    public float Intensity { get; set; } = 4.0f;
    public Vector3 Position { get; set; } = new(10f, 30f, 10f);
    public Vector3 Target { get; set; } = Vector3.Zero;
    public bool CastShadow { get; set; } = true;
    public int ShadowMapSize { get; set; } = 4096;
    public float ShadowBias { get; set; } = -0.0002f;
    public float ShadowNear { get; set; } = 0.1f;
    public float ShadowFar { get; set; } = 100f;
    public float OrthoExtent { get; set; } = 10f;
    public bool UseCsm { get; set; }
    public float CsmMaxFar { get; set; } = 50f;
    public float CsmLightMargin { get; set; } = 15f;

    public Vector3 GetLightDirectionToSource()
    {
        Vector3 d = Position - Target;
        return d.LengthSquared() > 1e-6f ? Vector3.Normalize(d) : Vector3.UnitY;
    }
}
