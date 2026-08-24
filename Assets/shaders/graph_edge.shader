HEADER
{
    Description = "Dynamic Multi-Pattern GPU Ribbon Edge Shader with Continuous Time";
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

    // Real-time continuous clock driven by CPU RealTime.Now
    float g_flCustomTime < Attribute("g_flCustomTime");
    > ;
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
    float2 vEdgeParams : TEXCOORD8; // x: Style ID, y: Flow Speed
};

VS
{
#include "common/vertex.hlsl"

    PixelInput MainVs(VertexInput i)
    {
        PixelInput o = ProcessVertex(i);
        o.vColor = i.vColor;
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
        float4 baseColor = i.vColor;
        float u = i.vTextureCoords.x;
        float v = i.vTextureCoords.y;
        float style = i.vEdgeParams.x;
        float speed = i.vEdgeParams.y;

        float time = g_flCustomTime;

        // =========================================================================
        // 1. FAST-PATH: SOLID LINES (Style == 0)
        // =========================================================================
        if (style < 0.5)
        {
            // Simple edge anti-aliasing on borders
            float edgeDist = abs(v - 0.5) * 2.0;
            float aa = fwidth(edgeDist);
            float alpha = 1.0 - smoothstep(1.0 - aa * 2.0, 1.0, edgeDist);
            return float4(baseColor.rgb, baseColor.a * alpha);
        }

        // =========================================================================
        // 2. ANIMATED PATTERNS
        // =========================================================================
        float crossDist = abs(v - 0.5) * 2.0;
        float finalAlpha = baseColor.a;
        float3 finalColor = baseColor.rgb;

        // STYLE 1: DASHED ( - - - - )
        if (style > 0.5 && style < 1.5)
        {
            float dash = frac(u * 12.0 - time * speed * 1.5);
            if (dash > 0.55)
                discard;

            float aa = fwidth(crossDist);
            finalAlpha *= (1.0 - smoothstep(0.85, 1.0, crossDist));
        }
        // STYLE 2: DIRECTIONAL CHEVRONS ( > > > > )
        else if (style > 1.5 && style < 2.5)
        {
            float cell = frac(u * 8.0 - time * speed * 1.2);
            float chevron = cell - (crossDist * 0.40);

            float isArrow = (chevron > 0.0 && chevron < 0.35) ? 1.0 : 0.0;
            float isGuide = (crossDist < 0.25) ? 0.35 : 0.0;

            finalAlpha *= max(isArrow, isGuide);
            if (finalAlpha <= 0.01)
                discard;

            if (isArrow > 0.5)
                finalColor = lerp(finalColor, float3(1.0, 1.0, 1.0), 0.65);
        }
        // STYLE 3: DOUBLE RAIL ( = = = = )
        else if (style > 2.5 && style < 3.5)
        {
            if (crossDist < 0.35)
                discard;
        }
        // STYLE 4: LASER ENERGY PULSE / PHOTON BURST
        else if (style > 3.5 && style < 4.5)
        {
            float pulsePos = frac(time * speed * 0.6);
            float distToPulse = abs(u - pulsePos);
            if (distToPulse > 0.5)
                distToPulse = 1.0 - distToPulse;

            float pulse = exp(-distToPulse * distToPulse * 120.0);

            finalColor += float3(0.4, 0.8, 1.0) * (pulse * 2.5);
            finalAlpha = max(baseColor.a * 0.35, pulse * 0.95);
        }

        return float4(finalColor, finalAlpha);
    }
}
