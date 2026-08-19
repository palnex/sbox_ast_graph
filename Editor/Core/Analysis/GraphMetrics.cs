#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Editor.Core.Models;

namespace Editor.Core.Analysis;

/// <summary>
/// Metric report detailing architectural complexity, hubs, God classes, and dead code.
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
/// Calculates architectural metrics and health indicators on the dependency graph.
/// </summary>
public static class GraphMetrics
{
    /// <summary>
    /// Computes in-degree (Hubs), out-degree (God Classes), and zero-degree (Isolated / Dead Code) metrics.
    /// </summary>
    public static ArchitectureMetricsReport Calculate( DependencyGraph graph, int topCount = 5 )
    {
        var inDegree = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
        var outDegree = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );

        foreach ( var node in graph.Nodes.Keys )
        {
            inDegree[node] = 0;
            outDegree[node] = 0;
        }

        foreach ( var edge in graph.Edges )
        {
            if ( outDegree.ContainsKey( edge.SourceId ) ) outDegree[edge.SourceId]++;
            if ( inDegree.ContainsKey( edge.TargetId ) ) inDegree[edge.TargetId]++;
        }

        var report = new ArchitectureMetricsReport
        {
            TotalNodes = graph.Nodes.Count,
            TotalEdges = graph.Edges.Count,
            TopHubs = inDegree.OrderByDescending( kv => kv.Value ).Take( topCount ).ToList(),
            TopGodNodes = outDegree.OrderByDescending( kv => kv.Value ).Take( topCount ).ToList(),
            IsolatedNodes = graph.Nodes.Keys.Where( k => inDegree[k] == 0 && outDegree[k] == 0 ).ToList()
        };

        return report;
    }
}