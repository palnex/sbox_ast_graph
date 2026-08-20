#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Core;

/// <summary>
/// Ultra-stable Force-Directed physics solver with Soft Velocity Collisions, 
/// High Viscosity Damping, and LinLog Subsystem Separation.
/// </summary>
public sealed class SleepyPhysicsSolver
{
    private readonly BarnesHutQuadTree _quadTree = new();
    private readonly SpatialHashGrid _collisionGrid = new( cellSize: 70f );

    public float Alpha { get; private set; } = 1.0f;
    public float AlphaTarget { get; set; } = 0.0f;
    public float AlphaDecay { get; set; } = 0.016f;
    public float AlphaMin { get; set; } = 0.001f;

    public float RepulsionConstant { get; set; } = 1400f;
    public float RepulsionMaxDist { get; set; } = 550f;
    public float LinkDistanceSetting { get; set; } = 200f;
    public float LinkForceSetting { get; set; } = 0.85f;
    public float CenterForceSetting { get; set; } = 0.35f;

    public float Damping { get; set; } = 0.68f;
    public float TerminalVelocity { get; set; } = 14f;
    public float BaseCollisionRadius { get; set; } = 14f;

    public bool PauseDuringPlay { get; set; } = true;
    public bool IsSleeping => Alpha < AlphaMin;

    public void Reheat( float energy = 1.0f ) => Alpha = Math.Max( Alpha, energy );
    public void WakeUp() => Reheat( 1.0f );

    public void Step( SpatialRegistry registry, IReadOnlyList<CanvasEdge> edges, float dt = 0.016f )
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

        // 3. Compute Many-Body Repulsion in Parallel
        Parallel.For( 0, nodeCount, i =>
        {
            ref var node = ref registry.GetSpatialRef( i );
            if ( node.IsPinned || node.IsHidden ) return;

            Vector2 repulsion = _quadTree.ComputeRepulsion( i, node.Position, RepulsionConstant, maxDist: RepulsionMaxDist );
            float mass = MathF.Max( 0.5f, registry.GetPayload( i ).PhysicsMass );
            node.Velocity += (repulsion * Alpha / mass) * dt;
        } );

        // 4. LinLog Link Constraints (Soft Logarithmic Attraction)
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

            float minDeg = MathF.Min( srcPayload.TotalDegree, dstPayload.TotalDegree );
            float strength = (LinkForceSetting / MathF.Max( 1f, minDeg )) * Alpha;

            float targetDistance = edge.DesiredSpringLength > 0 ? edge.DesiredSpringLength : LinkDistanceSetting;
            float displacementMag = MathF.Sign( dist - targetDistance ) * MathF.Log( 1f + MathF.Abs( dist - targetDistance ) * 0.05f ) * 12f * strength;
            Vector2 displacement = (delta / dist) * displacementMag;

            float totalDeg = (float)(srcPayload.TotalDegree + dstPayload.TotalDegree);
            float srcBias = dstPayload.TotalDegree / totalDeg;
            float dstBias = srcPayload.TotalDegree / totalDeg;

            if ( !src.IsPinned ) src.Velocity += displacement * srcBias;
            if ( !dst.IsPinned ) dst.Velocity -= displacement * dstBias;
        }

        // 5. Soft Velocity Collisions (Breaks artificial circular rings into organic clouds)
        ApplySoftCollisions( registry );

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

    private void ApplySoftCollisions( SpatialRegistry registry )
    {
        _collisionGrid.Build( registry );
        int count = registry.Count;

        for ( int i = 0; i < count; i++ )
        {
            ref readonly var a = ref registry.GetSpatialRef( i );
            if ( a.IsHidden ) continue;

            Vector2 posA = a.Position;
            float radiusA = a.Radius;
            bool isPinnedA = a.IsPinned;

            _collisionGrid.QueryNeighbors( posA, neighborIdx =>
            {
                if ( i >= neighborIdx ) return;

                ref var b = ref registry.GetSpatialRef( neighborIdx );
                if ( b.IsHidden ) return;

                float targetDist = radiusA + b.Radius + 4f;
                Vector2 delta = b.Position - posA;
                float distSq = delta.LengthSquared;

                if ( distSq < (targetDist * targetDist) && distSq > 0.001f )
                {
                    float dist = MathF.Sqrt( distSq );
                    float overlap = (targetDist - dist);

                    Vector2 pushImpulse = (delta / dist) * (overlap * 0.40f * Alpha);

                    if ( !isPinnedA ) registry.GetSpatialRef( i ).Velocity -= pushImpulse * 0.5f;
                    if ( !b.IsPinned ) b.Velocity += pushImpulse * 0.5f;
                }
            } );
        }
    }
}