#nullable enable
using System;

namespace ArchitectureVisualizer.UI.CanvasEngine.Models;

/// <summary>
/// Defines the geometric shape used for rendering and analytical hit-testing.
/// </summary>
public enum NodeShape : byte
{
    Circle = 0,
    Box = 1,
    Pill = 2,
    Custom = 3
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