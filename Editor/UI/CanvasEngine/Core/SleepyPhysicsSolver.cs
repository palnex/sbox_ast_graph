using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Core;

/// <summary>
/// Multithreaded Spring-Relaxation physics solver with kinetic energy sleep decay.
/// Automatically falls asleep when forces equilibrate (0% CPU cost).
/// </summary>
public sealed class SleepyPhysicsSolver
{
    private readonly SpatialHashGrid _grid = new();

    /// <summary>
    /// Repulsion force magnitude between nearby nodes (Coulomb-like anti-gravity).
    /// </summary>
    public float RepulsionConstant { get; set; } = 15000f;

    /// <summary>
    /// Cutoff radius squared for repulsion interactions.
    /// </summary>
    public float RepulsionCutoffSq { get; set; } = 400f * 400f;

    /// <summary>
    /// Spring stiffness factor (Hooke's law).
    /// </summary>
    public float SpringStiffness { get; set; } = 0.05f;

    /// <summary>
    /// Velocity damping per frame (friction).
    /// </summary>
    public float Damping { get; set; } = 0.85f;

    /// <summary>
    /// Global gravitational pull toward the world origin (0,0) to prevent disconnected drift.
    /// </summary>
    public float CenterGravity { get; set; } = 0.002f;

    /// <summary>
    /// Kinetic energy threshold below which simulation goes to sleep.
    /// </summary>
    public float SleepVelocityEpsilon { get; set; } = 0.02f;

    /// <summary>
    /// Indicates whether the physics engine is currently dormant (sleeping).
    /// </summary>
    public bool IsSleeping { get; private set; } = false;

    /// <summary>
    /// Wakes up the physics simulation when a node is dragged, added, or resized.
    /// </summary>
    public void WakeUp()
    {
        IsSleeping = false;
    }

    /// <summary>
    /// Advances the physics simulation by one time-step.
    /// </summary>
    public void Step( IReadOnlyList<CanvasNode> nodes, IReadOnlyList<CanvasEdge> edges, float dt = 0.016f )
    {
        int nodeCount = nodes.Count;
        if ( nodeCount == 0 || IsSleeping ) return;

        // 1. Rebuild spatial grid for O(1) local lookups
        _grid.Build( nodes );

        // 2. Compute Repulsion Forces (Multithreaded via Parallel.For)
        Parallel.For( 0, nodeCount, i =>
        {
            var nodeA = nodes[i];
            if ( nodeA.IsPinned || nodeA.IsDragging )
            {
                nodeA.AccumulatedForce = Vector2.Zero;
                return;
            }

            Vector2 posA = nodeA.Center;
            Vector2 accumRepulsion = Vector2.Zero;

            _grid.QueryNeighbors( posA, neighborIdx =>
            {
                if ( i == neighborIdx ) return;

                var nodeB = nodes[neighborIdx];
                Vector2 delta = posA - nodeB.Center;
                float distSq = (delta.x * delta.x) + (delta.y * delta.y) + 1.0f; // Softening

                if ( distSq < RepulsionCutoffSq )
                {
                    float invDist = 1.0f / MathF.Sqrt( distSq );
                    float forceMag = RepulsionConstant / distSq;
                    accumRepulsion += delta * invDist * forceMag;
                }
            } );

            // Center gravity pull
            Vector2 gravityPull = -posA * CenterGravity;

            nodeA.AccumulatedForce = accumRepulsion + gravityPull;
        } );

        // 3. Compute Spring Attraction along Edges (Hooke's Law)
        int edgeCount = edges.Count;
        for ( int i = 0; i < edgeCount; i++ )
        {
            var edge = edges[i];
            var src = edge.Source;
            var dst = edge.Target;

            Vector2 delta = dst.Center - src.Center;
            float currentDist = delta.Length;
            if ( currentDist < 0.001f ) continue;

            float displacement = currentDist - edge.DesiredSpringLength;
            Vector2 springForce = (delta / currentDist) * (displacement * SpringStiffness);

            if ( !src.IsPinned && !src.IsDragging )
                src.AccumulatedForce += springForce;

            if ( !dst.IsPinned && !dst.IsDragging )
                dst.AccumulatedForce -= springForce;
        }

        // 4. Integrate Velocities and Positions (Verlet/Euler with sleep detection)
        float totalVelocitySq = 0f;
        int activeMovableCount = 0;

        for ( int i = 0; i < nodeCount; i++ )
        {
            var node = nodes[i];
            if ( node.IsPinned || node.IsDragging )
            {
                node.Velocity = Vector2.Zero;
                continue;
            }

            // a = F / m
            Vector2 acceleration = node.AccumulatedForce / MathF.Max( 0.1f, node.Mass );

            // v = (v + a * dt) * damping
            node.Velocity = (node.Velocity + (acceleration * dt)) * Damping;

            // p = p + v
            node.Position += node.Velocity;

            totalVelocitySq += node.Velocity.LengthSquared;
            activeMovableCount++;
        }

        // 5. Stillness check: put simulation to sleep if movement is negligible
        if ( activeMovableCount > 0 )
        {
            float avgVelocity = MathF.Sqrt( totalVelocitySq / activeMovableCount );
            if ( avgVelocity < SleepVelocityEpsilon )
            {
                IsSleeping = true;
            }
        }
    }
}