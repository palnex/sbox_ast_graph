#nullable enable
using ArchitectureVisualizer.UI.CanvasEngine.Core;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Rendering;

/// <summary>
/// Encapsulates the visual environment and camera state during a canvas paint pass.
/// </summary>
public sealed class PaintContext
{
    /// <summary>
    /// Active canvas camera transform (World <-> Screen).
    /// </summary>
    public CanvasTransform Transform { get; }

    /// <summary>
    /// Active styling theme.
    /// </summary>
    public CanvasTheme Theme { get; }

    /// <summary>
    /// Visible viewport rectangle in world coordinates (used for Frustum Culling).
    /// </summary>
    public Rect VisibleWorldRect { get; }

    /// <summary>
    /// Node currently hovered by the mouse cursor, if any.
    /// </summary>
    public CanvasNode? HoveredNode { get; set; }

    /// <summary>
    /// Node currently selected, if any.
    /// </summary>
    public CanvasNode? SelectedNode { get; set; }

    /// <summary>
    /// Whether to draw simplified graphics due to low zoom (LOD optimization).
    /// </summary>
    public bool IsLowDetail => Transform.Zoom < 0.45f;

    public PaintContext( CanvasTransform transform, CanvasTheme theme, Rect visibleWorldRect )
    {
        Transform = transform;
        Theme = theme;
        VisibleWorldRect = visibleWorldRect;
    }
}