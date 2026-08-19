#nullable enable
using System.Collections.Generic;

namespace Editor.Core.Models;

/// <summary>
/// Lightweight metadata descriptor for members (properties, methods) on a node.
/// </summary>
public class MemberMetadata
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsStatic { get; set; }
}

/// <summary>
/// Represents an analyzed type, component, or asset as a vertex in the dependency graph.
/// </summary>
public class GraphNode
{
    /// <summary>
    /// Fully qualified unique identifier (e.g., "Sandbox.Component", "MyGame.PlayerController").
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Short display name of the type (e.g., "PlayerController").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Namespace containing this type.
    /// </summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>
    /// Origin of this node (User Project, Engine Runtime, Editor, System).
    /// </summary>
    public NodeOrigin Origin { get; set; } = NodeOrigin.UserProject;

    /// <summary>
    /// Functional category of the node (Component, Panel, Class, Struct, etc.).
    /// </summary>
    public SandboxTypeCategory Category { get; set; } = SandboxTypeCategory.Class;

    /// <summary>
    /// Human-readable title from DisplayInfo or fallback to Name.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Documentation summary extracted from XML comments (/// &lt;summary&gt;) or [Description] attribute.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Editor icon name extracted from [Icon] attribute or DisplayInfo.
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Group or category hierarchy from [Group] or [Category].
    /// </summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// File path on disk if this node originates from the active user project.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Indicates if the type is declared abstract.
    /// </summary>
    public bool IsAbstract { get; set; }

    /// <summary>
    /// Indicates if the type is a static class.
    /// </summary>
    public bool IsStatic { get; set; }

    /// <summary>
    /// Detailed property members declared on this type.
    /// </summary>
    public List<MemberMetadata> Properties { get; set; } = new();

    /// <summary>
    /// Detailed method members declared on this type.
    /// </summary>
    public List<MemberMetadata> Methods { get; set; } = new();

    public override string ToString()
    {
        return $"[{Origin}:{Category}] {Id}";
    }
}