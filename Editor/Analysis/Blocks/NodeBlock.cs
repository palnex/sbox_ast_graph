#nullable enable
using Editor.Analysis.Internal.Navigation;

namespace Editor.Analysis.Models.Blocks;

/// <summary>
/// Unified Node Data Container combining all structured data blocks.
/// </summary>
public class NodeBlock
{
    /// <summary>
    /// Identity, display title, icon, summary, and source code location.
    /// </summary>
    public HeaderBlock Header { get; set; } = new();

    /// <summary>
    /// Custom attributes decorating the type.
    /// </summary>
    public AttributeBlock Attributes { get; set; } = new();

    /// <summary>
    /// Declared properties, methods, and fields.
    /// </summary>
    public MemberBlock Members { get; set; } = new();

    /// <summary>
    /// Incoming and outgoing dependency connections.
    /// </summary>
    public RelationBlock Relations { get; set; } = new();

    /// <summary>
    /// Unique identifier shortcut.
    /// </summary>
    public string Id => Header.Id;

    /// <summary>
    /// Short name shortcut.
    /// </summary>
    public string Name => Header.Name;

    /// <summary>
    /// Opens the source code of this type directly in the user's IDE at the declaration line.
    /// </summary>
    public bool OpenInEditor()
    {
        return CodeNavigator.OpenFile( Header.FilePath, Header.LineNumber );
    }

    public override string ToString() => $"[{Header.Origin}:{Header.Category}] {Header.Id}";
}