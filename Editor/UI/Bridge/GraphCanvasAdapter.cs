#nullable enable
using System;
using System.Collections.Generic;
using Editor.Core.Models;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using ArchitectureVisualizer.UI.CanvasEngine.Widgets;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.Bridge;

public sealed class GraphFilterOptions
{
    public string? SearchQuery { get; set; }
    public bool UserCodeOnly { get; set; } = false;
    public bool IncludeSystemPrimitives { get; set; } = false;
    public bool ComponentsOnly { get; set; } = false;
    public bool RazorOnly { get; set; } = false;
    public int MaxNodesToLoad { get; set; } = 30000;
}

public static class GraphCanvasAdapter
{
    public static void PopulateCanvas( CanvasWidget canvas, DependencyGraph graph, GraphFilterOptions? options = null )
    {
        options ??= new GraphFilterOptions();
        canvas.Clear();

        var matchingNodes = new List<GraphNode>();
        var idToIndexMap = new Dictionary<string, int>();

        // 1. Filter Nodes
        foreach ( var node in graph.Nodes.Values )
        {
            if ( !options.IncludeSystemPrimitives && node.Origin == NodeOrigin.SystemPrimitive )
                continue;

            if ( options.UserCodeOnly && node.Origin != NodeOrigin.UserProject )
                continue;

            if ( options.ComponentsOnly && node.Category != SandboxTypeCategory.SceneComponent )
                continue;

            if ( options.RazorOnly && node.Category != SandboxTypeCategory.UiPanel && node.Category != SandboxTypeCategory.UiPanelComponent )
                continue;

            if ( !string.IsNullOrWhiteSpace( options.SearchQuery ) )
            {
                bool match = node.Name.Contains( options.SearchQuery, StringComparison.OrdinalIgnoreCase ) ||
                             node.Namespace.Contains( options.SearchQuery, StringComparison.OrdinalIgnoreCase );
                if ( !match ) continue;
            }

            matchingNodes.Add( node );
            if ( matchingNodes.Count >= options.MaxNodesToLoad ) break;
        }

        if ( matchingNodes.Count == 0 )
        {
            canvas.Update();
            return;
        }

        // 2. Fermat's Spiral Spatial Allocation
        const float goldenAngle = 137.507764f * (MathF.PI / 180f);
        float spacing = 35f;

        for ( int i = 0; i < matchingNodes.Count; i++ )
        {
            var gn = matchingNodes[i];
            int degree = Math.Max( 1, graph.GetOutgoingEdges( gn.Id ).Count + graph.GetIncomingEdges( gn.Id ).Count );

            float phi = i * goldenAngle;
            float r = spacing * MathF.Sqrt( i + 1 );
            Vector2 spiralPos = new( r * MathF.Cos( phi ), r * MathF.Sin( phi ) );

            ushort zLevel = (ushort)Math.Clamp( (int)MathF.Log2( degree + 1 ), 0, 16 );
            float radius = 6.0f + MathF.Sqrt( degree ) * 1.0f; // x for more hitbox

            NodeSpatialData spatial = new()
            {
                Position = spiralPos,
                Velocity = Vector2.Zero,
                Radius = radius,
                ZLevel = zLevel,
                Shape = GetCategoryShape( gn.Category ),
                Flags = NodeFlags.None
            };

            NodePayload payload = new()
            {
                Id = gn.Id,
                Title = gn.Name,
                Subtitle = gn.Namespace,
                Summary = gn.Summary,
                FilePath = gn.FilePath,
                LineNumber = 1,
                AccentColor = GetCategoryColor( gn.Category ),
                Icon = GetCategoryIcon( gn.Category ),
                TotalDegree = degree,
                PhysicsMass = 1.0f + (degree * 0.25f),
                UserData = gn
            };

            int idx = canvas.Registry.Allocate( in spatial, payload );
            idToIndexMap[gn.Id] = idx;
        }

        // 3. Allocate Edges
        foreach ( var gn in matchingNodes )
        {
            if ( !idToIndexMap.TryGetValue( gn.Id, out int srcIdx ) ) continue;

            var outgoing = graph.GetOutgoingEdges( gn.Id );
            foreach ( var edge in outgoing )
            {
                if ( !idToIndexMap.TryGetValue( edge.TargetId, out int dstIdx ) ) continue;

                var (edgeStyle, flowSpeed) = GetRelationStyle( edge.Kind );

                var cEdge = new CanvasEdge( srcIdx, dstIdx )
                {
                    Label = GetRelationLabel( edge.Kind ),
                    CustomColor = GetRelationColor( edge.Kind ),
                    Style = edgeStyle,
                    FlowSpeed = flowSpeed,
                    DesiredSpringLength = 220f,
                    UserData = edge
                };

                canvas.Edges.Add( cEdge );
            }
        }

        canvas.SyncGpuBuffers();
        canvas.Physics.Reheat( 1.0f );
        canvas.Update();
    }

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

