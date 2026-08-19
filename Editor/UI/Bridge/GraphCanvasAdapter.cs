#nullable enable
using System;
using System.Collections.Generic;
using Editor.Core.Models;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using ArchitectureVisualizer.UI.CanvasEngine.Widgets;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.Bridge;

/// <summary>
/// Options for filtering which nodes and edges are populated onto the visual canvas.
/// </summary>
public sealed class GraphFilterOptions
{
    public string? SearchQuery { get; set; }
    public bool UserCodeOnly { get; set; } = false;
    public bool ComponentsOnly { get; set; } = false;
    public bool RazorOnly { get; set; } = false;
    public int MaxNodesToLoad { get; set; } = 30000;
}

/// <summary>
/// Adapts Phase 1 DependencyGraph data into visual CanvasEngine nodes and edges.
/// </summary>
public static class GraphCanvasAdapter
{
    /// <summary>
    /// Populates a CanvasWidget with filtered nodes and edges from the DependencyGraph.
    /// </summary>
    public static void PopulateCanvas( CanvasWidget canvas, DependencyGraph graph, GraphFilterOptions? options = null )
    {
        options ??= new GraphFilterOptions();

        canvas.Clear();

        var matchingGraphNodes = new List<GraphNode>();
        var addedNodeMap = new Dictionary<string, CanvasNode>();

        // 1. Filter Nodes
        foreach ( var node in graph.Nodes.Values )
        {
            if ( options.UserCodeOnly && node.Origin != NodeOrigin.UserProject )
                continue;

            if ( options.ComponentsOnly && node.Category != SandboxTypeCategory.SceneComponent )
                continue;

            if ( options.RazorOnly && node.Category != SandboxTypeCategory.UiPanel && node.Category != SandboxTypeCategory.UiPanelComponent )
                continue;

            if ( !string.IsNullOrWhiteSpace( options.SearchQuery ) )
            {
                bool matchName = node.Name.Contains( options.SearchQuery, StringComparison.OrdinalIgnoreCase );
                bool matchNs = node.Namespace.Contains( options.SearchQuery, StringComparison.OrdinalIgnoreCase );
                if ( !matchName && !matchNs )
                    continue;
            }

            matchingGraphNodes.Add( node );

            if ( matchingGraphNodes.Count >= options.MaxNodesToLoad )
                break;
        }

        if ( matchingGraphNodes.Count == 0 )
        {
            canvas.Update();
            return;
        }

        // 2. Create Canvas Nodes using Fermat's Spiral (Phyllotaxis / Golden Angle)
        const float goldenAngle = 137.507764f * (MathF.PI / 180f);
        float initialSpacing = 35f;

        for ( int i = 0; i < matchingGraphNodes.Count; i++ )
        {
            var gn = matchingGraphNodes[i];

            // Compute connection degree
            int degree = graph.GetOutgoingEdges( gn.Id ).Count + graph.GetIncomingEdges( gn.Id ).Count;
            degree = Math.Max( 1, degree );

            // Fermat's Spiral: r = c * sqrt(i), theta = i * 137.5 deg
            float phi = i * goldenAngle;
            float r = initialSpacing * MathF.Sqrt( i + 1 );
            Vector2 spiralPos = new( r * MathF.Cos( phi ), r * MathF.Sin( phi ) );

            var cNode = new CanvasNode
            {
                Id = gn.Id,
                Title = gn.Name,
                Subtitle = gn.Namespace,
                Icon = GetCategoryIcon( gn.Category ),
                AccentColor = GetCategoryColor( gn.Category ),
                Position = spiralPos,
                Size = new Vector2( 20f, 20f ),
                Degree = degree,
                Mass = 1.0f + MathF.Sqrt( degree ) * 0.5f,
                UserData = gn
            };

            addedNodeMap[gn.Id] = cNode;
            canvas.Nodes.Add( cNode );
        }

        // 3. Create Canvas Edges with Dynamic Spring Lengths
        foreach ( var gn in matchingGraphNodes )
        {
            if ( !addedNodeMap.TryGetValue( gn.Id, out var srcCanvasNode ) )
                continue;

            var outgoingEdges = graph.GetOutgoingEdges( gn.Id );
            foreach ( var edge in outgoingEdges )
            {
                if ( !addedNodeMap.TryGetValue( edge.TargetId, out var dstCanvasNode ) )
                    continue;

                // Obsidian Link Distance: spacious 200-250px to form distinct planetary wheels
                float desiredDist = 220f;

                var cEdge = new CanvasEdge( srcCanvasNode, dstCanvasNode )
                {
                    Label = GetRelationLabel( edge.Kind ),
                    CustomColor = GetRelationColor( edge.Kind ),
                    DesiredSpringLength = desiredDist,
                    UserData = edge
                };

                canvas.Edges.Add( cEdge );
            }
        }

        // 4. Reheat physics simulation to full energy
        canvas.Physics.Reheat( 1.0f );
        canvas.Update();
    }

