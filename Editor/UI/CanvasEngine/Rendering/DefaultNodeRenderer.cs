#nullable enable
using System;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Rendering;

/// <summary>
/// Lightweight, high-performance circular node renderer (Obsidian Graph style).
/// </summary>
public sealed class DefaultNodeRenderer : INodeRenderer
{
    public void RenderNode( PaintContext ctx, CanvasNode node )
    {
        Vector2 screenPos = ctx.Transform.WorldToScreen( node.Center );
        float radius = MathF.Max( 4f, (6f + node.Mass * 1.5f) * ctx.Transform.Zoom );

        Paint.Antialiasing = true;

        // 1. Selection / Hover Halo
        if ( node.IsSelected )
        {
            Paint.ClearPen();
            Paint.SetBrush( ctx.Theme.SelectionColor.WithAlpha( 0.4f ) );
            Paint.DrawCircle( screenPos, radius + 5f );
        }
        else if ( node.IsHovered )
        {
            Paint.ClearPen();
            Paint.SetBrush( ctx.Theme.HoverColor.WithAlpha( 0.35f ) );
            Paint.DrawCircle( screenPos, radius + 3f );
        }

        // 2. Node Core Circle
        Color nodeColor = node.IsSelected ? ctx.Theme.SelectionColor :
                          node.IsHovered ? ctx.Theme.HoverColor :
                          node.AccentColor;

        Paint.ClearPen();
        Paint.SetBrush( nodeColor );
        Paint.DrawCircle( screenPos, radius );

        // 3. Node Title Label (Obsidian-style: only on zoom or if hovered/selected)
        if ( (ctx.Transform.Zoom > 0.8f || node.IsHovered || node.IsSelected) && !ctx.IsLowDetail )
        {
            int fontSize = (int)Math.Clamp( 11f * ctx.Transform.Zoom, 9f, 13f );
            Paint.SetFont( "Segoe UI", fontSize, 600 );
            Paint.SetPen( ctx.Theme.TextColor );

            Rect labelRect = new( screenPos.x - 80f, screenPos.y + radius + 2f, 160f, fontSize + 4 );
            Paint.DrawText( labelRect, node.Title, TextFlag.Center );
        }
    }
}





/* using System;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Rendering;

/// <summary>
/// Default clean vector renderer for node cards with headers, icons, pins, and selection halos.
/// </summary>
public sealed class DefaultNodeRenderer : INodeRenderer
{
    public void RenderNode( PaintContext ctx, CanvasNode node )
    {
        Rect screenRect = ctx.Transform.WorldToScreen( node.GetWorldBounds() );

        Paint.Antialiasing = true;

        // 1. Draw Selection or Hover Halo / Glow
        if ( node.IsSelected )
        {
            DrawHalo( screenRect, ctx.Theme.SelectionColor.WithAlpha( 0.4f ), radius: 6f );
        }
        else if ( node.IsHovered )
        {
            DrawHalo( screenRect, ctx.Theme.HoverColor.WithAlpha( 0.3f ), radius: 4f );
        }

        // 2. Draw Card Body Background
        Paint.ClearPen();
        Paint.SetBrush( ctx.Theme.NodeBackgroundColor );
        Paint.DrawRect( screenRect, ctx.Theme.NodeCornerRadius );

        // 3. Draw Category Accent Top Strip
        float stripHeight = MathF.Max( 3f, 4f * ctx.Transform.Zoom );
        Rect stripRect = new( screenRect.Position, new Vector2( screenRect.Width, stripHeight ) );
        Paint.SetBrush( node.AccentColor );
        Paint.DrawRect( stripRect, 2f );

        // 4. Draw Card Border Outline
        Color outlineColor = node.IsSelected ? ctx.Theme.SelectionColor :
                             node.IsHovered ? ctx.Theme.HoverColor :
                             ctx.Theme.NodeBorderColor;
        float strokeWidth = node.IsSelected ? 2.0f : 1.0f;
        Paint.SetPen( outlineColor, strokeWidth );
        Paint.ClearBrush();
        Paint.DrawRect( screenRect, ctx.Theme.NodeCornerRadius );

        // 5. Draw Pin Indicator if locked
        if ( node.IsPinned )
        {
            Rect pinRect = new( screenRect.TopRight - new Vector2( 18f, -4f ), new Vector2( 14f, 14f ) );
            Paint.SetPen( ctx.Theme.PinnedIndicatorColor );
            Paint.DrawIcon( pinRect, "push_pin", 12f, TextFlag.Center );
        }

        // 6. Draw Content (Text & Icons) - Skip if zoomed out far (LOD)
        if ( ctx.IsLowDetail ) return;

        float pad = MathF.Max( 6f, 8f * ctx.Transform.Zoom );
        float iconSize = MathF.Max( 14f, 18f * ctx.Transform.Zoom );

        // Icon
        Rect iconRect = new( screenRect.Left + pad, screenRect.Top + pad + 2f, iconSize, iconSize );
        Paint.SetPen( node.AccentColor );
        Paint.DrawIcon( iconRect, node.Icon, iconSize, TextFlag.Center );

        // Title
        float textLeft = iconRect.Right + pad * 0.75f;
        float textWidth = MathF.Max( 10f, screenRect.Right - textLeft - pad );
        int titleFontSize = (int)Math.Clamp( 12f * ctx.Transform.Zoom, 9f, 15f );

        Paint.SetHeadingFont( titleFontSize, 600 );
        Paint.SetPen( ctx.Theme.TextColor );
        Rect titleRect = new( textLeft, screenRect.Top + pad, textWidth, titleFontSize + 4 );
        string elidedTitle = Paint.GetElidedText( node.Title, textWidth, ElideMode.Right, TextFlag.Left );
        Paint.DrawText( titleRect, elidedTitle, TextFlag.Left );

        // Subtitle / Namespace
        if ( !string.IsNullOrEmpty( node.Subtitle ) && screenRect.Height > 45f )
        {
            int subFontSize = (int)Math.Clamp( 10f * ctx.Transform.Zoom, 8f, 12f );
            Paint.SetFont( "Segoe UI", subFontSize, 400 );
            Paint.SetPen( ctx.Theme.TextMutedColor );
            Rect subRect = new( textLeft, titleRect.Bottom, textWidth, subFontSize + 4 );
            string elidedSub = Paint.GetElidedText( node.Subtitle, textWidth, ElideMode.Right, TextFlag.Left );
            Paint.DrawText( subRect, elidedSub, TextFlag.Left );
        }
    }

    private static void DrawHalo( in Rect rect, Color haloColor, float radius )
    {
        Paint.ClearPen();
        Paint.SetBrush( haloColor );
        Paint.DrawRect( rect.Grow( radius ), 8f );
    }
} */