#nullable enable
using System;

namespace ArchitectureVisualizer.UI.CanvasEngine.Models;

/// <summary>
/// Geometric analytical shapes supported by the GPU SDF node shader.
/// </summary>
public enum NodeShape : byte
{
    Circle = 0,
    Box = 1,
    RoundedBox = 2,
    Hexagon = 3,
    Diamond = 4,
    Ring = 5,
    Star = 6,
    CustomMesh = 100
}

/// <summary>
/// Visual and animation patterns for dynamic graph connections.
/// </summary>
public enum EdgeStyle : byte
{
    Solid = 0,
    Dashed = 1,
    DirectionalArrows = 2,
    DoubleLine = 3,
    LaserPulse = 4,
    Custom = 100
}

/// <summary>
/// Bit-flags representing current interaction and filter state of a node.
/// </summary>
[Flags]
public enum NodeFlags : byte
{
    None = 0,
    Pinned = 1 << 0,
    Hovered = 1 << 1,
    Selected = 1 << 2,
    Dimmed = 1 << 3,
    FocusedNeighbor = 1 << 4,
    Hidden = 1 << 5
}