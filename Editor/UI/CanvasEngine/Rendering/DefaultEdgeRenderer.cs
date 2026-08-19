#nullable enable
using System;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Rendering;

/// <summary>
/// Default renderer for drawing smooth cubic Bézier connections with directional arrows and clamped labels.
/// </summary>
public sealed class DefaultEdgeRenderer : IEdgeRenderer
{
    private const int BezierSegments = 20;
    private readonly Vector2[] _splinePoints = new Vector2[BezierSegments + 1];

    public void RenderEdge( PaintContext ctx, CanvasEdge edge )
    {
        Vector2 p0World = edge.Source.Center;
        Vector2 p3World = edge.Target.Center;

        Vector2 p0 = ctx.Transform.WorldToScreen( p0World );
        Vector2 p3 = ctx.Transform.WorldToScreen( p3World );

        // Calculate Bézier control points (smooth horizontal curvature)
        float deltaX = MathF.Abs( p3.x - p0.x ) * 0.5f;
        float offset = MathF.Max( 40f * ctx.Transform.Zoom, deltaX );

        Vector2 p1 = p0 + new Vector2( offset, 0 );
        Vector2 p2 = p3 - new Vector2( offset, 0 );

        // Evaluate Cubic Bézier curve
        for ( int i = 0; i <= BezierSegments; i++ )
        {
            float t = (float)i / BezierSegments;
            float u = 1.0f - t;
            _splinePoints[i] = (u * u * u * p0) + (3f * u * u * t * p1) + (3f * u * t * t * p2) + (t * t * t * p3);
        }

        Color strokeColor = edge.CustomColor ?? (edge.IsHighlighted ? ctx.Theme.HoverColor : ctx.Theme.DefaultEdgeColor);
        float strokeWidth = edge.IsHighlighted ? (edge.StrokeWidth * 1.5f) : edge.StrokeWidth;

        Paint.Antialiasing = true;
        Paint.ClearBrush();
        Paint.SetPen( strokeColor, strokeWidth, PenStyle.Solid );
        Paint.DrawLine( _splinePoints );

        // Draw Midpoint Label Badge if exists and not low detail
        if ( !string.IsNullOrEmpty( edge.Label ) && !ctx.IsLowDetail )
        {
            Vector2 midPoint = _splinePoints[BezierSegments / 2];
            DrawEdgeBadge( midPoint, edge.Label, strokeColor, ctx );
        }
    }

    private static void DrawEdgeBadge( Vector2 centerPos, string label, Color accentColor, PaintContext ctx )
    {
        int fontSize = (int)Math.Clamp( 10f * ctx.Transform.Zoom, 8f, 12f );
        Paint.SetFont( "Segoe UI", fontSize, 600 );

        Rect measureBox = new( 0, 0, 300, 40 );
        Rect textBounds = Paint.MeasureText( measureBox, label, TextFlag.Center );

        Vector2 badgeSize = textBounds.Size + new Vector2( 12f, 6f );
        Rect badgeRect = new( centerPos - (badgeSize * 0.5f), badgeSize );

        // Badge Pill Background
        Paint.ClearPen();
        Paint.SetBrush( ctx.Theme.NodeBackgroundColor );
        Paint.DrawRect( badgeRect, 4f );

        // Badge Pill Outline
        Paint.SetPen( accentColor.WithAlpha( 0.7f ), 1.0f );
        Paint.ClearBrush();
        Paint.DrawRect( badgeRect, 4f );

        // Badge Text
        Paint.SetPen( ctx.Theme.TextColor );
        Paint.DrawText( badgeRect, label, TextFlag.Center );
    }
}