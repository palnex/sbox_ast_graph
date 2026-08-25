#nullable enable
using System.Collections.Generic;

namespace Editor.Analysis.Models.Blocks;

/// <summary>
/// Directed dependency link between two nodes.
/// </summary>
public class GraphEdge
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public RelationKind Kind { get; set; } = RelationKind.MethodCall;
    public string Details { get; set; } = string.Empty;
    public int LineNumber { get; set; }

    public override string ToString() => $"[{SourceId}] ──({Kind}: {Details})──> [{TargetId}]";
}

/// <summary>
/// Data block managing all incoming and outgoing connections for a specific node.
/// </summary>
public class RelationBlock
{
    public List<GraphEdge> Outgoing { get; set; } = new();
    public List<GraphEdge> Incoming { get; set; } = new();

    public int OutgoingCount => Outgoing.Count;
    public int IncomingCount => Incoming.Count;
}