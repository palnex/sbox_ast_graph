#nullable enable
using System;
using System.Collections.Generic;
using ArchitectureVisualizer.UI.CanvasEngine.Models;

namespace ArchitectureVisualizer.UI.Interaction;

/// <summary>
/// High-performance O(1) adjacency lookup cache for graph nodes and edges.
/// Eliminates costly linear edge iterations on selection and hover events.
/// </summary>
public sealed class CanvasAdjacencyIndex
{
    private List<int>[] _adjacency = Array.Empty<List<int>>();
    private readonly HashSet<int> _focusedNeighbors = new();

    /// <summary>
    /// Rebuilds the adjacency lookup table from the current edge list.
    /// Call this once after graph population or edge batch updates.
    /// </summary>
    public void Build( int nodeCount, IReadOnlyList<CanvasEdge> edges )
    {
        if ( _adjacency.Length < nodeCount )
        {
            _adjacency = new List<int>[nodeCount];
            for ( int i = 0; i < nodeCount; i++ )
                _adjacency[i] = new List<int>( 8 );
        }
        else
        {
            for ( int i = 0; i < nodeCount; i++ )
            {
                _adjacency[i] ??= new List<int>( 8 );
                _adjacency[i].Clear();
            }
        }

        for ( int i = 0; i < edges.Count; i++ )
        {
            var edge = edges[i];
            if ( edge.SourceIndex >= 0 && edge.SourceIndex < nodeCount &&
                 edge.TargetIndex >= 0 && edge.TargetIndex < nodeCount )
            {
                _adjacency[edge.SourceIndex].Add( edge.TargetIndex );
                _adjacency[edge.TargetIndex].Add( edge.SourceIndex );
            }
        }
    }

    /// <summary>
    /// Returns the cached set of neighbor indices connected to the active node.
    /// </summary>
    public HashSet<int> GetFocusedNeighbors( int activeIndex )
    {
        _focusedNeighbors.Clear();
        if ( activeIndex >= 0 && activeIndex < _adjacency.Length )
        {
            var list = _adjacency[activeIndex];
            if ( list != null )
            {
                for ( int i = 0; i < list.Count; i++ )
                    _focusedNeighbors.Add( list[i] );
            }
        }
        return _focusedNeighbors;
    }

    public void Clear()
    {
        _focusedNeighbors.Clear();
        for ( int i = 0; i < _adjacency.Length; i++ )
            _adjacency[i]?.Clear();
    }
}