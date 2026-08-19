#nullable enable
using ArchitectureVisualizer.UI.CanvasEngine.Models;

namespace ArchitectureVisualizer.UI.CanvasEngine.Rendering;

/// <summary>
/// Pluggable renderer contract for drawing CanvasEdge connections.
/// </summary>
public interface IEdgeRenderer
{
    /// <summary>
    /// Draws an edge onto the canvas using Editor.Paint.
    /// </summary>
    void RenderEdge( PaintContext ctx, CanvasEdge edge );
}