#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor.Core.Models;

/// <summary>
/// In-memory graph structure storing analyzed nodes and directed dependency edges with fast lookup indices.
/// </summary>
public class DependencyGraph
{
    private readonly Dictionary<string, GraphNode> _nodes = new( StringComparer.OrdinalIgnoreCase );
    private readonly List<GraphEdge> _edges = new();

    private readonly Dictionary<string, List<GraphEdge>> _outgoingEdges = new( StringComparer.OrdinalIgnoreCase );
    private readonly Dictionary<string, List<GraphEdge>> _incomingEdges = new( StringComparer.OrdinalIgnoreCase );

    /// <summary>
    /// All registered nodes mapped by their unique identifier.
    /// </summary>
    public IReadOnlyDictionary<string, GraphNode> Nodes => _nodes;

    /// <summary>
    /// All registered dependency edges.
    /// </summary>
    public IReadOnlyList<GraphEdge> Edges => _edges;

    /// <summary>
    /// Adds or updates a node in the graph.
    /// </summary>
    public void AddNode( GraphNode node )
    {
        if ( node == null || string.IsNullOrWhiteSpace( node.Id ) )
            return;

        _nodes[node.Id] = node;

        if ( !_outgoingEdges.ContainsKey( node.Id ) )
            _outgoingEdges[node.Id] = new List<GraphEdge>();

        if ( !_incomingEdges.ContainsKey( node.Id ) )
            _incomingEdges[node.Id] = new List<GraphEdge>();
    }

    /// <summary>
    /// Adds a dependency edge between two nodes.
    /// </summary>
    public void AddEdge( GraphEdge edge )
    {
        if ( edge == null || string.IsNullOrWhiteSpace( edge.SourceId ) || string.IsNullOrWhiteSpace( edge.TargetId ) )
            return;

        // Prevent exact duplicate edges
        bool exists = _edges.Any( e =>
            string.Equals( e.SourceId, edge.SourceId, StringComparison.OrdinalIgnoreCase ) &&
            string.Equals( e.TargetId, edge.TargetId, StringComparison.OrdinalIgnoreCase ) &&
            e.Kind == edge.Kind &&
            string.Equals( e.Details, edge.Details, StringComparison.OrdinalIgnoreCase ) );

        if ( exists )
            return;

        _edges.Add( edge );

        if ( !_outgoingEdges.ContainsKey( edge.SourceId ) )
            _outgoingEdges[edge.SourceId] = new List<GraphEdge>();
        _outgoingEdges[edge.SourceId].Add( edge );

        if ( !_incomingEdges.ContainsKey( edge.TargetId ) )
            _incomingEdges[edge.TargetId] = new List<GraphEdge>();
        _incomingEdges[edge.TargetId].Add( edge );
    }

    /// <summary>
    /// Retrieves a node by its unique identifier.
    /// </summary>
    public GraphNode? GetNode( string id )
    {
        if ( string.IsNullOrWhiteSpace( id ) ) return null;
        _nodes.TryGetValue( id, out var node );
        return node;
    }

    /// <summary>
    /// Gets all outgoing edges originating from the specified node.
    /// </summary>
    public IReadOnlyList<GraphEdge> GetOutgoingEdges( string nodeId )
    {
        if ( _outgoingEdges.TryGetValue( nodeId, out var list ) )
            return list;

        return Array.Empty<GraphEdge>();
    }

    /// <summary>
    /// Gets all incoming edges targeting the specified node.
    /// </summary>
    public IReadOnlyList<GraphEdge> GetIncomingEdges( string nodeId )
    {
        if ( _incomingEdges.TryGetValue( nodeId, out var list ) )
            return list;

        return Array.Empty<GraphEdge>();
    }

    /// <summary>
    /// Clears all nodes and edges from the graph.
    /// </summary>
    public void Clear()
    {
        _nodes.Clear();
        _edges.Clear();
        _outgoingEdges.Clear();
        _incomingEdges.Clear();
    }
}