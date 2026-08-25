#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Editor.Analysis.Models;

namespace Editor.Analysis;

/// <summary>
/// Architecture complexity report identifying key hubs, God classes, and isolated types.
/// </summary>
public class ArchitectureMetricsReport
{
    public int TotalNodes { get; set; }
    public int TotalEdges { get; set; }
    public List<KeyValuePair<string, int>> TopHubs { get; set; } = new();
    public List<KeyValuePair<string, int>> TopGodNodes { get; set; } = new();
    public List<string> IsolatedNodes { get; set; } = new();
}

/// <summary>
/// Calculates architectural health metrics on the dependency graph.
/// </summary>
public static class GraphMetrics
{
    public static ArchitectureMetricsReport Calculate( CodeGraph graph, int topCount = 5 )
    {
        var inDegree = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
        var outDegree = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );

        foreach ( var node in graph.Nodes.Values )
        {
            inDegree[node.Id] = node.Relations.IncomingCount;
            outDegree[node.Id] = node.Relations.OutgoingCount;
        }

        return new ArchitectureMetricsReport
        {
            TotalNodes = graph.Nodes.Count,
            TotalEdges = graph.Edges.Count,
            TopHubs = inDegree.OrderByDescending( kv => kv.Value ).Take( topCount ).ToList(),
            TopGodNodes = outDegree.OrderByDescending( kv => kv.Value ).Take( topCount ).ToList(),
            IsolatedNodes = graph.Nodes.Keys.Where( k => inDegree.GetValueOrDefault( k ) == 0 && outDegree.GetValueOrDefault( k ) == 0 ).ToList()
        };
    }
}