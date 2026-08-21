#nullable enable
using System;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Core;

/// <summary>
/// High-performance 2D QuadTree implementing Barnes-Hut approximation over flat SpatialRegistry arrays.
/// Zero GC allocations per tick.
/// </summary>
public sealed class BarnesHutQuadTree
{
    private struct QuadCell
    {
        public Rect Bounds;
        public Vector2 CenterOfMass;
        public float TotalMass;
        public int NodeIndex; // -1 = empty, >= 0 = leaf, -2 = branch
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

    public BarnesHutQuadTree( int initialCapacity = 4096 )
    {
        _cells = new QuadCell[initialCapacity];
    }

    public void Build( SpatialRegistry registry )
    {
        _cellCount = 0;
        int count = registry.Count;
        if ( count == 0 ) return;

        var spatials = registry.GetReadOnlySpatialSpan();

        // 1. Calculate World Enclosing Bounds
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for ( int i = 0; i < count; i++ )
        {
            ref readonly var node = ref spatials[i];
            if ( node.IsHidden ) continue;

            Vector2 p = node.Position;
            if ( p.x < minX ) minX = p.x;
            if ( p.x > maxX ) maxX = p.x;
            if ( p.y < minY ) minY = p.y;
            if ( p.y > maxY ) maxY = p.y;
        }

        float size = MathF.Max( maxX - minX, maxY - minY ) + 60f;
        float halfSize = size * 0.5f;
        Vector2 center = new( (minX + maxX) * 0.5f, (minY + maxY) * 0.5f );

        Rect rootBounds = new( center.x - halfSize, center.y - halfSize, size, size );
        int rootIdx = AllocateCell( rootBounds );

        // 2. Insert all active nodes
        for ( int i = 0; i < count; i++ )
        {
            ref readonly var node = ref spatials[i];
            if ( node.IsHidden ) continue;

            float mass = registry.GetPayload( i ).PhysicsMass;
            InsertNode( registry, rootIdx, i, node.Position, mass );
        }

        // 3. Compute Centers of Mass
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

    private void InsertNode( SpatialRegistry registry, int cellIdx, int nodeIdx, Vector2 pos, float mass )
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
            int existingIdx = cell.NodeIndex;
            Vector2 existingPos = registry.GetSpatialRef( existingIdx ).Position;
            float existingMass = registry.GetPayload( existingIdx ).PhysicsMass;

            cell.NodeIndex = -2; // Convert to branch
            Subdivide( cellIdx );

            InsertIntoChildren( registry, cellIdx, existingIdx, existingPos, existingMass );
            InsertIntoChildren( registry, cellIdx, nodeIdx, pos, mass );
            return;
        }

        if ( cell.IsBranch )
        {
            InsertIntoChildren( registry, cellIdx, nodeIdx, pos, mass );
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

    private void InsertIntoChildren( SpatialRegistry registry, int cellIdx, int nodeIdx, Vector2 pos, float mass )
    {
        ref var cell = ref _cells[cellIdx];
        Vector2 mid = cell.Bounds.Center;

        int targetChild = (pos.y < mid.y)
            ? (pos.x < mid.x ? cell.ChildNW : cell.ChildNE)
            : (pos.x < mid.x ? cell.ChildSW : cell.ChildSE);

        InsertNode( registry, targetChild, nodeIdx, pos, mass );
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
    /// Computes mass-weighted Barnes-Hut repulsion force.
    /// </summary>
    public Vector2 ComputeRepulsion( int targetNodeIdx, Vector2 targetPos, float targetMass, float kRepulse, float theta = 0.85f, float maxDist = 2500f )
    {
        if ( _cellCount == 0 ) return Vector2.Zero;
        Vector2 accumForce = Vector2.Zero;
        TraverseCell( 0, targetNodeIdx, targetPos, targetMass, kRepulse, theta * theta, maxDist * maxDist, ref accumForce );
        return accumForce;
    }

    private void TraverseCell( int cellIdx, int targetNodeIdx, Vector2 targetPos, float targetMass, float kRepulse, float thetaSq, float maxDistSq, ref Vector2 accumForce )
    {
        if ( cellIdx < 0 || cellIdx >= _cellCount ) return;
        ref var cell = ref _cells[cellIdx];
        if ( cell.IsEmpty || cell.TotalMass <= 0.0001f ) return;

        Vector2 delta = targetPos - cell.CenterOfMass;
        float distSq = delta.LengthSquared;

        if ( distSq > maxDistSq ) return;

        // Softening radius scales with mass to prevent overlapping centers
        float minSoftDist = 64.0f + (targetMass + cell.TotalMass) * 4.0f;
        distSq = MathF.Max( distSq, minSoftDist );

        float dist = MathF.Sqrt( distSq );
        float forceMag = (kRepulse * 350.0f * cell.TotalMass) / distSq;

        if ( cell.IsLeaf )
        {
            if ( cell.NodeIndex != targetNodeIdx )
            {
                accumForce += (delta / dist) * forceMag;
            }
            return;
        }

        // Barnes-Hut criterion
        float sizeSq = cell.Bounds.Width * cell.Bounds.Width;
        if ( (sizeSq / distSq) < thetaSq )
        {
            accumForce += (delta / dist) * forceMag;
        }
        else
        {
            TraverseCell( cell.ChildNW, targetNodeIdx, targetPos, targetMass, kRepulse, thetaSq, maxDistSq, ref accumForce );
            TraverseCell( cell.ChildNE, targetNodeIdx, targetPos, targetMass, kRepulse, thetaSq, maxDistSq, ref accumForce );
            TraverseCell( cell.ChildSW, targetNodeIdx, targetPos, targetMass, kRepulse, thetaSq, maxDistSq, ref accumForce );
            TraverseCell( cell.ChildSE, targetNodeIdx, targetPos, targetMass, kRepulse, thetaSq, maxDistSq, ref accumForce );
        }
    }
}