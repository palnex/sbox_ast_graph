#nullable enable
using System;
using System.Runtime.CompilerServices;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Core;

/// <summary>
/// High-performance zero-allocation geometric hit-testing for Canvas nodes and connections.
/// </summary>
public static class SpatialHitTester
{
    /// <summary>
    /// Exact squared distance test for circular nodes.
    /// </summary>
    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static bool HitTestCircle( Vector2 point, Vector2 center, float radius )
    {
        float dx = point.x - center.x;
        float dy = point.y - center.y;
        return (dx * dx + dy * dy) <= (radius * radius);
    }

    /// <summary>
    /// Exact axis-aligned bounding box (AABB) hit test for rectangular cards.
    /// </summary>
    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static bool HitTestBox( Vector2 point, Rect box )
    {
        return point.x >= box.Left && point.x <= box.Right &&
               point.y >= box.Top && point.y <= box.Bottom;
    }

    /// <summary>
    /// Exact geometric distance test for capsules, pills, and thick connection edges.
    /// Projects point onto segment [segA -> segB] with zero memory allocations.
    /// </summary>
    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static bool HitTestCapsule( Vector2 point, Vector2 segA, Vector2 segB, float radius )
    {
        Vector2 ab = segB - segA;
        Vector2 ap = point - segA;

        float abLenSq = (ab.x * ab.x) + (ab.y * ab.y);
        if ( abLenSq < 0.0001f )
            return HitTestCircle( point, segA, radius );

        // Project point onto line segment AB, clamped to [0, 1]
        float t = Math.Clamp( ((ap.x * ab.x) + (ap.y * ab.y)) / abLenSq, 0.0f, 1.0f );
        Vector2 closestPoint = segA + (ab * t);

        float dx = point.x - closestPoint.x;
        float dy = point.y - closestPoint.y;
        return ((dx * dx) + (dy * dy)) <= (radius * radius);
    }
}