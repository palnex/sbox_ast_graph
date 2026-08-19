using System;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Core;

/// <summary>
/// Manages 2D viewport camera transformation, zoom bounds, and coordinate spaces.
/// </summary>
public sealed class CanvasTransform
{
    /// <summary>
    /// Camera panning offset in screen pixels.
    /// </summary>
    public Vector2 PanOffset { get; set; } = Vector2.Zero;

    /// <summary>
    /// Active zoom scale factor (1.0 = 100%).
    /// </summary>
    public float Zoom { get; set; } = 1.0f;

    /// <summary>
    /// Minimum allowed zoom factor.
    /// </summary>
    public float MinZoom { get; set; } = 0.1f;

    /// <summary>
    /// Maximum allowed zoom factor.
    /// </summary>
    public float MaxZoom { get; set; } = 3.0f;

    /// <summary>
    /// Viewport widget dimensions in screen pixels.
    /// </summary>
    public Vector2 ViewportSize { get; set; } = new( 800f, 600f );

    /// <summary>
    /// Converts a screen/mouse local pixel coordinate to Canvas world space.
    /// </summary>
    public Vector2 ScreenToWorld( Vector2 screenPos )
    {
        Vector2 center = ViewportSize * 0.5f;
        return (screenPos - center - PanOffset) / Zoom;
    }

    /// <summary>
    /// Converts a Canvas world space coordinate to Widget screen pixel space.
    /// </summary>
    public Vector2 WorldToScreen( Vector2 worldPos )
    {
        Vector2 center = ViewportSize * 0.5f;
        return (worldPos * Zoom) + PanOffset + center;
    }

    /// <summary>
    /// Converts a world-space rectangle to screen-space pixel bounds.
    /// </summary>
    public Rect WorldToScreen( in Rect worldRect )
    {
        Vector2 screenPos = WorldToScreen( worldRect.Position );
        return new Rect( screenPos, worldRect.Size * Zoom );
    }

    /// <summary>
    /// Calculates the visible canvas bounds in world space.
    /// </summary>
    public Rect GetVisibleWorldRect( float margin = 100f )
    {
        Vector2 topLeft = ScreenToWorld( Vector2.Zero );
        Vector2 bottomRight = ScreenToWorld( ViewportSize );

        float minX = MathF.Min( topLeft.x, bottomRight.x ) - margin;
        float minY = MathF.Min( topLeft.y, bottomRight.y ) - margin;
        float maxX = MathF.Max( topLeft.x, bottomRight.x ) + margin;
        float maxY = MathF.Max( topLeft.y, bottomRight.y ) + margin;

        return new Rect( minX, minY, maxX - minX, maxY - minY );
    }

    /// <summary>
	/// Checks if a world-space bounding box overlaps the visible screen viewport (Frustum Culling).
	/// </summary>
	public bool IsWorldRectVisible( in Rect worldRect, in Rect visibleWorldRect )
    {
        return worldRect.Left <= visibleWorldRect.Right &&
               worldRect.Right >= visibleWorldRect.Left &&
               worldRect.Top <= visibleWorldRect.Bottom &&
               worldRect.Bottom >= visibleWorldRect.Top;
    }

    /// <summary>
    /// Centers the camera smoothly on a given world position.
    /// </summary>
    public void CenterOn( Vector2 targetWorldPos )
    {
        PanOffset = -targetWorldPos * Zoom;
    }

    /// <summary>
    /// Zooms centered on a specific screen pivot point (e.g. mouse cursor position).
    /// </summary>
    public void ZoomAt( Vector2 screenPivot, float zoomDeltaFactor )
    {
        float targetZoom = Math.Clamp( Zoom * zoomDeltaFactor, MinZoom, MaxZoom );
        if ( MathF.Abs( targetZoom - Zoom ) < 0.0001f ) return;

        Vector2 worldBefore = ScreenToWorld( screenPivot );
        Zoom = targetZoom;
        Vector2 worldAfter = ScreenToWorld( screenPivot );

        PanOffset += (worldAfter - worldBefore) * Zoom;
    }
}