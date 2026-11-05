cbuffer GBufferUniforms : register(b0, space1) {
    column_major float4x4 viewProj;
    column_major float4x4 model;
    column_major float4x4 normalMat;
    float4 baseColor;
    uint hasTexture;
    float roughness;
    float metalness;
    float _pad0;
};

struct VertexInput {
    float3 position : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float2 uv : TEXCOORD2;
};

struct VertexOutput {
    float4 position : SV_Position;
    float3 worldNormal : TEXCOORD0;
    float2 uv : TEXCOORD1;
};

VertexOutput main(VertexInput input) {
    VertexOutput output;
    float4 worldPos = mul(model, float4(input.position, 1.0));
    output.position = mul(viewProj, worldPos);
    output.worldNormal = normalize(mul(normalMat, float4(input.normal, 0.0)).xyz);
    output.uv = input.uv;
    return output;
}
