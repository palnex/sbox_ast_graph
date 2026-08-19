using System;
using System.Collections.Generic;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Core;

/// <summary>
/// High-performance flat spatial hash grid for O(1) neighbor queries and collision culling.
/// Zero per-frame allocations via reusable integer index arrays.
/// </summary>
public sealed class SpatialHashGrid
{
    private readonly float _cellSize;
    private readonly float _invCellSize;
    private readonly int _gridCols;
    private readonly int _gridRows;
    private readonly int _totalCells;

    private int[] _cellHead;
    private int[] _nodeNext;

    public SpatialHashGrid( float cellSize = 150f, int gridCols = 128, int gridRows = 128, int initialCapacity = 2048 )
    {
        _cellSize = cellSize;
        _invCellSize = 1.0f / cellSize;
        _gridCols = gridCols;
        _gridRows = gridRows;
        _totalCells = gridCols * gridRows;

        _cellHead = new int[_totalCells];
        _nodeNext = new int[initialCapacity];
    }

    /// <summary>
    /// Rebuilds the spatial index for the given list of nodes.
    /// </summary>
    public void Build( IReadOnlyList<Models.CanvasNode> nodes )
    {
        int count = nodes.Count;

        if ( _nodeNext.Length < count )
        {
            Array.Resize( ref _nodeNext, Math.Max( count * 2, 64 ) );
        }

        Array.Fill( _cellHead, -1 );

        int halfCols = _gridCols / 2;
        int halfRows = _gridRows / 2;

        for ( int i = 0; i < count; i++ )
        {
            Vector2 pos = nodes[i].Center;

            int cx = (int)MathF.Floor( pos.x * _invCellSize ) + halfCols;
            int cy = (int)MathF.Floor( pos.y * _invCellSize ) + halfRows;

            cx = Math.Clamp( cx, 0, _gridCols - 1 );
            cy = Math.Clamp( cy, 0, _gridRows - 1 );

            int cellIdx = (cy * _gridCols) + cx;

            _nodeNext[i] = _cellHead[cellIdx];
            _cellHead[cellIdx] = i;
        }
    }

    /// <summary>
    /// Queries all node indices located within a 3x3 cell neighborhood around the specified world position.
    /// </summary>
    public void QueryNeighbors( Vector2 worldPos, Action<int> onNeighborFound )
    {
        int halfCols = _gridCols / 2;
        int halfRows = _gridRows / 2;

        int cx = (int)MathF.Floor( worldPos.x * _invCellSize ) + halfCols;
        int cy = (int)MathF.Floor( worldPos.y * _invCellSize ) + halfRows;

        int minX = Math.Max( 0, cx - 1 );
        int maxX = Math.Min( _gridCols - 1, cx + 1 );
        int minY = Math.Max( 0, cy - 1 );
        int maxY = Math.Min( _gridRows - 1, cy + 1 );

        for ( int y = minY; y <= maxY; y++ )
        {
            int rowOffset = y * _gridCols;
            for ( int x = minX; x <= maxX; x++ )
            {
                int cellIdx = rowOffset + x;
                int nodeIdx = _cellHead[cellIdx];

                while ( nodeIdx != -1 )
                {
                    onNeighborFound( nodeIdx );
                    nodeIdx = _nodeNext[nodeIdx];
                }
            }
        }
    }
}