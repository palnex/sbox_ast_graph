#nullable enable
using System.Collections.Generic;
using Editor.Analysis.Internal.Navigation;

namespace Editor.Analysis.Models.Blocks;

/// <summary>
/// Unified Fractal CodeBlock unifying Body (Anatomy), Wires (Semantics), and Activity (Telemetry).
/// </summary>
public class NodeBlock
{
    public FractalLevel Level { get; set; } = FractalLevel.Class;

    /// <summary> 1. BODY: Static Identity and Location </summary>
    public BodyBlock Body { get; set; } = new();

    /// <summary> 2. WIRES: Semantic Connections (Agent -> Action -> Recipient) </summary>
    public RelationBlock Relations { get; set; } = new();

    /// <summary> 3. ACTIVITY: Live Telemetry and Thermal Pulse </summary>
    public TelemetryActivity Activity { get; set; } = new();

    /// <summary> Custom attributes ([Property], [RequireComponent], [Rpc]) </summary>
    public AttributeBlock Attributes { get; set; } = new();

    /// <summary> Declared properties, methods, and fields </summary>
    public MemberBlock Members { get; set; } = new();

    /// <summary> Fractal Child Entities (e.g. Methods inside a Class) </summary>
    public List<NodeBlock> Children { get; set; } = new();

    // Direct Convenience Shortcuts
    public string DocId => Body.DocId;
    public string Id => Body.DocId;
    public string Name => Body.Name;
    public BodyBlock Header => Body; // Compatibility with HeaderBlock

    /// <summary> Opens the source code of this entity directly in VS Code / Rider. </summary>
    public bool OpenInEditor()
    {
        return CodeNavigator.OpenFile( Body.FilePath, Body.LineNumber );
    }

    public override string ToString() => $"[{Body.Origin}:{Body.Category}] {Body.DocId}";
}