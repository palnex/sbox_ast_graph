#nullable enable
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Models;

public enum TextLabelMode
{
    SmartLOD = 0,     // Shows hubs & important nodes when far, all when close
    AlwaysShowAll = 1,// Always shows all labels with smooth distance fade
    HoverOnly = 2     // Minimalist: shows label only when hovered or selected
}

/// <summary>
/// Defines visual styling tokens and visual flags for the 2D Canvas Engine.
/// </summary>
public sealed class CanvasTheme
{
    /// <summary>
    /// Canvas background clear color.
    /// </summary>
    public Color BackgroundColor { get; set; } = new Color( 0.08f, 0.09f, 0.11f );

    /// <summary>
    /// Grid minor lines / dots color.
    /// </summary>
    public Color GridColor { get; set; } = new Color( 1f, 1f, 1f, 0.04f );

    /// <summary>
    /// Default node card body background color.
    /// </summary>
    public Color NodeBackgroundColor { get; set; } = new Color( 0.13f, 0.14f, 0.18f );

    /// <summary>
    /// Node outline color when normal / unselected.
    /// </summary>
    public Color NodeBorderColor { get; set; } = new Color( 0.25f, 0.28f, 0.35f, 0.6f );

    /// <summary>
    /// Node selection outline / glow color.
    /// </summary>
    public Color SelectionColor { get; set; } = Theme.Primary;

    /// <summary>
    /// Node hover outline accent color.
    /// </summary>
    public Color HoverColor { get; set; } = Theme.Yellow;

    /// <summary>
    /// Pinned node indicator color.
    /// </summary>
    public Color PinnedIndicatorColor { get; set; } = Theme.Red;

    /// <summary>
    /// Main readable typography color.
    /// </summary>
    public Color TextColor { get; set; } = Theme.Text;

    /// <summary>
    /// Subtitle or secondary detail text color.
    /// </summary>
    public Color TextMutedColor { get; set; } = new Color( 0.65f, 0.68f, 0.75f );

    /// <summary>
    /// Default edge / line stroke color.
    /// </summary>
    public Color DefaultEdgeColor { get; set; } = new Color( 0.45f, 0.50f, 0.60f, 0.5f );

    /// <summary>
    /// Corner radius for node cards.
    /// </summary>
    public float NodeCornerRadius { get; set; } = 6f;

    /// <summary>
    /// Stroke width for connections.
    /// </summary>
    public float EdgeStrokeWidth { get; set; } = 2.0f;

    /// <summary>
    /// Glow blur radius for selection and highlight passes.
    /// </summary>
    public float GlowRadius { get; set; } = 8f;

    /// <summary>
    /// Whether to draw subtle background grid lines/dots.
    /// </summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>
    /// Grid cell step size in world coordinates.
    /// </summary>
    public float GridStep { get; set; } = 40f;

    /// <summary>
    /// Returns the default dark theme.
    /// </summary>
    public static CanvasTheme DefaultDark => new();

    /// <summary>
    /// Global scale factor for all node radii and their hitboxes (Default = 1.0).
    /// </summary>
    public float NodeSizeScale { get; set; } = 1.0f;

    /// <summary>
    /// Global multiplier for link/edge stroke thickness (Default = 1.0).
    /// </summary>
    public float LinkThicknessScale { get; set; } = 1.0f;

    /// <summary>
    /// Minimum zoom threshold at which node title text starts fading in (Default = 0.70).
    /// </summary>
    public float TextFadeThreshold { get; set; } = 0.70f;

    /// <summary>
    /// Whether to render directional arrowheads along edges.
    /// </summary>
    public bool ShowEdgeArrows { get; set; } = false;

    public TextLabelMode LabelMode { get; set; } = TextLabelMode.SmartLOD;
    public float TextSizeScale { get; set; } = 1.0f;
}