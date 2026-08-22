#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Core;

/// <summary>
/// Ultra-stable Force-Directed physics solver with scale-adaptive springs, 
/// soft collision damping, and zero oscillation resonance.
/// </summary>
public sealed class SleepyPhysicsSolver
{
    private readonly BarnesHutQuadTree _quadTree = new();
    private readonly SpatialHashGrid _collisionGrid = new();

    public float Alpha { get; private set; } = 1.0f;
    public float AlphaTarget { get; set; } = 0.0f;
    public float AlphaDecay { get; set; } = 0.016f;
    public float AlphaMin { get; set; } = 0.001f;

    public float RepulsionConstant { get; set; } = 10.0f; // Default matches Obsidian 10-15
    public float RepulsionMaxDist { get; set; } = 2500f;  // Spacious multi-screen radius
    public float LinkDistanceSetting { get; set; } = 160f;
    public float LinkForceSetting { get; set; } = 1.0f;
    public float CenterForceSetting { get; set; } = 0.35f;

    public float Damping { get; set; } = 0.68f;
    public float TerminalVelocity { get; set; } = 15f;

    public bool PauseDuringPlay { get; set; } = true;
    public bool IsSleeping => Alpha < AlphaMin;

    public void Reheat( float energy = 1.0f ) => Alpha = Math.Max( Alpha, energy );
    public void WakeUp() => Reheat( 1.0f );

    public void Step( SpatialRegistry registry, IReadOnlyList<CanvasEdge> edges, float dt = 0.016f, float nodeSizeScale = 1.0f )
    {
        int nodeCount = registry.Count;
        if ( nodeCount == 0 || IsSleeping ) return;

        if ( PauseDuringPlay && Game.IsPlaying ) return;

        // 1. Cool down Alpha
        Alpha += (AlphaTarget - Alpha) * AlphaDecay;
        if ( Alpha < AlphaMin )
        {
            var spatials = registry.GetSpatialSpan();
            for ( int i = 0; i < nodeCount; i++ ) spatials[i].Velocity = Vector2.Zero;
            return;
        }

        // 2. Build Barnes-Hut QuadTree
        _quadTree.Build( registry );

        // 3. Compute Many-Body Repulsion (Barnes-Hut)
        float scaledRepelDist = RepulsionMaxDist * MathF.Max( 1.0f, nodeSizeScale * 0.75f );
        float repelPower = RepulsionConstant * 12.0f;

        Parallel.For( 0, nodeCount, i =>
        {
            ref var node = ref registry.GetSpatialRef( i );
            if ( node.IsPinned || node.IsHidden ) return;

            float targetMass = registry.GetPayload( i ).PhysicsMass;
            Vector2 repulsion = _quadTree.ComputeRepulsion( i, node.Position, targetMass, repelPower, maxDist: scaledRepelDist );
            node.Velocity += repulsion * (Alpha * 0.016f);
        } );

        // NOTE (Phase 5 Roadmap): Add relation-based link strength weighting (Edge Weighting by RelationKind)
        // e.g. Inherits = 1.0f, FieldReference = 0.4f, ParameterUsage = 0.05f to decouple primitive types like Vector3/Transform.

        // 4. Scale-Adaptive Link Springs (Harmonized with Node Radii to eliminate resonance)
        int edgeCount = edges.Count;
        for ( int i = 0; i < edgeCount; i++ )
        {
            var edge = edges[i];
            ref var src = ref registry.GetSpatialRef( edge.SourceIndex );
            ref var dst = ref registry.GetSpatialRef( edge.TargetIndex );

            if ( src.IsHidden || dst.IsHidden ) continue;

            Vector2 delta = dst.Position - src.Position;
            float dist = delta.Length;
            if ( dist < 0.001f ) continue;

            var srcPayload = registry.GetPayload( edge.SourceIndex );
            var dstPayload = registry.GetPayload( edge.TargetIndex );

            // Degree-Normalized damping: massive 500-child hubs won't form tight rings!
            float maxDeg = MathF.Max( srcPayload.TotalDegree, dstPayload.TotalDegree );
            float degreeDamping = 1.0f / MathF.Sqrt( MathF.Max( 1.0f, maxDeg * 0.15f ) );

            float combinedRadii = (src.Radius + dst.Radius) * nodeSizeScale;
            float targetDistance = LinkDistanceSetting + (combinedRadii * 0.85f);
            float displacement = dist - targetDistance;
            float invDist = 1.0f / dist;

            // Powerful drag elasticity: when dragging a node, pull its neighbors with full kinetic force!
            bool isBeingDragged = src.IsPinned || dst.IsPinned;
            float springCoeff = isBeingDragged ? 0.65f : (0.24f * degreeDamping);
            float strength = (LinkForceSetting * springCoeff) * Alpha;
            Vector2 springForce = delta * (invDist * displacement * strength);

            if ( src.IsPinned && !dst.IsPinned )
            {
                dst.Velocity -= springForce * 1.5f; // Extra responsive drag pull!
            }
            else if ( !src.IsPinned && dst.IsPinned )
            {
                src.Velocity += springForce * 1.5f;
            }
            else if ( !src.IsPinned && !dst.IsPinned )
            {
                src.Velocity += springForce * 0.5f;
                dst.Velocity -= springForce * 0.5f;
            }
        }

        // 5. Soft Velocity Collisions (With scale-adaptive grid)
        ApplySoftCollisions( registry, nodeSizeScale );

        // 6. Center Gravity, Viscous Damping & Position Update
        var finalSpatials = registry.GetSpatialSpan();
        float maxSpeedSq = TerminalVelocity * TerminalVelocity;

        for ( int i = 0; i < nodeCount; i++ )
        {
            ref var node = ref finalSpatials[i];
            if ( node.IsPinned || node.IsHidden )
            {
                node.Velocity = Vector2.Zero;
                continue;
            }

            Vector2 centerPull = -node.Position * (CenterForceSetting * 0.002f) * Alpha;
            node.Velocity = (node.Velocity + centerPull) * Damping;

            if ( node.Velocity.LengthSquared > maxSpeedSq )
                node.Velocity = node.Velocity.Normal * TerminalVelocity;

            node.Position += node.Velocity;
        }
    }

