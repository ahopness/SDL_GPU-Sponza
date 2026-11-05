// Set viewProj to the current cascade light view-projection matrix.
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

float4 main(float3 position : TEXCOORD0) : SV_Position {
    return mul(viewProj, mul(model, float4(position, 1.0)));
}
