#nullable enable
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Models;

/// <summary>
/// Represents a directed connection between two nodes in the SpatialRegistry.
/// </summary>
public sealed class CanvasEdge
{
    public int SourceIndex { get; }
    public int TargetIndex { get; }

    public string? Label { get; set; }
    public Color? CustomColor { get; set; }
    public float StrokeWidth { get; set; } = 1.0f;
    public float DesiredSpringLength { get; set; } = 220f;
    public ushort ZLevel { get; set; } = 0;
    public object? UserData { get; set; }

    public CanvasEdge( int sourceIndex, int targetIndex )
    {
        SourceIndex = sourceIndex;
        TargetIndex = targetIndex;
    }
}