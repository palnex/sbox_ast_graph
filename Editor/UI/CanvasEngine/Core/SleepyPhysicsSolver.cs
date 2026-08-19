using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Core;

/// <summary>
/// Ultra-stable Force-Directed physics solver with Soft Velocity Collisions, 
/// High Viscosity Damping (0.68), and LinLog Subsystem Separation.
/// </summary>
public sealed class SleepyPhysicsSolver
{
    private readonly BarnesHutQuadTree _quadTree = new();
    private readonly SpatialHashGrid _collisionGrid = new( cellSize: 60f );

    public float Alpha { get; private set; } = 1.0f;
    public float AlphaTarget { get; set; } = 0.0f;
    public float AlphaDecay { get; set; } = 0.016f;
    public float AlphaMin { get; set; } = 0.001f;

    public float RepulsionConstant { get; set; } = 1400f;
    public float RepulsionMaxDist { get; set; } = 550f;
    public float LinkDistanceSetting { get; set; } = 200f;
    public float LinkForceSetting { get; set; } = 0.85f;
    public float CenterForceSetting { get; set; } = 0.35f;

    /// <summary>
    /// Viscous fluid damping (0.68 = dense syrup that kills all oscillations and jitter).
    /// </summary>
    public float Damping { get; set; } = 0.68f;

    public float TerminalVelocity { get; set; } = 14f;
    public float BaseCollisionRadius { get; set; } = 12f;

    public bool IsSleeping => Alpha < AlphaMin;

    public void Reheat( float energy = 1.0f )
    {
        Alpha = Math.Max( Alpha, energy );
    }

    public void WakeUp() => Reheat( 1.0f );

    /// <summary>
    /// Advances the physics simulation by one time-step.
    /// </summary>
    public void Step( IReadOnlyList<CanvasNode> nodes, IReadOnlyList<CanvasEdge> edges, float dt = 0.016f )
    {
        int nodeCount = nodes.Count;
        if ( nodeCount == 0 || IsSleeping ) return;

        // 1. Cool down Alpha
        Alpha += (AlphaTarget - Alpha) * AlphaDecay;

        if ( Alpha < AlphaMin )
        {
            // Hard freeze velocities on sleep to guarantee rock-solid stillness
            for ( int i = 0; i < nodeCount; i++ ) nodes[i].Velocity = Vector2.Zero;
            return;
        }

        // 2. Build Barnes-Hut QuadTree for Repulsion
        _quadTree.Build( nodes );

        // 3. Compute Many-Body Repulsion (Barnes-Hut)
        Parallel.For( 0, nodeCount, i =>
        {
            var node = nodes[i];
            if ( node.IsPinned || node.IsDragging )
            {
                node.AccumulatedForce = Vector2.Zero;
                return;
            }

            Vector2 pos = node.Center;
            Vector2 repulsion = _quadTree.ComputeRepulsion( i, pos, RepulsionConstant, maxDist: RepulsionMaxDist );
            node.AccumulatedForce = repulsion * Alpha;
        } );

        // 4. Integrate Acceleration from Repulsion into Velocity
        for ( int i = 0; i < nodeCount; i++ )
        {
            var node = nodes[i];
            if ( node.IsPinned || node.IsDragging )
            {
                node.Velocity = Vector2.Zero;
                continue;
            }

            Vector2 acceleration = node.AccumulatedForce / MathF.Max( 0.5f, node.Mass );
            node.Velocity += acceleration * dt;
        }

        // 5. LinLog Link Constraints (Soft Logarithmic Attraction to preserve distinct continents)
        int edgeCount = edges.Count;
        for ( int i = 0; i < edgeCount; i++ )
        {
            var edge = edges[i];
            var src = edge.Source;
            var dst = edge.Target;

            Vector2 delta = dst.Center - src.Center;
            float dist = delta.Length;
            if ( dist < 0.001f ) continue;

            // Degree-normalized strength
            float minDeg = MathF.Min( src.Degree, dst.Degree );
            float strength = (LinkForceSetting / MathF.Max( 1f, minDeg )) * Alpha;

            // Soft Logarithmic spring (ForceAtlas2 LinLog model)
            float displacementMag = MathF.Sign( dist - LinkDistanceSetting ) * MathF.Log( 1f + MathF.Abs( dist - LinkDistanceSetting ) * 0.05f ) * 12f * strength;
            Vector2 displacement = (delta / dist) * displacementMag;

            float totalDeg = (float)(src.Degree + dst.Degree);
            float srcBias = dst.Degree / totalDeg;
            float dstBias = src.Degree / totalDeg;

            if ( !src.IsPinned && !src.IsDragging )
                src.Velocity += displacement * srcBias;

            if ( !dst.IsPinned && !dst.IsDragging )
                dst.Velocity -= displacement * dstBias;
        }

        // 6. Soft Velocity Collision Pass (D3-style, NO coordinate teleportation)
        ApplySoftCollisions( nodes );

        // 7. Apply Center Force, Viscous Damping & Position Update
        float maxSpeedSq = TerminalVelocity * TerminalVelocity;

        for ( int i = 0; i < nodeCount; i++ )
        {
            var node = nodes[i];
            if ( node.IsPinned || node.IsDragging )
            {
                node.Velocity = Vector2.Zero;
                continue;
            }

            // Soft Center Gravity on velocity
            Vector2 centerPull = -node.Position * (CenterForceSetting * 0.002f) * Alpha;
            node.Velocity = (node.Velocity + centerPull) * Damping;

            // Terminal Velocity Clamp
            float speedSq = node.Velocity.LengthSquared;
            if ( speedSq > maxSpeedSq )
            {
                node.Velocity = node.Velocity.Normal * TerminalVelocity;
            }

            // Position update
            node.Position += node.Velocity;
        }
    }

    private void ApplySoftCollisions( IReadOnlyList<CanvasNode> nodes )
    {
        _collisionGrid.Build( nodes );
        int count = nodes.Count;

        for ( int i = 0; i < count; i++ )
        {
            var a = nodes[i];
            Vector2 posA = a.Center;
            float radiusA = BaseCollisionRadius + MathF.Sqrt( a.Degree ) * 1.0f;

            _collisionGrid.QueryNeighbors( posA, neighborIdx =>
            {
                if ( i >= neighborIdx ) return;

                var b = nodes[neighborIdx];
                float radiusB = BaseCollisionRadius + MathF.Sqrt( b.Degree ) * 1.0f;
                float targetDist = radiusA + radiusB;

                Vector2 delta = b.Center - posA;
                float distSq = delta.LengthSquared;

                if ( distSq < (targetDist * targetDist) && distSq > 0.001f )
                {
                    float dist = MathF.Sqrt( distSq );
                    float overlap = (targetDist - dist);

                    // Soft velocity impulse scaled by alpha (NO jittering!)
                    Vector2 pushImpulse = (delta / dist) * (overlap * 0.35f * Alpha);

                    if ( !a.IsPinned && !a.IsDragging ) a.Velocity -= pushImpulse * 0.5f;
                    if ( !b.IsPinned && !b.IsDragging ) b.Velocity += pushImpulse * 0.5f;
                }
            } );
        }
    }
}