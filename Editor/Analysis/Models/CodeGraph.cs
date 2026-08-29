#nullable enable
using System;
using System.Collections.Generic;
using Editor.Analysis.Models.Blocks;

namespace Editor.Analysis.Models;

/// <summary>
/// High-performance fractal code graph indexing entities by DocId with instant semantic lookup.
/// </summary>
public class CodeGraph
{
    private readonly Dictionary<string, NodeBlock> _nodes = new( StringComparer.OrdinalIgnoreCase );
    private readonly List<SemanticWire> _edges = new();
    private readonly HashSet<string> _edgeLookup = new( StringComparer.OrdinalIgnoreCase );

    // Maps Interface DocId -> List of implementing concrete Class DocIds (Polymorphic Index)
    public Dictionary<string, List<string>> InterfaceImplementations { get; } = new( StringComparer.OrdinalIgnoreCase );

    public IReadOnlyDictionary<string, NodeBlock> Nodes => _nodes;
    public IReadOnlyList<SemanticWire> Edges => _edges;

    public void AddNode( NodeBlock node )
    {
        if ( node == null || string.IsNullOrWhiteSpace( node.DocId ) )
            return;

        _nodes[node.DocId] = node;

        // Also index by simple name if not exists for fast lookups
        if ( !string.Equals( node.DocId, node.Name, StringComparison.OrdinalIgnoreCase ) && !_nodes.ContainsKey( node.Name ) )
        {
            _nodes[node.Name] = node;
        }
    }

    public void AddEdge( SemanticWire edge )
    {
        if ( edge == null || string.IsNullOrWhiteSpace( edge.AgentDocId ) || string.IsNullOrWhiteSpace( edge.RecipientDocId ) )
            return;

        if ( string.Equals( edge.AgentDocId, edge.RecipientDocId, StringComparison.OrdinalIgnoreCase ) )
            return;

        string key = $"{edge.AgentDocId}|{edge.RecipientDocId}|{edge.Action}|{edge.Instrument}|{edge.Condition}";
        if ( !_edgeLookup.Add( key ) )
            return;

        _edges.Add( edge );

        if ( _nodes.TryGetValue( edge.AgentDocId, out var sourceNode ) )
            sourceNode.Relations.Outgoing.Add( edge );

        if ( _nodes.TryGetValue( edge.RecipientDocId, out var targetNode ) )
            targetNode.Relations.Incoming.Add( edge );

        // If edge is Interface Implementation, index it for Polymorphic Dynamic Dispatch
        if ( edge.Action == RelationKind.Implements )
        {
            if ( !InterfaceImplementations.TryGetValue( edge.RecipientDocId, out var list ) )
            {
                list = new List<string>();
                InterfaceImplementations[edge.RecipientDocId] = list;
            }
            if ( !list.Contains( edge.AgentDocId ) ) list.Add( edge.AgentDocId );
        }
    }

    public NodeBlock? GetNode( string idOrName )
    {
        if ( string.IsNullOrWhiteSpace( idOrName ) ) return null;

        if ( _nodes.TryGetValue( idOrName, out var exactNode ) )
            return exactNode;

        return null;
    }

    public void Clear()
    {
        _nodes.Clear();
        _edges.Clear();
        _edgeLookup.Clear();
        InterfaceImplementations.Clear();
    }
}