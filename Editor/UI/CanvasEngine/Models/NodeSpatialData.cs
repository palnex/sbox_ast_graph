#nullable enable
using System.Runtime.InteropServices;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Models;

/// <summary>
/// Compact 28-byte cache-friendly spatial data layout for ultra-fast physics and vector batching.
/// </summary>
[StructLayout( LayoutKind.Sequential, Pack = 4 )]
public struct NodeSpatialData
{
    /// <summary>
    /// 2D Canvas world-space position.
    /// </summary>
    public Vector2 Position; // 8 bytes

    /// <summary>
    /// Linear physics velocity vector.
    /// </summary>
    public Vector2 Velocity; // 8 bytes

    /// <summary>
    /// Geometric interaction radius in canvas world units.
    /// </summary>
    public float Radius; // 4 bytes

    /// <summary>
    /// Architectural elevation / stratum level (0 = leaf/unused, higher = core hubs/god classes).
    /// </summary>
    public ushort ZLevel; // 2 bytes

    /// <summary>
    /// Geometric shape type used for rendering and analytical hit testing.
    /// </summary>
    public NodeShape Shape; // 1 byte

    /// <summary>
    /// Interactive bit-flags (Pinned, Hovered, Selected, Dimmed).
    /// </summary>
    public NodeFlags Flags; // 1 byte

    /// <summary>
    /// Index pointing to the rich metadata payload in the payload registry.
    /// </summary>
    public int PayloadIndex; // 4 bytes

    // ================= HELPER PROPERTIES =================

    public bool IsPinned => (Flags & NodeFlags.Pinned) != 0;
    public bool IsHovered => (Flags & NodeFlags.Hovered) != 0;
    public bool IsSelected => (Flags & NodeFlags.Selected) != 0;
    public bool IsDimmed => (Flags & NodeFlags.Dimmed) != 0;
    public bool IsFocusedNeighbor => (Flags & NodeFlags.FocusedNeighbor) != 0;
    public bool IsHidden => (Flags & NodeFlags.Hidden) != 0;

    public void SetFlag( NodeFlags flag, bool enable )
    {
        if ( enable ) Flags |= flag;
        else Flags &= ~flag;
    }
}