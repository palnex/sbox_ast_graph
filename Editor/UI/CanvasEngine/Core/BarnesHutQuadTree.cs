using System;
using System.Collections.Generic;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Core;

/// <summary>
/// High-performance 2D QuadTree implementing the Barnes-Hut approximation for N-body force calculations.
/// Uses pre-allocated flat arrays to guarantee zero GC allocations per tick.
/// </summary>
public sealed class BarnesHutQuadTree
{
    private struct QuadCell
    {
        public Rect Bounds;
        public Vector2 CenterOfMass;
        public float TotalMass;
        public int NodeIndex; // -1 if empty, >= 0 if leaf node, -2 if internal branch
        public int ChildNW;
        public int ChildNE;
        public int ChildSW;
        public int ChildSE;

        public bool IsLeaf => NodeIndex >= 0;
        public bool IsBranch => NodeIndex == -2;
        public bool IsEmpty => NodeIndex == -1;
    }

    private QuadCell[] _cells;
    private int _cellCount;
    private IReadOnlyList<CanvasNode>? _activeNodes;

    public BarnesHutQuadTree( int initialCapacity = 4096 )
    {
        _cells = new QuadCell[initialCapacity];
    }

    /// <summary>
    /// Builds the QuadTree covering the bounding box of all active nodes.
    /// </summary>
    public void Build( IReadOnlyList<CanvasNode> nodes )
    {
        _activeNodes = nodes;
        _cellCount = 0;

        int count = nodes.Count;
        if ( count == 0 ) return;

        // 1. Calculate World Enclosing Bounds
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for ( int i = 0; i < count; i++ )
        {
            Vector2 p = nodes[i].Center;
            if ( p.x < minX ) minX = p.x;
            if ( p.x > maxX ) maxX = p.x;
            if ( p.y < minY ) minY = p.y;
            if ( p.y > maxY ) maxY = p.y;
        }

        float size = MathF.Max( maxX - minX, maxY - minY ) + 40f;
        float halfSize = size * 0.5f;
        Vector2 center = new( (minX + maxX) * 0.5f, (minY + maxY) * 0.5f );

        Rect rootBounds = new( center.x - halfSize, center.y - halfSize, size, size );

        // 2. Allocate Root Cell
        int rootIdx = AllocateCell( rootBounds );

        // 3. Insert all nodes
        for ( int i = 0; i < count; i++ )
        {
            InsertNode( rootIdx, i, nodes[i].Center, nodes[i].Mass );
        }

        // 4. Compute Centers of Mass recursively
        ComputeMassDistribution( rootIdx );
    }

    private int AllocateCell( in Rect bounds )
    {
        if ( _cellCount >= _cells.Length )
        {
            Array.Resize( ref _cells, _cells.Length * 2 );
        }

        int idx = _cellCount++;
        _cells[idx] = new QuadCell
        {
            Bounds = bounds,
            CenterOfMass = Vector2.Zero,
            TotalMass = 0f,
            NodeIndex = -1,
            ChildNW = -1,
            ChildNE = -1,
            ChildSW = -1,
            ChildSE = -1
        };
        return idx;
    }

    private void InsertNode( int cellIdx, int nodeIdx, Vector2 pos, float mass )
    {
        ref var cell = ref _cells[cellIdx];

        if ( cell.IsEmpty )
        {
            cell.NodeIndex = nodeIdx;
            cell.CenterOfMass = pos;
            cell.TotalMass = mass;
            return;
        }

        if ( cell.IsLeaf )
        {
            int existingNodeIdx = cell.NodeIndex;
            Vector2 existingPos = _activeNodes![existingNodeIdx].Center;
            float existingMass = _activeNodes[existingNodeIdx].Mass;

            cell.NodeIndex = -2; // Convert to branch
            Subdivide( cellIdx );

            // Re-insert existing node and new node into children
            InsertIntoChildren( cellIdx, existingNodeIdx, existingPos, existingMass );
            InsertIntoChildren( cellIdx, nodeIdx, pos, mass );
            return;
        }

        if ( cell.IsBranch )
        {
            InsertIntoChildren( cellIdx, nodeIdx, pos, mass );
        }
    }

    private void Subdivide( int cellIdx )
    {
        Rect b = _cells[cellIdx].Bounds;
        float halfW = b.Width * 0.5f;
        float halfH = b.Height * 0.5f;

        _cells[cellIdx].ChildNW = AllocateCell( new Rect( b.Left, b.Top, halfW, halfH ) );
        _cells[cellIdx].ChildNE = AllocateCell( new Rect( b.Left + halfW, b.Top, halfW, halfH ) );
        _cells[cellIdx].ChildSW = AllocateCell( new Rect( b.Left, b.Top + halfH, halfW, halfH ) );
        _cells[cellIdx].ChildSE = AllocateCell( new Rect( b.Left + halfW, b.Top + halfH, halfW, halfH ) );
    }

