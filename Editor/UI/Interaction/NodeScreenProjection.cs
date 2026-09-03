#nullable enable
using System;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.Interaction;

/// <summary>
/// Screen-space projection and smart anti-collision bounding math for floating viewport cards.
/// Derived from technical research for Source 2 SceneRenderingWidget.
/// </summary>
public static class NodeScreenProjection
{
    public struct AnchorLayoutResult
    {
        public bool IsVisible;
        public Vector2 ScreenCenter;
        public float ScreenRadiusPx;
        public Rect CardRect;
    }

    /// <summary>
    /// Calculates a local widget rect for the inspector card that never overlaps the target node.
    /// Implements 4-way smart flipping (Right <-> Left, Top <-> Bottom) and margin clamping.
    /// </summary>
    public static AnchorLayoutResult CalculateAnchorLayout(
        CameraComponent camera,
        Vector2 widgetSize,
        Vector3 worldPos,
        float worldRadius,
        Vector2 cardSize,
        float padding = 16f,
        float nodeGap = 16f )
    {
        var result = new AnchorLayoutResult();

        if ( camera == null || widgetSize.x <= 0 || widgetSize.y <= 0 )
            return result;

        // 1. Cull behind near plane
        var camTransform = camera.WorldTransform;
        var dirToNode = worldPos - camTransform.Position;
        float dotForward = Vector3.Dot( dirToNode, camTransform.Forward );

        if ( !camera.Orthographic && dotForward <= (camera.ZNear > 0 ? camera.ZNear : 1.0f) )
        {
            result.IsVisible = false;
            return result;
        }

        // 2. Project World Position -> Normalized Screen (0..1) -> Local Pixels
        var normalPos = camera.PointToScreenNormal( worldPos );
        Vector2 centerPx = new( normalPos.x * widgetSize.x, normalPos.y * widgetSize.y );
        result.ScreenCenter = centerPx;

        // 3. Compute Screen-Space Visual Radius (R_px)
        float screenRadiusPx;
        if ( camera.Orthographic )
        {
            float orthoHeight = MathF.Max( camera.OrthographicHeight, 0.001f );
            float pxPerWorldUnit = widgetSize.y / orthoHeight;
            screenRadiusPx = worldRadius * pxPerWorldUnit;
        }
        else
        {
            var offsetWorldPos = worldPos + camTransform.Right * worldRadius;
            var offsetNormalPos = camera.PointToScreenNormal( offsetWorldPos );
            screenRadiusPx = MathF.Abs( offsetNormalPos.x - normalPos.x ) * widgetSize.x;
        }

        screenRadiusPx = MathF.Max( screenRadiusPx, 6f );
        result.ScreenRadiusPx = screenRadiusPx;

        // 4. Smart 4-Way Flipping & Clamping
        float availableRight = widgetSize.x - padding - (centerPx.x + screenRadiusPx + nodeGap);
        float availableLeft = (centerPx.x - screenRadiusPx - nodeGap) - padding;

        float cardX;
        if ( cardSize.x <= availableRight )
        {
            // Fits to the Right of node
            cardX = centerPx.x + screenRadiusPx + nodeGap;
        }
        else if ( cardSize.x <= availableLeft )
        {
            // Flip to the Left of node
            cardX = centerPx.x - screenRadiusPx - nodeGap - cardSize.x;
        }
        else
        {
            // Fallback to whichever side has more room
            cardX = (availableRight >= availableLeft)
                ? centerPx.x + screenRadiusPx + nodeGap
                : centerPx.x - screenRadiusPx - nodeGap - cardSize.x;
        }

        // Vertical Alignment: Center vertically with node center
        float cardY = centerPx.y - (cardSize.y * 0.5f);

        // Clamp to viewport boundary
        cardX = Math.Clamp( cardX, padding, MathF.Max( padding, widgetSize.x - padding - cardSize.x ) );
        cardY = Math.Clamp( cardY, padding, MathF.Max( padding, widgetSize.y - padding - cardSize.y ) );

        // Avoid overlap if clamping pushed card into the node circle
        bool isOverlapping = cardX < (centerPx.x + screenRadiusPx) &&
                             (cardX + cardSize.x) > (centerPx.x - screenRadiusPx) &&
                             cardY < (centerPx.y + screenRadiusPx) &&
                             (cardY + cardSize.y) > (centerPx.y - screenRadiusPx);

        if ( isOverlapping )
        {
            float availableBottom = widgetSize.y - padding - (centerPx.y + screenRadiusPx + nodeGap);
            float availableTop = (centerPx.y - screenRadiusPx - nodeGap) - padding;

            if ( availableBottom >= cardSize.y )
            {
                cardY = centerPx.y + screenRadiusPx + nodeGap;
            }
            else if ( availableTop >= cardSize.y )
            {
                cardY = centerPx.y - screenRadiusPx - nodeGap - cardSize.x;
            }

            cardY = Math.Clamp( cardY, padding, MathF.Max( padding, widgetSize.y - padding - cardSize.y ) );
        }

        result.CardRect = new Rect( cardX, cardY, cardSize.x, cardSize.y );
        result.IsVisible = true;
        return result;
    }
}