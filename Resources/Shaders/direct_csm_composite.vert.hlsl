struct VertexOutput {
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

// Draw three vertices, starting at vertex 0, without a vertex buffer.
VertexOutput main(uint vid : SV_VertexID) {
    VertexOutput output;
    float x = float((vid << 1u) & 2u);
    float y = float(vid & 2u);
    output.position = float4(x * 2.0 - 1.0, 1.0 - y * 2.0, 0.0, 1.0);
    output.uv = float2(x, y);
    return output;
}
