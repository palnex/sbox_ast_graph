#nullable enable
using System.Collections.Generic;
using ArchitectureVisualizer.UI.CanvasEngine.Core;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Rendering;

/// <summary>
/// Visual execution context passed through the rendering pipeline.
/// </summary>
public sealed class PaintContext
{
    public CanvasTransform Transform { get; }
    public CanvasTheme Theme { get; }
    public Rect VisibleWorldRect { get; }

    public int HoveredNodeIndex { get; set; } = -1;
    public int SelectedNodeIndex { get; set; } = -1;
    public HashSet<int>? FocusedNeighborIndices { get; set; }

    public bool HasActiveFocus => HoveredNodeIndex >= 0 || SelectedNodeIndex >= 0;
    public bool IsLowDetail => Transform.Zoom < 0.45f;

    public PaintContext( CanvasTransform transform, CanvasTheme theme, Rect visibleWorldRect )
    {
        Transform = transform;
        Theme = theme;
        VisibleWorldRect = visibleWorldRect;
    }

    public bool IsNodeInFocus( int nodeIndex )
    {
        if ( !HasActiveFocus ) return true;
        if ( nodeIndex == SelectedNodeIndex || nodeIndex == HoveredNodeIndex ) return true;
        return FocusedNeighborIndices != null && FocusedNeighborIndices.Contains( nodeIndex );
    }

    public bool IsEdgeInFocus( CanvasEdge edge )
    {
        if ( !HasActiveFocus ) return true;
        int active = HoveredNodeIndex >= 0 ? HoveredNodeIndex : SelectedNodeIndex;
        return edge.SourceIndex == active || edge.TargetIndex == active;
    }
}