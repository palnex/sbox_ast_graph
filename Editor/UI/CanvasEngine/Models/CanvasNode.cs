#nullable enable
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Models;

/// <summary>
/// Represents a visual interactive node on the 2D Canvas.
/// </summary>
public sealed class CanvasNode
{
    /// <summary>
    /// Unique identifier for this node.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Primary title displayed on the node header.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Subtitle or namespace displayed below title.
    /// </summary>
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>
	/// Total number of connected edges (in-degree + out-degree).
	/// </summary>
	public int Degree { get; set; } = 1;

    /// <summary>
    /// Material/editor icon name (e.g. "category", "code", "schema").
    /// </summary>
    public string Icon { get; set; } = "circle";

    /// <summary>
    /// Category accent color (e.g. Green for SceneComponent, Blue for UI).
    /// </summary>
    public Color AccentColor { get; set; } = Theme.Primary;

    /// <summary>
    /// Canvas world space position (top-left or center based on renderer).
    /// </summary>
    public Vector2 Position { get; set; } = Vector2.Zero;

    /// <summary>
    /// Current velocity for physics relaxation solver.
    /// </summary>
    public Vector2 Velocity { get; set; } = Vector2.Zero;

    /// <summary>
    /// Accumulated force in the current physics simulation tick.
    /// </summary>
    public Vector2 AccumulatedForce { get; set; } = Vector2.Zero;

    /// <summary>
    /// Physics mass factor (higher mass resists movement).
    /// </summary>
    public float Mass { get; set; } = 1.0f;

    /// <summary>
    /// Dimensions of the node card in world units.
    /// </summary>
    public Vector2 Size { get; set; } = new( 180f, 60f );

    /// <summary>
    /// If true, this node is locked in space and unaffected by physics forces.
    /// </summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// If true, the user is currently hovering the mouse over this node.
    /// </summary>
    public bool IsHovered { get; set; }

    /// <summary>
    /// If true, this node is actively selected in the editor.
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// If true, this node is actively being dragged by mouse.
    /// </summary>
    public bool IsDragging { get; set; }

    /// <summary>
    /// Optional user payload (e.g. original GraphNode from Core engine).
    /// </summary>
    public object? UserData { get; set; }

    /// <summary>
    /// Calculates the axis-aligned bounding box (AABB) in world space.
    /// </summary>
    public Rect GetWorldBounds() => new( Position, Size );

    /// <summary>
    /// Returns the center point in world space.
    /// </summary>
    public Vector2 Center => Position + (Size * 0.5f);
}