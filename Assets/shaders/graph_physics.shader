HEADER
{
    Description = "High-Performance Atomic Multi-Pass Graph Physics Kernel";
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

    int u_PassMode < Attribute("PassMode");
    > ; // 0: Clear, 1: Springs, 2: Integrate
    uint u_NumNodes < Attribute("NumNodes");
    > ;
    uint u_NumEdges < Attribute("NumEdges");
    > ;

    float u_DeltaTime < Attribute("DeltaTime");
    > ;
    float u_Alpha < Attribute("Alpha");
    > ;
    float u_RepulsionStrength < Attribute("RepulsionStrength");
    > ;
    float u_RepelMaxDist < Attribute("RepelMaxDist");
    > ;
    float u_LinkDistance < Attribute("LinkDistance");
    > ;
    float u_LinkForce < Attribute("LinkForce");
    > ;
    float u_CenterForce < Attribute("CenterForce");
    > ;
    float u_Damping < Attribute("Damping");
    > ;
    float u_TerminalSpeed < Attribute("TerminalSpeed");
    > ;
    float u_NodeSizeScale < Attribute("NodeSizeScale");
    > ;

    int u_DraggedNodeId < Attribute("DraggedNodeId");
    > ;
    float2 u_DragTargetPos < Attribute("DragTargetPos");
    > ;

    StructuredBuffer<NodePhysicsData> InNodes < Attribute("InNodes");
    > ;
    StructuredBuffer<EdgePhysicsData> InEdges < Attribute("InEdges");
    > ;
    RWStructuredBuffer<int> AccumForces < Attribute("AccumForces");
    > ; // 2 ints per node (fx, fy)
    RWStructuredBuffer<NodePhysicsData> OutNodes < Attribute("OutNodes");
    > ;
}

CS
{
    [numthreads(64, 1, 1)]
    void MainCs(uint3 id: SV_DispatchThreadID)
    {
        uint threadIdx = id.x;

        // =========================================================================
        // PASS 0: ZERO OUT ACCUMULATION BUFFER (1 Thread per Node)
        // =========================================================================
        if (u_PassMode == 0)
        {
            if (threadIdx >= u_NumNodes)
                return;
            AccumForces[threadIdx * 2 + 0] = 0;
            AccumForces[threadIdx * 2 + 1] = 0;
            return;
        }

        // =========================================================================
        // PASS 1: ATOMIC SPRINGS EVALUATION (1 Thread per Edge -> O(E) Scalability!)
        // =========================================================================
        if (u_PassMode == 1)
        {
            if (threadIdx >= u_NumEdges)
                return;

            EdgePhysicsData edge = InEdges[threadIdx];
            if (edge.SourceIndex == edge.TargetIndex)
                return;

            NodePhysicsData src = InNodes[edge.SourceIndex];
            NodePhysicsData dst = InNodes[edge.TargetIndex];

            // Ignore hidden nodes
            if ((src.Flags & (1 << 1)) != 0 || (dst.Flags & (1 << 1)) != 0)
                return;

            float2 delta = dst.Position - src.Position;
            float dist = length(delta);
            if (dist < 0.001)
                return;

            float maxDeg = max((float)src.TotalDegree, (float)dst.TotalDegree);
            float degreeDamping = 1.0 / sqrt(max(1.0, maxDeg * 0.15));

            float combinedRadii = (src.Radius + dst.Radius) * u_NodeSizeScale;
            float targetDistance = u_LinkDistance + (combinedRadii * 0.85);
            float displacement = dist - targetDistance;

            bool isPinned = (src.Flags & (1 << 0)) != 0 || (dst.Flags & (1 << 0)) != 0;
            bool isDragged = ((int)edge.SourceIndex == u_DraggedNodeId || (int)edge.TargetIndex == u_DraggedNodeId);

            float springCoeff = (isPinned || isDragged) ? 0.70 : (0.26 * degreeDamping);
            float strength = (u_LinkForce * springCoeff) * u_Alpha;

            float2 springForce = (delta / dist) * (displacement * strength);

            // Convert to fixed-point integer representation for atomics
            int2 iForce = (int2)(springForce * FIXED_SCALE);

            // Atomic accumulation on both endpoints in parallel
            InterlockedAdd(AccumForces[edge.SourceIndex * 2 + 0], iForce.x);
            InterlockedAdd(AccumForces[edge.SourceIndex * 2 + 1], iForce.y);

            InterlockedAdd(AccumForces[edge.TargetIndex * 2 + 0], -iForce.x);
            InterlockedAdd(AccumForces[edge.TargetIndex * 2 + 1], -iForce.y);
            return;
        }

        // =========================================================================
        // PASS 2: REPULSION, GRAVITY & INTEGRATION (1 Thread per Node)
        // =========================================================================
        if (u_PassMode == 2)
        {
            if (threadIdx >= u_NumNodes)
                return;

            NodePhysicsData node = InNodes[threadIdx];

            bool isPinned = (node.Flags & (1 << 0)) != 0;
            bool isHidden = (node.Flags & (1 << 1)) != 0;
            bool isDragged = ((int)threadIdx == u_DraggedNodeId);

            if (isHidden)
            {
                OutNodes[threadIdx] = node;
                return;
            }

            // Direct hardware mouse drag pinning
            if (isDragged)
            {
                node.Position = u_DragTargetPos;
                node.Velocity = float2(0.0, 0.0);
                OutNodes[threadIdx] = node;
                return;
            }

            if (isPinned)
            {
                node.Velocity = float2(0.0, 0.0);
                OutNodes[threadIdx] = node;
                return;
            }

            // 1. Unpack accumulated spring forces
            int fx = AccumForces[threadIdx * 2 + 0];
            int fy = AccumForces[threadIdx * 2 + 1];
            float2 totalForce = float2((float)fx, (float)fy) / FIXED_SCALE;

            float2 myPos = node.Position;
            float myMass = max(0.5, node.Mass);

            // 2. Fast N-Body Coulomb Repulsion
            float scaledRepelDist = u_RepelMaxDist * max(1.0, u_NodeSizeScale * 0.85);
            float repelDistSq = scaledRepelDist * scaledRepelDist;
            float repelPower = u_RepulsionStrength * 12.0;

            for (uint j = 0; j < u_NumNodes; j++)
            {
                if (j == threadIdx)
                    continue;

                NodePhysicsData other = InNodes[j];
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
                totalForce += (delta / dist) * (forceMag * (u_Alpha * 0.016));
            }

            // 3. Center Gravity Pull
            float2 centerPull = -myPos * (u_CenterForce * 0.002) * u_Alpha;

            // 4. Semi-Implicit Euler Integration & Damping
            node.Velocity = (node.Velocity + totalForce + centerPull) * u_Damping;

            // Terminal speed clamp
            float speed = length(node.Velocity);
            if (speed > u_TerminalSpeed)
            {
                node.Velocity = (node.Velocity / speed) * u_TerminalSpeed;
            }

            node.Position += node.Velocity;
            OutNodes[threadIdx] = node;
        }
    }
}
