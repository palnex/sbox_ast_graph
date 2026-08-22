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
    public float StrokeWidth { get; set; } = 1.8f;
    public float DesiredSpringLength { get; set; } = 220f;
    public ushort ZLevel { get; set; } = 0;

    /// <summary>
    /// Visual pattern rendered by the GPU shader (Solid, Dashed, Arrows, Laser).
    /// </summary>
    public EdgeStyle Style { get; set; } = EdgeStyle.Solid;

    /// <summary>
    /// Speed and direction of pattern animation (positive = forward, negative = reverse, 0 = static).
    /// </summary>
    public float FlowSpeed { get; set; } = 1.0f;

    /// <summary>
    /// Multiplier for laser pulse brightness or emission.
    /// </summary>
    public float PulseIntensity { get; set; } = 1.0f;

    public object? UserData { get; set; }

    public CanvasEdge( int sourceIndex, int targetIndex )
    {
        SourceIndex = sourceIndex;
        TargetIndex = targetIndex;
    }
}