    private void ApplySoftCollisions( SpatialRegistry registry, float nodeSizeScale )
    {
        _collisionGrid.Build( registry, nodeSizeScale );
        int count = registry.Count;

        for ( int i = 0; i < count; i++ )
        {
            ref readonly var a = ref registry.GetSpatialRef( i );
            if ( a.IsHidden ) continue;

            Vector2 posA = a.Position;
            float radiusA = a.Radius * nodeSizeScale;
            bool isPinnedA = a.IsPinned;

            _collisionGrid.QueryNeighbors( posA, neighborIdx =>
            {
                if ( i >= neighborIdx ) return;

                ref var b = ref registry.GetSpatialRef( neighborIdx );
                if ( b.IsHidden ) return;

                float targetDist = radiusA + (b.Radius * nodeSizeScale) + (6.0f * nodeSizeScale);
                Vector2 delta = b.Position - posA;
                float distSq = delta.LengthSquared;

                if ( distSq < (targetDist * targetDist) && distSq > 0.0001f )
                {
                    float dist = MathF.Sqrt( distSq );
                    float overlap = (targetDist - dist);

                    // Strong non-linear separation for massive overlapping hubs
                    float pushStrength = (overlap > radiusA * 0.5f) ? 0.60f : 0.35f;
                    Vector2 pushImpulse = (delta / dist) * (overlap * pushStrength * Alpha);

                    if ( !isPinnedA ) registry.GetSpatialRef( i ).Velocity -= pushImpulse * 0.5f;
                    if ( !b.IsPinned ) b.Velocity += pushImpulse * 0.5f;
                }
            } );
        }
    }
}