namespace Editor.Core.Models;

/// <summary>
/// Represents a directed dependency link from a Source node to a Target node.
/// </summary>
public class GraphEdge
{
    /// <summary>
    /// Identifier of the caller or referencing node.
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the target dependency node being referenced.
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// Specific kind of relationship (Call, Inheritance, Field, Event, etc.).
    /// </summary>
    public RelationKind Kind { get; set; } = RelationKind.MethodCall;

    /// <summary>
    /// Human-readable context details (e.g., "Method 'TakeDamage()'", "Field 'weapon'").
    /// </summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>
    /// Source code line number where the dependency was detected (if available).
    /// </summary>
    public int LineNumber { get; set; }

    public override string ToString()
    {
        return $"[{SourceId}] ──({Kind}: {Details})──> [{TargetId}]";
    }
}