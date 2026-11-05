static const float PI = 3.141592653589793238462;

cbuffer CompositeUniforms : register(b0, space3) {
    column_major float4x4 projInv;
    column_major float4x4 camWorld;
    column_major float4x4 csmLightViewProj0;
    column_major float4x4 csmLightViewProj1;
    column_major float4x4 csmLightViewProj2;
    column_major float4x4 csmLightViewProj3;
    float4 csmSplits;
    float4 lightDir;
    float4 lightColor;
    float4 camPos;
    float exposure;
    uint csmEnabled;
    uint hasEnvironment;
    float ambientIntensity;
    float4 environmentSh[9];
    float skyboxExposure;
    uint encodeSrgb;
    float2 _padding;
};

// SDL fragment sampler slots 0..3: use nearest filtering and clamp-to-edge.
// SampleLevel keeps depth textures in SDL sampled-texture bindings.
// Shadow comparisons are performed manually.
Texture2D<float> texDepth : register(t0, space2);
SamplerState texDepthSampler : register(s0, space2);
Texture2D<float4> texNormal : register(t1, space2);
SamplerState texNormalSampler : register(s1, space2);
Texture2D<float4> texAlbedo : register(t2, space2);
SamplerState texAlbedoSampler : register(s2, space2);
Texture2DArray<float> shadowMapArray : register(t3, space2);
SamplerState shadowSampler : register(s3, space2);

Texture2D<float4> environmentMap : register(t4, space2);
SamplerState environmentSampler : register(s4, space2);

