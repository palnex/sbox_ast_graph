#nullable enable

namespace Editor.Analysis.Models.Blocks;

/// <summary>
/// 5-Dimensional Semantic Case Frame Wire: Agent -> Action -> Recipient [Instrument, Condition]
/// </summary>
public class SemanticWire
{
    /// <summary> AGENT: Unique DocId of the caller entity. </summary>
    public string AgentDocId { get; set; } = string.Empty;

    /// <summary> ACTION: Semantic relationship verb. </summary>
    public RelationKind Action { get; set; } = RelationKind.MethodCall;

    /// <summary> RECIPIENT: Unique DocId of the receiving entity. </summary>
    public string RecipientDocId { get; set; } = string.Empty;

    /// <summary> INSTRUMENT: Payload data types, arguments or return types passed across the boundary. </summary>
    public string Instrument { get; set; } = string.Empty;

    /// <summary> CONDITION: Contextual guards, network realm or execution attributes ([Host], [Authority]). </summary>
    public string Condition { get; set; } = string.Empty;

    /// <summary> Source code declaration line where the wire originates. </summary>
    public int LineNumber { get; set; }

    /// <summary> True if this is a ghost/dashed polymorphic dispatch link to a concrete interface implementation. </summary>
    public bool IsPolymorphicFanout { get; set; }

    // Compatibility Shortcuts for Graph Adapters
    public string SourceId => AgentDocId;
    public string TargetId => RecipientDocId;
    public RelationKind Kind => Action;
    public string Details => string.IsNullOrEmpty( Condition ) ? Instrument : $"{Instrument} [{Condition}]";

    public override string ToString() => $"[{AgentDocId}] ──({Action}: {Details})──> [{RecipientDocId}]";
}