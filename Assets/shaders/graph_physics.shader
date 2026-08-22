HEADER
{
    Description = "Atomic Multi-Pass Zero-Copy Graph Physics Kernel";
    Version = 1;
}

COMMON
{
#include "common/shared.hlsl"

    struct NodePhysicsData
    {
        float2 Position;
        float2 Velocity;
        float Radius;
        float Mass;
        uint Flags; // Bit 0: Pinned, Bit 1: Hidden
        uint TotalDegree;
    };

    struct EdgePhysicsData
    {
        uint SourceIndex;
        uint TargetIndex;
        float DesiredDistance;
        float SpringStrength;
    };

#define FIXED_SCALE 10000.0f

    float g_flDeltaTime < Attribute("DeltaTime");
    > ;
    float g_flAlpha < Attribute("Alpha");
    > ;
    float g_flRepelConstant < Attribute("RepelConstant");
    > ;
    float g_flRepelMaxDist < Attribute("RepelMaxDist");
    > ;
    float g_flLinkDistance < Attribute("LinkDistance");
    > ;
    float g_flLinkForce < Attribute("LinkForce");
    > ;
    float g_flCenterForce < Attribute("CenterForce");
    > ;
    float g_flDamping < Attribute("Damping");
    > ;
    float g_flTerminalSpeed < Attribute("TerminalSpeed");
    > ;
    float g_flNodeSizeScale < Attribute("NodeSizeScale");
    > ;
    uint g_nNodeCount < Attribute("NodeCount");
    > ;
    uint g_nEdgeCount < Attribute("EdgeCount");
    > ;

    float2 g_vDragPos < Attribute("DragPos");
    > ;
    int g_nDragIndex < Attribute("DragIndex");
    > ;

    StructuredBuffer<NodePhysicsData> NodesIn < Attribute("NodesIn");
    > ;
    StructuredBuffer<EdgePhysicsData> EdgesIn < Attribute("EdgesIn");
    > ;
    RWStructuredBuffer<int> ForceAccum < Attribute("ForceAccum");
    > ;
    RWStructuredBuffer<NodePhysicsData> NodesOut < Attribute("NodesOut");
    > ;
}

CS
{
    [numthreads(64, 1, 1)]
    void MainCs(uint3 id: SV_DispatchThreadID)
    {
        uint idx = id.x;
        if (idx >= g_nNodeCount)
            return;

        NodePhysicsData node = NodesIn[idx];

        bool isPinned = (node.Flags & (1 << 0)) != 0;
        bool isHidden = (node.Flags & (1 << 1)) != 0;
        bool isDragged = ((int)idx == g_nDragIndex);

        if (isHidden)
        {
            NodesOut[idx] = node;
            return;
        }

        // Direct hardware drag anchoring
        if (isDragged)
        {
            node.Position = g_vDragPos;
            node.Velocity = float2(0.0, 0.0);
            NodesOut[idx] = node;
            return;
        }

        if (isPinned)
        {
            node.Velocity = float2(0.0, 0.0);
            NodesOut[idx] = node;
            return;
        }

        float2 totalForce = float2(0.0, 0.0);
        float2 myPos = node.Position;
        float myMass = max(0.5, node.Mass);

        // ================= 1. N-BODY REPULSION =================
        float scaledRepelDist = g_flRepelMaxDist * max(1.0, g_flNodeSizeScale * 0.75);
        float repelDistSq = scaledRepelDist * scaledRepelDist;
        float repelPower = g_flRepelConstant * 12.0;

        for (uint j = 0; j < g_nNodeCount; j++)
        {
            if (j == idx)
                continue;

            NodePhysicsData other = NodesIn[j];
            if ((other.Flags & (1 << 1)) != 0)
                continue;

            float2 delta = myPos - other.Position;
            float distSq = dot(delta, delta);

            if (distSq > repelDistSq)
                continue;

            float minSoftDist = 64.0 + (myMass + other.Mass) * 4.0;
            distSq = max(distSq, minSoftDist);

            float dist = sqrt(distSq);
            float forceMag = (repelPower * 350.0 * other.Mass) / distSq;
            totalForce += (delta / dist) * (forceMag * (g_flAlpha * 0.016));
        }

        // ================= 2. SCALE-ADAPTIVE SPRINGS =================
        for (uint e = 0; e < g_nEdgeCount; e++)
        {
            EdgePhysicsData edge = EdgesIn[e];
            if (edge.SourceIndex != idx && edge.TargetIndex != idx)
                continue;

            uint otherIdx = (edge.SourceIndex == idx) ? edge.TargetIndex : edge.SourceIndex;
            NodePhysicsData other = NodesIn[otherIdx];
            if ((other.Flags & (1 << 1)) != 0)
                continue;

            float2 delta = other.Position - myPos;
            float dist = length(delta);
            if (dist < 0.001)
                continue;

            float maxDeg = max((float)node.TotalDegree, (float)other.TotalDegree);
            float degreeDamping = 1.0 / sqrt(max(1.0, maxDeg * 0.15));

            float combinedRadii = (node.Radius + other.Radius) * g_flNodeSizeScale;
            float targetDistance = g_flLinkDistance + (combinedRadii * 0.85);
            float displacement = dist - targetDistance;

            bool otherIsPinned = (other.Flags & (1 << 0)) != 0;
            bool otherIsDragged = ((int)otherIdx == g_nDragIndex);

            float springCoeff = (otherIsPinned || otherIsDragged) ? 0.65 : (0.24 * degreeDamping);
            float strength = (g_flLinkForce * springCoeff) * g_flAlpha;

            float2 springForce = (delta / dist) * (displacement * strength);

            if (otherIsPinned || otherIsDragged)
                totalForce += springForce * 1.5;
            else
                totalForce += springForce * 0.5;
        }

        // ================= 3. CENTER GRAVITY & INTEGRATION =================
        float2 centerPull = -myPos * (g_flCenterForce * 0.002) * g_flAlpha;

        node.Velocity += (totalForce + centerPull);
        node.Velocity *= g_flDamping;

        // Terminal velocity clamp
        float speed = length(node.Velocity);
        if (speed > g_flTerminalSpeed)
        {
            node.Velocity = (node.Velocity / speed) * g_flTerminalSpeed;
        }

        node.Position += node.Velocity;
        NodesOut[idx] = node;
    }
}
