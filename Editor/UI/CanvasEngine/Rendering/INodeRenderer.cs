#nullable enable
using ArchitectureVisualizer.UI.CanvasEngine.Models;

namespace ArchitectureVisualizer.UI.CanvasEngine.Rendering;

/// <summary>
/// Pluggable renderer contract for drawing CanvasNode elements.
/// </summary>
public interface INodeRenderer
{
    /// <summary>
    /// Draws a node onto the canvas using Editor.Paint.
    /// </summary>
    void RenderNode( PaintContext ctx, CanvasNode node );
}