    public static Color GetCategoryColor( SandboxTypeCategory category ) => category switch
    {
        SandboxTypeCategory.SceneComponent => new Color( 0.18f, 0.80f, 0.44f ),
        SandboxTypeCategory.UiPanel or SandboxTypeCategory.UiPanelComponent => new Color( 0.20f, 0.60f, 1.0f ),
        SandboxTypeCategory.GameResource => new Color( 0.95f, 0.77f, 0.06f ),
        SandboxTypeCategory.Interface => new Color( 0.61f, 0.35f, 0.71f ),
        SandboxTypeCategory.Struct => new Color( 0.90f, 0.49f, 0.13f ),
        SandboxTypeCategory.Enum => new Color( 0.10f, 0.74f, 0.61f ),
        _ => new Color( 0.55f, 0.60f, 0.70f )
    };

    public static string? GetRelationLabel( RelationKind kind ) => kind switch
    {
        RelationKind.Inherits => "inherits",
        RelationKind.Implements => "implements",
        RelationKind.RazorMarkupTag => "<tag />",
        RelationKind.EventSubscription => "+=",
        RelationKind.Instantiates => "new",
        _ => null
    };

    public static Color GetRelationColor( RelationKind kind ) => kind switch
    {
        RelationKind.Inherits or RelationKind.Implements => new Color( 0.91f, 0.30f, 0.24f, 0.8f ),
        RelationKind.RazorMarkupTag => new Color( 1.0f, 0.62f, 0.26f, 0.8f ),
        RelationKind.EventSubscription => new Color( 0.68f, 0.38f, 0.95f, 0.8f ),
        RelationKind.Instantiates => new Color( 0.18f, 0.80f, 0.44f, 0.7f ),
        _ => new Color( 0.35f, 0.42f, 0.55f, 0.45f )
    };

    public static (EdgeStyle Style, float Speed) GetRelationStyle( RelationKind kind ) => kind switch
    {
        RelationKind.Inherits or RelationKind.Implements => (EdgeStyle.DirectionalArrows, 1.2f),
        RelationKind.EventSubscription => (EdgeStyle.LaserPulse, 2.0f),
        RelationKind.Instantiates => (EdgeStyle.Dashed, 1.0f),
        RelationKind.RazorMarkupTag => (EdgeStyle.DoubleLine, 0.0f),
        _ => (EdgeStyle.Solid, 0.0f)
    };

    public static NodeShape GetCategoryShape( SandboxTypeCategory category ) => category switch
    {
        SandboxTypeCategory.SceneComponent => NodeShape.RoundedBox,
        SandboxTypeCategory.Interface => NodeShape.Hexagon,
        SandboxTypeCategory.Enum => NodeShape.Diamond,
        SandboxTypeCategory.GameResource => NodeShape.Ring,
        _ => NodeShape.Circle
    };
}