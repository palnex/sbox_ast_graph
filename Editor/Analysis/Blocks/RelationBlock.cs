#nullable enable
using System.Collections.Generic;

namespace Editor.Analysis.Models.Blocks;

/// <summary>
/// Semantic relation container managing all incoming, outgoing, and polymorphic dispatch wires.
/// </summary>
public class RelationBlock
{
    public List<SemanticWire> Outgoing { get; set; } = new();
    public List<SemanticWire> Incoming { get; set; } = new();

    public int OutgoingCount => Outgoing.Count;
    public int IncomingCount => Incoming.Count;
}

// Backward compatibility alias
public class GraphEdge : SemanticWire
{
}