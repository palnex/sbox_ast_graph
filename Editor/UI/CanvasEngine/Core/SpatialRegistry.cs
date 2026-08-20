#nullable enable
using System;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Core;

/// <summary>
/// High-performance contiguous memory registry managing Spatial Nodes and Payloads.
/// </summary>
public sealed class SpatialRegistry
{
    private NodeSpatialData[] _spatials;
    private NodePayload[] _payloads;
    private int _count;

    public int Count => _count;

    public SpatialRegistry( int initialCapacity = 2048 )
    {
        _spatials = new NodeSpatialData[initialCapacity];
        _payloads = new NodePayload[initialCapacity];
        _count = 0;
    }

    public void Clear()
    {
        _count = 0;
    }

    public int Allocate( in NodeSpatialData spatial, NodePayload payload )
    {
        if ( _count >= _spatials.Length )
        {
            int newCap = Math.Max( _spatials.Length * 2, 64 );
            Array.Resize( ref _spatials, newCap );
            Array.Resize( ref _payloads, newCap );
        }

        int idx = _count++;
        _spatials[idx] = spatial;
        _spatials[idx].PayloadIndex = idx;

        payload.Index = idx;
        _payloads[idx] = payload;

        return idx;
    }

    public Span<NodeSpatialData> GetSpatialSpan() => new( _spatials, 0, _count );
    public ReadOnlySpan<NodeSpatialData> GetReadOnlySpatialSpan() => new( _spatials, 0, _count );

    public ref NodeSpatialData GetSpatialRef( int index ) => ref _spatials[index];
    public NodePayload GetPayload( int index ) => _payloads[index];

    /// <summary>
    /// Analytical Distance-Sorted and Z-Priority picking: finds the closest top-most node under cursor.
    /// </summary>
    public int PickNode( Vector2 worldPos )
    {
        int bestIdx = -1;
        float bestDistSq = float.MaxValue;
        ushort bestZ = 0;

        for ( int i = _count - 1; i >= 0; i-- )
        {
            ref readonly var n = ref _spatials[i];
            if ( n.IsHidden ) continue;

            bool isHit = n.Shape switch
            {
                NodeShape.Circle => SpatialHitTester.HitTestCircle( worldPos, n.Position, n.Radius ),
                NodeShape.Box => SpatialHitTester.HitTestBox( worldPos, new Rect( n.Position - new Vector2( n.Radius ), new Vector2( n.Radius * 2f ) ) ),
                _ => SpatialHitTester.HitTestCircle( worldPos, n.Position, n.Radius )
            };

            if ( isHit )
            {
                float distSq = (worldPos - n.Position).LengthSquared;

                // Priority: 1. Higher Z-Level, 2. Closest center distance
                if ( n.ZLevel > bestZ || (n.ZLevel == bestZ && distSq < bestDistSq) )
                {
                    bestZ = n.ZLevel;
                    bestDistSq = distSq;
                    bestIdx = i;
                }
            }
        }

        return bestIdx;
    }
}