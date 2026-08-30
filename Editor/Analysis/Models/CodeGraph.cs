#nullable enable
using System;
using System.Collections.Generic;
using Editor.Analysis.Models.Blocks;

namespace Editor.Analysis.Models;

/// <summary>
/// Blazing-fast O(1) fractal code graph with multi-alias indexing and polymorphic registry.
/// </summary>
public class CodeGraph
{
    private readonly Dictionary<string, NodeBlock> _nodesByDocId = new( StringComparer.OrdinalIgnoreCase );
    private readonly Dictionary<string, NodeBlock> _lookupAliases = new( StringComparer.OrdinalIgnoreCase );
    private readonly List<SemanticWire> _edges = new();
    private readonly HashSet<string> _edgeLookup = new( StringComparer.OrdinalIgnoreCase );

    public Dictionary<string, List<string>> InterfaceImplementations { get; } = new( StringComparer.OrdinalIgnoreCase );

    public IReadOnlyDictionary<string, NodeBlock> Nodes => _nodesByDocId;
    public IReadOnlyList<SemanticWire> Edges => _edges;

    public void AddNode( NodeBlock node )
    {
        if ( node == null || string.IsNullOrWhiteSpace( node.DocId ) )
            return;

        _nodesByDocId[node.DocId] = node;

        // O(1) Fast Multi-Alias Indexing
        _lookupAliases[node.DocId] = node;
        _lookupAliases[node.Name] = node;

        string fqn = $"{node.Body.Namespace}.{node.Name}".TrimStart( '.' );
        if ( !string.IsNullOrWhiteSpace( fqn ) )
        {
            _lookupAliases[fqn] = node;
            _lookupAliases[$"T:{fqn}"] = node;
        }

        string cleanDoc = node.DocId.Replace( "T:", "" );
        _lookupAliases[cleanDoc] = node;
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

        // Direct O(1) instant dictionary lookup!
        if ( _lookupAliases.TryGetValue( edge.AgentDocId, out var sourceNode ) )
            sourceNode.Relations.Outgoing.Add( edge );

        if ( _lookupAliases.TryGetValue( edge.RecipientDocId, out var targetNode ) )
            targetNode.Relations.Incoming.Add( edge );

        // Index polymorphic implementations
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

    /// <summary>
    /// Instant O(1) lookup by DocId, full namespace, short name, or prefix (e.g. "BBox", "Sandbox.BBox", "T:Sandbox.BBox").
    /// </summary>
    public NodeBlock? GetNode( string query )
    {
        if ( string.IsNullOrWhiteSpace( query ) ) return null;

        // Direct O(1) dictionary lookups
        if ( _lookupAliases.TryGetValue( query, out var exact ) )
            return exact;

        if ( !query.StartsWith( "T:" ) && _lookupAliases.TryGetValue( $"T:{query}", out var withPrefix ) )
            return withPrefix;

        if ( _lookupAliases.TryGetValue( $"Sandbox.{query}", out var sandbox ) )
            return sandbox;

        if ( _lookupAliases.TryGetValue( $"T:Sandbox.{query}", out var sandboxDoc ) )
            return sandboxDoc;

        return null;
    }

    public void Clear()
    {
        _nodesByDocId.Clear();
        _lookupAliases.Clear();
        _edges.Clear();
        _edgeLookup.Clear();
        InterfaceImplementations.Clear();
    }
}