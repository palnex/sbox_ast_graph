HEADER
{
    Description = "Dynamic Multi-Pattern Ribbon Edge Shader";
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
    float4 vColor : COLOR0;
};

struct PixelInput
{
#include "common/pixelinput.hlsl"
    float4 vColor : COLOR0;
    float2 vEdgeParams : TEXCOORD8; // x: StyleID (0..4), y: Speed
};

VS
{
#include "common/vertex.hlsl"

    PixelInput MainVs(VertexInput i)
    {
        PixelInput o = ProcessVertex(i);
        o.vColor = i.vColor;
        // Read Style ID and Speed passed via Normal vector
        o.vEdgeParams = i.vNormalOs.xy;
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

    float4 MainPs(PixelInput i) : SV_Target0
    {
        float u = i.vTextureCoords.x; // 0.0 (Source) -> 1.0 (Target)
        float v = i.vTextureCoords.y; // 0.0 (Left) -> 1.0 (Right)

        float style = i.vEdgeParams.x; // 0: Solid, 1: Dashed, 2: Arrows, 3: Double, 4: Laser
        float speed = max(0.2, i.vEdgeParams.y);

        float4 baseColor = i.vColor;
        float crossDist = abs(v - 0.5) * 2.0; // 0.0 center, 1.0 border

        float finalAlpha = baseColor.a;
        float3 finalColor = baseColor.rgb;

        // ================= STYLE 1: DASHED (- - - -) =================
        if (style > 0.5 && style < 1.5)
        {
            float dash = frac(u * 16.0 - g_flTime * speed * 2.0);
            if (dash > 0.5)
                discard;
            finalAlpha = max(0.7, baseColor.a);
        }
        // ================= STYLE 2: CHEVRON ARROWS (> > > >) =================
        else if (style > 1.5 && style < 2.5)
        {
            float cell = frac(u * 6.0 - g_flTime * speed * 1.5);
            float chevron = cell - (crossDist * 0.45);
            float isArrow = (chevron > 0.0 && chevron < 0.30) ? 1.0 : 0.0;
            float isGuide = (crossDist < 0.20) ? 0.30 : 0.0;

            finalAlpha = max(isArrow * 0.95, isGuide * 0.35);
            if (finalAlpha <= 0.02)
                discard;

            if (isArrow > 0.5)
                finalColor = lerp(baseColor.rgb, float3(1.0, 1.0, 1.0), 0.55);
        }
        // ================= STYLE 3: DOUBLE LINE (= = = =) =================
        else if (style > 2.5 && style < 3.5)
        {
            if (crossDist < 0.35)
                discard; // вирізаємо центр
            finalAlpha = max(0.8, baseColor.a);
        }
        // ================= STYLE 4: LASER / EVENT PULSE =================
        else if (style > 3.5 && style < 4.5)
        {
            float pulsePos = frac(g_flTime * speed * 0.8);
            float distToPulse = abs(u - pulsePos);
            if (distToPulse > 0.5)
                distToPulse = 1.0 - distToPulse;
            float pulse = exp(-distToPulse * distToPulse * 90.0);

            finalColor += float3(0.5, 0.8, 1.0) * (pulse * 1.8);
            finalAlpha = max(baseColor.a * 0.4, pulse);
        }
        // ================= STYLE 0: SOLID GLOW LINE =================
        else
        {
            float core = 1.0 - smoothstep(0.0, 0.9, crossDist);
            finalAlpha = max(0.4, baseColor.a * core);
        }

        return float4(finalColor, finalAlpha);
    }
}