struct FragmentInput {
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

float3 reconstruct_view_pos(float2 uv, float depth) {
    float4 ndc = float4(uv.x * 2.0 - 1.0, (1.0 - uv.y) * 2.0 - 1.0, depth, 1.0);
    float4 clip = mul(projInv, ndc);
    return clip.xyz / clip.w;
}

float4x4 shadowMatrix(int c) {
    switch (c) {
        case 0: { return csmLightViewProj0; }
        case 1: { return csmLightViewProj1; }
        case 2: { return csmLightViewProj2; }
        default: { return csmLightViewProj3; }
    }
}

float filterShadow(int c, float3 worldPos, float3 normal, float NdotL) {
    float4x4 m = shadowMatrix(c);
    uint width, height, layers;
    shadowMapArray.GetDimensions(width, height, layers);
    float2 dims = float2(width, height);
    // HLSL indexes matrix rows, including for column-major storage.
    // Orthographic matrix row norms give world units per shadow texel.
    float sx = length(m[0].xyz);
    float sy = length(m[1].xyz);
    float texelWorld = 2.0 / (max(sx, sy) * dims.x);
    float3 offset = normal * texelWorld * 0.35 * sqrt(max(0.0, 1.0 - NdotL * NdotL));
    float4 clip = mul(m, float4(worldPos + offset, 1.0));
    float3 p = clip.xyz / clip.w;
    float2 uv = float2(p.x * 0.5 + 0.5, 0.5 - p.y * 0.5);
    if (any(uv < float2(0.0, 0.0)) || any(uv > float2(1.0, 1.0)) || p.z < 0.0 || p.z > 1.0) { return 1.0; }
    float lightDepthScale = length(m[2].xyz);
    float bias = lightDepthScale * texelWorld * (0.08 + 0.25 * (1.0 - NdotL));
    // Project the receiver plane into shadow UV/depth space. Neighboring PCF taps
    // compare against their own plane depth rather than increasing a constant bias.
    float3 axisX = normalize(m[0].xyz);
    float3 axisY = normalize(m[1].xyz);
    float3 axisZ = normalize(m[2].xyz);
    float nz = dot(normal, axisZ);
    float safeNz = (nz >= 0.0 ? 1.0 : -1.0) * max(abs(nz), 0.1);
    float2 gradient = float2(-dot(normal, axisX)/safeNz*lightDepthScale/sx*2.0,
                         dot(normal, axisY)/safeNz*lightDepthScale/sy*2.0);
    float sum = 0.0;
    float total = 0.0;
    for (int y = -2; y <= 2; y++) {
        for (int x = -2; x <= 2; x++) {
            float w = float((3 - abs(x)) * (3 - abs(y)));
            int2 samplePx = clamp(int2(uv * dims) + int2(x, y), int2(0, 0), int2(dims) - int2(1, 1));
            float d = shadowMapArray.SampleLevel(shadowSampler,
                float3((float2(samplePx) + 0.5) / dims, float(c)), 0.0);
            float receiverDepth = p.z + dot(gradient, (float2(samplePx) + float2(0.5, 0.5)) / dims - uv);
            sum += w * (receiverDepth - bias <= d ? 1.0 : 0.0);
            total += w;
        }
    }
    return sum / total;
}

float sampleShadowCascade(float3 worldPos, float3 worldNormal, float viewDepth, float NdotL) {
    if (csmEnabled == 0u) { return 1.0; }
    int c = 0;
    if (viewDepth > csmSplits.x) { c = 1; }
    if (viewDepth > csmSplits.y) { c = 2; }
    if (viewDepth > csmSplits.z) { c = 3; }
    float visibility = filterShadow(c, worldPos, worldNormal, NdotL);
    if (c < 3) {
        float nearDepth = (c > 0 ? csmSplits[c - 1] : 0.0);
        float farDepth = csmSplits[uint(c)];
        float blend = smoothstep(lerp(nearDepth, farDepth, 0.9), farDepth, viewDepth);
        if (blend > 0.0) { return lerp(visibility, filterShadow(c + 1, worldPos, worldNormal, NdotL), blend); }
    }
    return visibility;
}

float3 reinhardToneMapping(float3 colorIn) {
    float3 color = max(colorIn * exposure, float3(0.0, 0.0, 0.0));
    return color / (float3(1.0, 1.0, 1.0) + color);
}

float3 diffuseEnvironment(float3 n) {
    if (hasEnvironment == 0u) return float3(0.0, 0.0, 0.0);
    // Matches the CPU projection basis; coefficients already include cosine/pi.
    float3 irradiance = environmentSh[0].rgb * 0.2820947918
        + environmentSh[1].rgb * (0.4886025119 * n.y)
        + environmentSh[2].rgb * (0.4886025119 * n.z)
        + environmentSh[3].rgb * (0.4886025119 * n.x)
        + environmentSh[4].rgb * (1.0925484306 * n.x * n.y)
        + environmentSh[5].rgb * (1.0925484306 * n.y * n.z)
        + environmentSh[6].rgb * (0.3153915653 * (3.0 * n.z * n.z - 1.0))
        + environmentSh[7].rgb * (1.0925484306 * n.x * n.z)
        + environmentSh[8].rgb * (0.5462742153 * (n.x * n.x - n.y * n.y));
    return max(irradiance, float3(0.0, 0.0, 0.0)) * ambientIntensity;
}

float3 shadePixel(float2 uv) {
    float depth = texDepth.SampleLevel(texDepthSampler, uv, 0.0);
    if (depth >= 1.0) {
        if (hasEnvironment == 0u) return float3(0.0, 0.0, 0.0);
        float3 viewDirection = normalize(reconstruct_view_pos(uv, 1.0));
        float3 direction = normalize(mul(camWorld, float4(viewDirection, 0.0)).xyz);
        float2 envUV = float2(atan2(direction.z, direction.x) / (2.0 * PI) + 0.5,
                             acos(clamp(direction.y, -1.0, 1.0)) / PI);
        return reinhardToneMapping(environmentMap.SampleLevel(environmentSampler, envUV, 0.0).rgb * skyboxExposure);
    }

    float3 viewPos = reconstruct_view_pos(uv, depth);
    float3 worldPos = mul(camWorld, float4(viewPos, 1.0)).xyz;

    float4 normRough = texNormal.SampleLevel(texNormalSampler, uv, 0.0);
    float3 N = normalize(normRough.xyz * 2.0 - 1.0);
    float roughness = clamp(normRough.w, 0.05, 1.0);
    float3 albedo = texAlbedo.SampleLevel(texAlbedoSampler, uv, 0.0).rgb;

    float3 L = normalize(lightDir.xyz);
    float3 V = normalize(camPos.xyz - worldPos);
    float3 H = normalize(L + V);

    float NdotL = max(0.0, dot(N, L));
    float NdotH = max(0.0, dot(N, H));
    float shadow = (NdotL > 0.0 ? sampleShadowCascade(worldPos, N, -viewPos.z, NdotL) : 0.0);

    // Lambertian diffuse and a subtle GGX-like specular lobe.
    float3 diff = albedo * (1.0 / PI);
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float denom = NdotH * NdotH * (alpha2 - 1.0) + 1.0;
    float D = alpha2 / max(1e-4, PI * denom * denom);
    float3 spec = float3(0.04, 0.04, 0.04) * min(D * 0.25, 2.0);

    float3 directLight = (diff + spec) * lightColor.rgb * NdotL * shadow;

    float3 ambientLight = albedo * diffuseEnvironment(N);
    return reinhardToneMapping(directLight + ambientLight);
}

float3 linearToSrgb(float3 color) {
    float3 low = color * 12.92;
    float3 high = 1.055 * pow(max(color, float3(0.0, 0.0, 0.0)), 1.0 / 2.4) - 0.055;
    return float3(color.r <= 0.0031308 ? low.r : high.r,
                  color.g <= 0.0031308 ? low.g : high.g,
                  color.b <= 0.0031308 ? low.b : high.b);
}

float4 main(FragmentInput input) : SV_Target0 {
    uint width, height;
    texDepth.GetDimensions(width, height);
    int2 dims = int2(width, height);
    int2 px = clamp(int2(input.uv * float2(dims)), int2(0, 0), dims - int2(1, 1));
    float3 color = shadePixel((float2(px) + 0.5) / float2(dims));
    // UNORM swapchains need display encoding; sRGB attachments do it in hardware.
    if (encodeSrgb != 0u) color = linearToSrgb(color);
    return float4(color, 1.0);
}
