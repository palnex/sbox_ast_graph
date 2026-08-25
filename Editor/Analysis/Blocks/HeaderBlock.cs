#nullable enable

namespace Editor.Analysis.Models.Blocks;

/// <summary>
/// Primary metadata header block describing identity, display info, and source location.
/// </summary>
public class HeaderBlock
{
    /// <summary>
    /// Fully qualified unique identifier (e.g., "Sandbox.Component", "MyGame.PlayerController").
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Short identifier name (e.g., "PlayerController").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Namespace containing this type.
    /// </summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable title from DisplayInfo or fallback to Name.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Documentation summary extracted from XML comments (/// &lt;summary&gt;) or [Description].
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Google Material Icon name from [Icon] or DisplayInfo (e.g., "sports_martial_arts", "rocket_launch").
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Group or category hierarchy from [Group] or [Category].
    /// </summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// Where this type originates from (UserProject, EngineRuntime, etc.).
    /// </summary>
    public NodeOrigin Origin { get; set; } = NodeOrigin.UserProject;

    /// <summary>
    /// Functional category (SceneComponent, UiPanel, Class, Struct, etc.).
    /// </summary>
    public SandboxTypeCategory Category { get; set; } = SandboxTypeCategory.Class;

    /// <summary>
    /// Absolute file path on disk if this node originates from source code.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Source code declaration line number.
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Indicates if the type is declared abstract.
    /// </summary>
    public bool IsAbstract { get; set; }

    /// <summary>
    /// Indicates if the type is a static class.
    /// </summary>
    public bool IsStatic { get; set; }

    /// <summary>
    /// Indicates if the type is a struct/value type.
    /// </summary>
    public bool IsValueType { get; set; }

    /// <summary>
    /// Indicates if this node originates from user project code.
    /// </summary>
    public bool IsUserCode => Origin == NodeOrigin.UserProject;

    /// <summary>
    /// Indicates if this node originates from the engine runtime or editor.
    /// </summary>
    public bool IsEngineCode => Origin is NodeOrigin.EngineRuntime or NodeOrigin.EngineEditor;
}