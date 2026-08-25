#nullable enable
using System;
using System.Collections.Generic;
using Editor.Analysis.Models.Blocks;

namespace Editor.Analysis.Models;

/// <summary>
/// High-performance in-memory graph model containing indexed NodeBlocks and deduplicated edges.
/// </summary>
public class CodeGraph
{
    private readonly Dictionary<string, NodeBlock> _nodes = new( StringComparer.OrdinalIgnoreCase );
    private readonly List<GraphEdge> _edges = new();
    private readonly HashSet<string> _edgeLookup = new( StringComparer.OrdinalIgnoreCase );

    public IReadOnlyDictionary<string, NodeBlock> Nodes => _nodes;
    public IReadOnlyList<GraphEdge> Edges => _edges;

    public void AddNode( NodeBlock node )
    {
        if ( node == null || string.IsNullOrWhiteSpace( node.Id ) )
            return;

        _nodes[node.Id] = node;
    }

    public void AddEdge( GraphEdge edge )
    {
        if ( edge == null || string.IsNullOrWhiteSpace( edge.SourceId ) || string.IsNullOrWhiteSpace( edge.TargetId ) )
            return;

        if ( string.Equals( edge.SourceId, edge.TargetId, StringComparison.OrdinalIgnoreCase ) )
            return;

        // O(1) Fast Deduplication Key
        string key = $"{edge.SourceId}|{edge.TargetId}|{edge.Kind}|{edge.Details}";
        if ( !_edgeLookup.Add( key ) )
            return;

        _edges.Add( edge );

        if ( _nodes.TryGetValue( edge.SourceId, out var sourceNode ) )
        {
            sourceNode.Relations.Outgoing.Add( edge );
        }

        if ( _nodes.TryGetValue( edge.TargetId, out var targetNode ) )
        {
            targetNode.Relations.Incoming.Add( edge );
        }
    }

    public NodeBlock? GetNode( string idOrName )
    {
        if ( string.IsNullOrWhiteSpace( idOrName ) ) return null;

        if ( _nodes.TryGetValue( idOrName, out var exactNode ) )
            return exactNode;

        foreach ( var n in _nodes.Values )
        {
            if ( string.Equals( n.Name, idOrName, StringComparison.OrdinalIgnoreCase ) )
                return n;
        }

        return null;
    }

    public void Clear()
    {
        _nodes.Clear();
        _edges.Clear();
        _edgeLookup.Clear();
    }
}