    private void InsertIntoChildren( int cellIdx, int nodeIdx, Vector2 pos, float mass )
    {
        ref var cell = ref _cells[cellIdx];
        Vector2 mid = cell.Bounds.Center;

        int targetChild = (pos.y < mid.y)
            ? (pos.x < mid.x ? cell.ChildNW : cell.ChildNE)
            : (pos.x < mid.x ? cell.ChildSW : cell.ChildSE);

        InsertNode( targetChild, nodeIdx, pos, mass );
    }

    private void ComputeMassDistribution( int cellIdx )
    {
        if ( cellIdx < 0 || cellIdx >= _cellCount ) return;
        ref var cell = ref _cells[cellIdx];

        if ( cell.IsLeaf ) return;

        if ( cell.IsBranch )
        {
            ComputeMassDistribution( cell.ChildNW );
            ComputeMassDistribution( cell.ChildNE );
            ComputeMassDistribution( cell.ChildSW );
            ComputeMassDistribution( cell.ChildSE );

            float totalMass = 0f;
            Vector2 weightedPos = Vector2.Zero;

            AccumulateChildMass( cell.ChildNW, ref totalMass, ref weightedPos );
            AccumulateChildMass( cell.ChildNE, ref totalMass, ref weightedPos );
            AccumulateChildMass( cell.ChildSW, ref totalMass, ref weightedPos );
            AccumulateChildMass( cell.ChildSE, ref totalMass, ref weightedPos );

            cell.TotalMass = totalMass;
            cell.CenterOfMass = totalMass > 0.001f ? (weightedPos / totalMass) : cell.Bounds.Center;
        }
    }

    private void AccumulateChildMass( int childIdx, ref float totalMass, ref Vector2 weightedPos )
    {
        if ( childIdx < 0 ) return;
        ref var child = ref _cells[childIdx];
        if ( child.TotalMass > 0 )
        {
            totalMass += child.TotalMass;
            weightedPos += child.CenterOfMass * child.TotalMass;
        }
    }

    /// <summary>
	/// Computes the Barnes-Hut repulsion force with DistanceMax cutoff (Obsidian/D3 style).
	/// </summary>
	public Vector2 ComputeRepulsion( int targetNodeIdx, Vector2 targetPos, float kRepulse, float theta = 0.85f, float maxDist = 450f )
    {
        if ( _cellCount == 0 ) return Vector2.Zero;
        Vector2 accumulatedForce = Vector2.Zero;
        TraverseCell( 0, targetNodeIdx, targetPos, kRepulse, theta * theta, maxDist * maxDist, ref accumulatedForce );
        return accumulatedForce;
    }

    private void TraverseCell( int cellIdx, int targetNodeIdx, Vector2 targetPos, float kRepulse, float thetaSq, float maxDistSq, ref Vector2 accumForce )
    {
        if ( cellIdx < 0 || cellIdx >= _cellCount ) return;
        ref var cell = ref _cells[cellIdx];
        if ( cell.IsEmpty || cell.TotalMass <= 0.0001f ) return;

        Vector2 delta = targetPos - cell.CenterOfMass;
        float distSq = delta.LengthSquared;

        // DistanceMax Cutoff: Ignore clusters that are too far away
        if ( distSq > maxDistSq ) return;

        distSq += 25.0f; // Softening radius

        if ( cell.IsLeaf )
        {
            if ( cell.NodeIndex != targetNodeIdx )
            {
                float invDist = 1.0f / MathF.Sqrt( distSq );
                float forceMag = (kRepulse * cell.TotalMass) / distSq;
                accumForce += delta * invDist * forceMag;
            }
            return;
        }

        // Barnes-Hut Criterion: (s / d)^2 < theta^2
        float sizeSq = cell.Bounds.Width * cell.Bounds.Width;
        if ( (sizeSq / distSq) < thetaSq )
        {
            // Treat entire cluster as single mass
            float invDist = 1.0f / MathF.Sqrt( distSq );
            float forceMag = (kRepulse * cell.TotalMass) / distSq;
            accumForce += delta * invDist * forceMag;
        }
        else
        {
            // Recurse into 4 quadrants
            TraverseCell( cell.ChildNW, targetNodeIdx, targetPos, kRepulse, thetaSq, maxDistSq, ref accumForce );
            TraverseCell( cell.ChildNE, targetNodeIdx, targetPos, kRepulse, thetaSq, maxDistSq, ref accumForce );
            TraverseCell( cell.ChildSW, targetNodeIdx, targetPos, kRepulse, thetaSq, maxDistSq, ref accumForce );
            TraverseCell( cell.ChildSE, targetNodeIdx, targetPos, kRepulse, thetaSq, maxDistSq, ref accumForce );
        }
    }
}