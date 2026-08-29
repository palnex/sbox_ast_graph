#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Editor.Analysis.Models;
using Editor.Analysis.Models.Blocks;

namespace Editor.Analysis;

/// <summary>
/// Result container for shortest path search between two entities.
/// </summary>
public class GraphPathResult
{
    public bool Found { get; set; }
    public string StartNodeId { get; set; } = string.Empty;
    public string TargetNodeId { get; set; } = string.Empty;
    public List<SemanticWire> Steps { get; set; } = new();

    public override string ToString()
    {
        if ( !Found ) return $"No path between '{StartNodeId}' and '{TargetNodeId}'.";
        return $"Path found ({Steps.Count} steps): " + string.Join( " -> ", Steps.Select( s => $"[{s.AgentDocId}]--({s.Action})-->[{s.RecipientDocId}]" ) );
    }
}

/// <summary>
/// Result container for detected circular dependencies.
/// </summary>
public class GraphCycleResult
{
    public List<string> CycleNodes { get; set; } = new();
    public string Representation { get; set; } = string.Empty;

    public override string ToString() => Representation;
}

/// <summary>
/// Graph traversal, pathfinding, and cycle detection algorithms.
/// </summary>
public static class GraphAlgorithms
{
    /// <summary>
    /// Finds the shortest dependency path between two nodes using Breadth-First Search (BFS).
    /// </summary>
    public static GraphPathResult FindShortestPath( CodeGraph graph, string fromId, string toId, bool undirected = false )
    {
        var result = new GraphPathResult
        {
            StartNodeId = fromId,
            TargetNodeId = toId
        };

        var startNode = graph.GetNode( fromId );
        var targetNode = graph.GetNode( toId );

        if ( startNode == null || targetNode == null )
            return result;

        string startKey = startNode.DocId;
        string targetKey = targetNode.DocId;

        var queue = new Queue<string>();
        var visited = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        var parentEdge = new Dictionary<string, SemanticWire>( StringComparer.OrdinalIgnoreCase );

        queue.Enqueue( startKey );
        visited.Add( startKey );

        bool found = false;

        while ( queue.Count > 0 )
        {
            var current = queue.Dequeue();

            if ( string.Equals( current, targetKey, StringComparison.OrdinalIgnoreCase ) )
            {
                found = true;
                break;
            }

            var currNode = graph.GetNode( current );
            if ( currNode == null ) continue;

            var neighbors = new List<SemanticWire>();
            neighbors.AddRange( currNode.Relations.Outgoing );

            if ( undirected )
            {
                neighbors.AddRange( currNode.Relations.Incoming );
            }

            foreach ( var edge in neighbors )
            {
                string neighborId = string.Equals( edge.AgentDocId, current, StringComparison.OrdinalIgnoreCase )
                    ? edge.RecipientDocId
                    : edge.AgentDocId;

                if ( !visited.Contains( neighborId ) )
                {
                    visited.Add( neighborId );
                    parentEdge[neighborId] = edge;
                    queue.Enqueue( neighborId );
                }
            }
        }

        if ( !found )
            return result;

        result.Found = true;
        string curr = targetKey;

        while ( !string.Equals( curr, startKey, StringComparison.OrdinalIgnoreCase ) )
        {
            if ( parentEdge.TryGetValue( curr, out var edge ) )
            {
                result.Steps.Add( edge );
                curr = string.Equals( edge.AgentDocId, curr, StringComparison.OrdinalIgnoreCase ) ? edge.RecipientDocId : edge.AgentDocId;
            }
            else
            {
                break;
            }
        }

        result.Steps.Reverse();
        return result;
    }

    /// <summary>
    /// Detects all unique circular dependency loops (A -> B -> C -> A) using Depth-First Search.
    /// </summary>
    public static List<GraphCycleResult> DetectCycles( CodeGraph graph )
    {
        var states = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
        var pathStack = new List<string>();
        var uniqueCycles = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        var results = new List<GraphCycleResult>();

        foreach ( var node in graph.Nodes.Keys )
        {
            states[node] = 0;
        }

        foreach ( var node in graph.Nodes.Keys )
        {
            if ( states[node] == 0 )
            {
                DfsCycle( graph, node, states, pathStack, uniqueCycles, results );
            }
        }

        return results;
    }

    private static void DfsCycle(
        CodeGraph graph,
        string nodeKey,
        Dictionary<string, int> states,
        List<string> pathStack,
        HashSet<string> uniqueCycles,
        List<GraphCycleResult> results )
    {
        states[nodeKey] = 1;
        pathStack.Add( nodeKey );

        var node = graph.GetNode( nodeKey );
        if ( node != null )
        {
            foreach ( var edge in node.Relations.Outgoing )
            {
                string target = edge.RecipientDocId;

                if ( !states.ContainsKey( target ) )
                    continue;

                if ( states[target] == 1 )
                {
                    int startIndex = pathStack.IndexOf( target );
                    if ( startIndex != -1 )
                    {
                        var cycleNodes = pathStack.Skip( startIndex ).ToList();
                        cycleNodes.Add( target );

                        string canonical = NormalizeCycle( cycleNodes );
                        if ( uniqueCycles.Add( canonical ) )
                        {
                            results.Add( new GraphCycleResult
                            {
                                CycleNodes = cycleNodes,
                                Representation = canonical
                            } );
                        }
                    }
                }
                else if ( states[target] == 0 )
                {
                    DfsCycle( graph, target, states, pathStack, uniqueCycles, results );
                }
            }
        }

        pathStack.RemoveAt( pathStack.Count - 1 );
        states[nodeKey] = 2;
    }

    private static string NormalizeCycle( List<string> cycleNodes )
    {
        var temp = cycleNodes.Take( cycleNodes.Count - 1 ).ToList();
        string minNode = temp.OrderBy( n => n, StringComparer.OrdinalIgnoreCase ).First();
        int minIndex = temp.IndexOf( minNode );

        var normalized = temp.Skip( minIndex ).Concat( temp.Take( minIndex ) ).ToList();
        normalized.Add( minNode );

        return string.Join( " ──► ", normalized.Select( n => $"[{n}]" ) );
    }
}