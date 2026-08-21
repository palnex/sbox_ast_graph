#nullable enable
using System;
using System.Collections.Generic;
using ArchitectureVisualizer.UI.CanvasEngine.Core;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Rendering;

/// <summary>
/// Unified high-performance vector renderer with Z-Layer stratification, neon additive halos, and theme-driven scaling.
/// </summary>
public sealed class GraphCanvasRenderer
{
    public void Render( PaintContext ctx, SpatialRegistry registry, IReadOnlyList<CanvasEdge> edges )
    {
        var spatials = registry.GetReadOnlySpatialSpan();
        int nodeCount = spatials.Length;
        int edgeCount = edges.Count;

        // 1. Draw Edges Pass
        Paint.Antialiasing = false; // Fast line rasterization
        Paint.ClearBrush();

        for ( int i = 0; i < edgeCount; i++ )
        {
            var edge = edges[i];
            ref readonly var src = ref spatials[edge.SourceIndex];
            ref readonly var dst = ref spatials[edge.TargetIndex];

            if ( src.IsHidden || dst.IsHidden ) continue;

            Rect edgeBounds = Rect.FromPoints( src.Position, dst.Position ).Grow( 40f );
            if ( !ctx.Transform.IsWorldRectVisible( edgeBounds, ctx.VisibleWorldRect ) )
                continue;

            Vector2 p0 = ctx.Transform.WorldToScreen( src.Position );
            Vector2 p1 = ctx.Transform.WorldToScreen( dst.Position );

            bool inFocus = ctx.IsEdgeInFocus( edge );
            float alphaMult = inFocus ? 1.0f : 0.08f;

            Color edgeColor = edge.CustomColor ?? ctx.Theme.DefaultEdgeColor;
            if ( inFocus && ctx.HasActiveFocus )
            {
                edgeColor = (src.IsSelected || dst.IsSelected) ? ctx.Theme.SelectionColor : ctx.Theme.HoverColor;
            }

            Color strokeColor = edgeColor.WithAlpha( edgeColor.a * alphaMult );

            // 1. Link Thickness Multiplier
            float baseWidth = inFocus && ctx.HasActiveFocus ? 2.0f : 1.0f;
            float strokeWidth = MathF.Max( 0.5f, baseWidth * ctx.Theme.LinkThicknessScale );

            Paint.SetPen( strokeColor, strokeWidth );
            Paint.DrawLine( p0, p1 );
        }

        // 2. Draw Nodes Pass
        Paint.Antialiasing = true;

        for ( int i = 0; i < nodeCount; i++ )
        {
            ref readonly var node = ref spatials[i];
            if ( node.IsHidden ) continue;

            // 2. Dynamic Node Size Scaling
            float scaledWorldRadius = node.Radius * ctx.Theme.NodeSizeScale;
            Rect nodeBounds = new( node.Position - new Vector2( scaledWorldRadius ), new Vector2( scaledWorldRadius * 2f ) );
            if ( !ctx.Transform.IsWorldRectVisible( nodeBounds, ctx.VisibleWorldRect ) )
                continue;

            Vector2 screenPos = ctx.Transform.WorldToScreen( node.Position );
            float screenRadius = MathF.Max( 3.0f, scaledWorldRadius * ctx.Transform.Zoom );

            bool inFocus = ctx.IsNodeInFocus( i );
            float alphaMult = inFocus ? 1.0f : 0.10f;
            var payload = registry.GetPayload( i );

            // A. Neon Glow Halo Pass (Subtle & Clean)
            if ( node.IsSelected )
            {
                Paint.ClearPen();
                Paint.SetBrush( ctx.Theme.SelectionColor.WithAlpha( 0.25f * alphaMult ) );
                Paint.DrawCircle( screenPos, screenRadius + 6f );
            }
            else if ( node.IsHovered )
            {
                Paint.ClearPen();
                Paint.SetBrush( ctx.Theme.HoverColor.WithAlpha( 0.25f * alphaMult ) );
                Paint.DrawCircle( screenPos, screenRadius + 4f );
            }
            else if ( inFocus && ctx.HasActiveFocus )
            {
                Paint.ClearPen();
                Paint.SetBrush( payload.AccentColor.WithAlpha( 0.15f ) );
                Paint.DrawCircle( screenPos, screenRadius + 3f );
            }

            // B. Core Body Pass
            Color bodyColor = node.IsSelected ? ctx.Theme.SelectionColor :
                              node.IsHovered ? ctx.Theme.HoverColor :
                              payload.AccentColor;

            Paint.ClearPen();
            Paint.SetBrush( bodyColor.WithAlpha( bodyColor.a * alphaMult ) );

            if ( node.Shape == NodeShape.Box )
            {
                Rect boxRect = new( screenPos - new Vector2( screenRadius ), new Vector2( screenRadius * 2f ) );
                Paint.DrawRect( boxRect, 4f );
            }
            else
            {
                Paint.DrawCircle( screenPos, screenRadius );
            }

            // C. Label Typography Pass (With Text Fade Threshold)
            bool isPrimary = node.IsHovered || node.IsSelected;
            bool isNeighbor = inFocus && ctx.HasActiveFocus;
            int neighborCount = ctx.FocusedNeighborIndices?.Count ?? 0;

            bool allowNeighborLabel = isNeighbor && (neighborCount < 18 || ctx.Transform.Zoom > 1.25f);
            bool isZoomAboveThreshold = ctx.Transform.Zoom >= ctx.Theme.TextFadeThreshold;

            bool shouldShowLabel = (isPrimary || (allowNeighborLabel && isZoomAboveThreshold)) && !ctx.IsLowDetail;

            if ( shouldShowLabel )
            {
                int fontSize = (int)Math.Clamp( (isPrimary ? 13f : 10f) * ctx.Transform.Zoom, 8f, 15f );
                Paint.SetFont( "Segoe UI", fontSize, isPrimary ? 700 : 500 );

                Color textColor = isPrimary ? Color.White : ctx.Theme.TextColor.WithAlpha( 0.85f );
                Paint.SetPen( textColor );

                Rect labelRect = new( screenPos.x - 100f, screenPos.y + screenRadius + 3f, 200f, fontSize + 6 );
                Paint.DrawText( labelRect, payload.Title, TextFlag.Center );
            }
        }
    }
}