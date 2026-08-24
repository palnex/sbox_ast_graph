HEADER
{
    Description = "Isolated Widget Billboard Text Shader";
    Version = 1;
}

MODES
{
    Default();
    Forward();
}

COMMON
{
#include "common/shared.hlsl"

    struct NodeLabelGpuData
    {
        float3 vWorldPosition;
        float flNodeRadiusPx;
        float4 vScreenRect; // x: Width, y: Height
        float4 vAtlasUv;    // x: uMin, y: vMin, z: uMax, w: vMax
        float4 vColor;
    };

    StructuredBuffer<NodeLabelGpuData> g_InstanceBuffer < Attribute("InstanceBuffer");
    > ;
    Texture2D g_tAtlas < Attribute("AtlasTexture");
    > ;
    SamplerState g_sBilinear < Filter(BILINEAR);
    AddressU(CLAMP);
    AddressV(CLAMP);
    > ;

    // Explicit Widget Camera Uniforms (100% Isolated from Main Viewport)
    float4x4 g_matWidgetViewProj < Attribute("g_matWidgetViewProj");
    > ;
    float3 g_vWidgetCamRight < Attribute("g_vWidgetCamRight");
    > ;
    float3 g_vWidgetCamUp < Attribute("g_vWidgetCamUp");
    > ;
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
};

VS
{
#include "common/vertex.hlsl"

    PixelInput MainVs(VertexInput i)
    {
        PixelInput o = (PixelInput)0;
        NodeLabelGpuData node = g_InstanceBuffer[i.nInstanceId];

        float3 nodeCenterWs = node.vWorldPosition;
        float nodeRadius = node.flNodeRadiusPx;
        float labelWidth = node.vScreenRect.x;
        float labelHeight = node.vScreenRect.y;

        // Quad Offsets: x in [-0.5..0.5], y in [0..-1] (Top-anchored below node)
        float deltaX = i.vPositionOs.x * labelWidth;
        float deltaY = -(nodeRadius + 4.0f) + (i.vPositionOs.y * labelHeight);

        // Billboard expansion using Widget's own Camera Right & Up vectors
        float3 billboardPosWs = nodeCenterWs + (g_vWidgetCamRight * deltaX) + (g_vWidgetCamUp * deltaY);

        // Project directly using Widget's View-Projection matrix
        o.vPositionPs = mul(g_matWidgetViewProj, float4(billboardPosWs, 1.0f));
        o.vPositionWs = billboardPosWs;

        // Non-inverted UV mapping
        float2 quadUv = saturate(float2(i.vPositionOs.x + 0.5f, -i.vPositionOs.y));
        o.vTextureCoords.xy = lerp(node.vAtlasUv.xy, node.vAtlasUv.zw, quadUv);
        o.vColor = node.vColor;

        return o;
    }
}

PS
{
#include "common/pixel.hlsl"

    RenderState(BlendEnable, true);
    RenderState(SrcBlend, SRC_ALPHA);
    RenderState(DstBlend, INV_SRC_ALPHA);
    RenderState(DepthEnable, false);
    RenderState(DepthWriteEnable, false);
    RenderState(CullMode, NONE);

    float4 MainPs(PixelInput i) : SV_Target0
    {
        float4 texColor = g_tAtlas.Sample(g_sBilinear, i.vTextureCoords.xy);

        if (texColor.a < 0.02f)
            discard;

        return float4(i.vColor.rgb * texColor.rgb, i.vColor.a * texColor.a);
    }
}
