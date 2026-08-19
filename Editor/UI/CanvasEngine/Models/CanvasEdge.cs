#nullable enable
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Models;

/// <summary>
/// Represents a directed visual connection between two CanvasNodes.
/// </summary>
public sealed class CanvasEdge
{
    /// <summary>
    /// Source node originating the connection.
    /// </summary>
    public CanvasNode Source { get; }

    /// <summary>
    /// Target node receiving the connection.
    /// </summary>
    public CanvasNode Target { get; }

    /// <summary>
    /// Optional label displayed along the curve (e.g. method name, count).
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Custom stroke color. If null, theme default will be used.
    /// </summary>
    public Color? CustomColor { get; set; }

    /// <summary>
    /// Custom stroke width.
    /// </summary>
    public float StrokeWidth { get; set; } = 2.0f;

    /// <summary>
    /// Whether this edge is currently highlighted (e.g. hovered chain).
    /// </summary>
    public bool IsHighlighted { get; set; }

    /// <summary>
    /// Desired natural spring rest length for physics solver.
    /// </summary>
    public float DesiredSpringLength { get; set; } = 220f;

    /// <summary>
    /// Optional user payload (e.g. original GraphEdge).
    /// </summary>
    public object? UserData { get; set; }

    public CanvasEdge( CanvasNode source, CanvasNode target )
    {
        Source = source;
        Target = target;
    }
}