    /// <summary>
    /// Returns the Material icon representing a given type category.
    /// </summary>
    public static string GetCategoryIcon( SandboxTypeCategory category ) => category switch
    {
        SandboxTypeCategory.SceneComponent => "view_in_ar",
        SandboxTypeCategory.UiPanel or SandboxTypeCategory.UiPanelComponent => "dashboard",
        SandboxTypeCategory.GameResource => "inventory_2",
        SandboxTypeCategory.Interface => "extension",
        SandboxTypeCategory.Struct => "data_object",
        SandboxTypeCategory.Enum => "list",
        _ => "code"
    };

    /// <summary>
    /// Returns the theme accent color for a given type category.
    /// </summary>
    public static Color GetCategoryColor( SandboxTypeCategory category ) => category switch
    {
        SandboxTypeCategory.SceneComponent => new Color( 0.18f, 0.80f, 0.44f ), // Vibrant Green
        SandboxTypeCategory.UiPanel or SandboxTypeCategory.UiPanelComponent => new Color( 0.20f, 0.60f, 1.0f ), // Sky Blue
        SandboxTypeCategory.GameResource => new Color( 0.95f, 0.77f, 0.06f ), // Amber Yellow
        SandboxTypeCategory.Interface => new Color( 0.61f, 0.35f, 0.71f ), // Purple
        SandboxTypeCategory.Struct => new Color( 0.90f, 0.49f, 0.13f ), // Orange
        SandboxTypeCategory.Enum => new Color( 0.10f, 0.74f, 0.61f ), // Teal
        _ => new Color( 0.55f, 0.60f, 0.70f ) // Muted Slate
    };

    /// <summary>
    /// Returns the display text badge for a relationship kind.
    /// </summary>
    public static string? GetRelationLabel( RelationKind kind ) => kind switch
    {
        RelationKind.Inherits => "inherits",
        RelationKind.Implements => "implements",
        RelationKind.RazorMarkupTag => "<tag />",
        RelationKind.EventSubscription => "+=",
        RelationKind.Instantiates => "new",
        _ => null
    };

    /// <summary>
    /// Returns the visual stroke color for a relationship kind.
    /// </summary>
    public static Color GetRelationColor( RelationKind kind ) => kind switch
    {
        RelationKind.Inherits or RelationKind.Implements => new Color( 0.91f, 0.30f, 0.24f, 0.8f ), // Coral Red
        RelationKind.RazorMarkupTag => new Color( 1.0f, 0.62f, 0.26f, 0.8f ), // Orange
        RelationKind.EventSubscription => new Color( 0.68f, 0.38f, 0.95f, 0.8f ), // Neon Purple
        RelationKind.Instantiates => new Color( 0.18f, 0.80f, 0.44f, 0.7f ), // Green
        _ => new Color( 0.35f, 0.42f, 0.55f, 0.45f ) // Subtle Gray-Blue
    };
}