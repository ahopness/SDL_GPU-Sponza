Texture2D<float4> albedoTex : register(t0, space2);
SamplerState albedoSampler : register(s0, space2);

cbuffer GBufferUniforms : register(b0, space3) {
    column_major float4x4 viewProj;
    column_major float4x4 model;
    column_major float4x4 normalMat;
    float4 baseColor;
    uint hasTexture;
    float roughness;
    float metalness;
    float _pad0;
};

struct FragmentInput {
    float4 position : SV_Position;
    float3 worldNormal : TEXCOORD0;
    float2 uv : TEXCOORD1;
};

struct FragmentOutput {
    float4 normal : SV_Target0;
    float4 albedo : SV_Target1;
};

FragmentOutput main(FragmentInput input, bool isFront : SV_IsFrontFace) {
    FragmentOutput output;
    float3 n = normalize(input.worldNormal);
    if (!isFront) {
        n = -n;
    }
    output.normal = float4(n * 0.5 + 0.5, roughness);
    float3 color = baseColor.rgb;
    if (hasTexture != 0u) {
        color *= albedoTex.Sample(albedoSampler, float2(input.uv.x, 1.0 - input.uv.y)).rgb;
    }
    output.albedo = float4(color, 1.0);
    return output;
}
