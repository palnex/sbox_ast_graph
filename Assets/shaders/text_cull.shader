HEADER
{
    Description = "GPU Compute Shader for Text Culling and Label Layout";
    Version = 1;
}

COMMON
{
#include "common/shared.hlsl"

    struct NodeTextData
    {
        float3 PositionWs;
        float Scale;
        float4 Color;
        float4 OutlineColor;
        float4 AtlasUvRect;
    };

    struct GlyphInstanceData
    {
        float3 WorldPos;
        float Scale;
        float4 Color;
        float4 OutlineColor;
        float4 AtlasUvRect;
    };

    int u_NodeCount < Attribute("NodeCount");
    > ;
    float u_MaxDistance < Attribute("MaxDistance");
    > ;

    StructuredBuffer<NodeTextData> InNodes < Attribute("InNodes");
    > ;
    RWStructuredBuffer<GlyphInstanceData> OutDrawData < Attribute("OutDrawData");
    > ;
}

CS
{
    [numthreads(64, 1, 1)]
    void MainCs(uint3 id: SV_DispatchThreadID)
    {
        uint threadIdx = id.x;
        if (threadIdx >= (uint)u_NodeCount)
            return;

        NodeTextData node = InNodes[threadIdx];

        // 1. Check if node is culled by scale
        if (node.Scale <= 0.0001f)
        {
            OutDrawData[threadIdx].WorldPos = node.PositionWs;
            OutDrawData[threadIdx].Scale = 0.0f;
            OutDrawData[threadIdx].Color = float4(0, 0, 0, 0);
            OutDrawData[threadIdx].OutlineColor = float4(0, 0, 0, 0);
            OutDrawData[threadIdx].AtlasUvRect = float4(0, 0, 0, 0);
            return;
        }

        // 2. Frustum culling via Clip Space projection
        float4 clipPos = mul(g_matWorldToProjection, float4(node.PositionWs, 1.0f));
        bool visible = true;

        if (clipPos.w <= 0.1f)
        {
            visible = false;
        }
        else
        {
            float2 ndc = clipPos.xy / clipPos.w;
            if (abs(ndc.x) > 1.35f || abs(ndc.y) > 1.35f)
            {
                visible = false;
            }
        }

        // 3. Max view distance culling
        float3 toCam = g_vCameraPositionWs - node.PositionWs;
        if (dot(toCam, toCam) > (u_MaxDistance * u_MaxDistance))
        {
            visible = false;
        }

        // 4. Output instances
        if (visible)
        {
            OutDrawData[threadIdx].WorldPos = node.PositionWs;
            OutDrawData[threadIdx].Scale = node.Scale;
            OutDrawData[threadIdx].Color = node.Color;
            OutDrawData[threadIdx].OutlineColor = node.OutlineColor;
            OutDrawData[threadIdx].AtlasUvRect = node.AtlasUvRect;
        }
        else
        {
            OutDrawData[threadIdx].WorldPos = node.PositionWs;
            OutDrawData[threadIdx].Scale = 0.0f;
            OutDrawData[threadIdx].Color = float4(0, 0, 0, 0);
            OutDrawData[threadIdx].OutlineColor = float4(0, 0, 0, 0);
            OutDrawData[threadIdx].AtlasUvRect = float4(0, 0, 0, 0);
        }
    }
}
