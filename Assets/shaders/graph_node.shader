HEADER
{
    Description = "GPU Instanced Multi-Shape SDF Node Shader";
    Version = 1;
}

FEATURES
{
#include "common/features.hlsl"
}

MODES
{
    Default();
    Forward();
}

COMMON
{
#include "common/shared.hlsl"
}

struct VertexInput
{
#include "common/vertexinput.hlsl"
    uint nInstanceId : SV_InstanceID;
};

struct PixelInput
{
#include "common/pixelinput.hlsl"
    float4 vColor : COLOR0;
    float4 vNodeParams : TEXCOORD8; // x: ShapeID, y: IsHovered, z: IsSelected, w: IsDimmed
    float2 vLocalPos : TEXCOORD9;   // Local Quad coordinates [-1..1]
};

VS
{
#include "common/vertex.hlsl"

    Texture2D g_tColors < Attribute("g_tColors");
    > ;

    PixelInput MainVs(VertexInput i)
    {
        PixelInput o = ProcessVertex(i);

        // 1. Texture color & flags
        uint texWidth = 512;
        int3 texCoord = int3((int)(i.nInstanceId % texWidth), (int)(i.nInstanceId / texWidth), 0);
        float4 rawTexel = g_tColors.Load(texCoord);

        o.vColor = float4(rawTexel.rgb, 1.0);

        uint packedByte = (uint)(rawTexel.a * 255.0 + 0.5);
        float shapeId = (float)(packedByte & 0x0F);
        float isHovered = (packedByte & (1 << 4)) ? 1.0 : 0.0;
        float isSelected = (packedByte & (1 << 5)) ? 1.0 : 0.0;
        float isDimmed = (packedByte & (1 << 6)) ? 1.0 : 0.0;

        o.vNodeParams = float4(shapeId, isHovered, isSelected, isDimmed);
        o.vLocalPos = (i.vTexCoord.xy * 2.0) - 1.0;

        return FinalizeVertex(o);
    }
}

PS
{
#include "common/pixel.hlsl"

    RenderState(BlendEnable, true);
    RenderState(SrcBlend, SRC_ALPHA);
    RenderState(DstBlend, INV_SRC_ALPHA);
    RenderState(DepthWriteEnable, false);
    RenderState(CullMode, NONE);

    float SdCircle(float2 p, float r)
    {
        return length(p) - r;
    }
    float SdRoundedBox(float2 p, float2 b, float r)
    {
        float2 q = abs(p) - b + r;
        return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
    }
    float SdHexagon(float2 p, float r)
    {
        const float3 k = float3(-0.866025404, 0.5, 0.577350269);
        p = abs(p);
        p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
        p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
        return length(p) * sign(p.y);
    }
    float SdDiamond(float2 p, float r)
    {
        return (abs(p.x) + abs(p.y)) - r;
    }
    float SdRing(float2 p, float r, float th)
    {
        return abs(length(p) - r) - th;
    }

    float4 MainPs(PixelInput i) : SV_Target0
    {
        float2 p = i.vLocalPos;
        int shape = (int)(i.vNodeParams.x + 0.5);
        float isHovered = i.vNodeParams.y;
        float isSelected = i.vNodeParams.z;
        float isDimmed = i.vNodeParams.w;

        float dist = 0.0;
        if (shape == 1 || shape == 2)
            dist = SdRoundedBox(p, float2(0.70, 0.70), 0.22);
        else if (shape == 3)
            dist = SdHexagon(p, 0.82);
        else if (shape == 4)
            dist = SdDiamond(p, 0.88);
        else if (shape == 5)
            dist = SdRing(p, 0.68, 0.18);
        else
            dist = SdCircle(p, 0.78);

        float aa = fwidth(dist);
        float alpha = 1.0 - smoothstep(-aa, aa, dist);
        if (alpha <= 0.01)
            discard;

        float3 baseColor = i.vColor.rgb;
        if (isDimmed > 0.5)
            baseColor *= 0.22;

        float isHighlight = max(isHovered, isSelected);
        float3 glowColor = isSelected > 0.5 ? float3(0.2, 0.7, 1.0) : float3(1.0, 0.85, 0.2);

        float outline = 1.0 - smoothstep(0.0, aa * 3.5, abs(dist + 0.04));
        float coreShade = clamp(1.0 - length(p) * 0.35, 0.75, 1.0);
        float3 finalRgb = baseColor * coreShade;

        if (isHighlight > 0.5)
            finalRgb = lerp(finalRgb, glowColor * 1.8, outline);

        return float4(finalRgb, alpha);
    }
}
