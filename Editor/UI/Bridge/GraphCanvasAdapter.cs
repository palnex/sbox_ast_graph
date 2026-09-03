#nullable enable
using System;
using System.Collections.Generic;
using ArchitectureVisualizer.UI.CanvasEngine.API;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using ArchitectureVisualizer.UI;
using Editor.Analysis.Models;
using Editor.Analysis.Models.Blocks;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.Bridge;

/// <summary>
/// Filtering options for building the visual canvas graph.
/// </summary>
public sealed class GraphFilterOptions
{
    public string? SearchQuery { get; set; }
    public bool UserCodeOnly { get; set; } = false;
    public bool IncludeSystemPrimitives { get; set; } = false;
    public bool IncludeCompilerGenerated { get; set; } = false;
    public bool ComponentsOnly { get; set; } = false;
    public bool RazorOnly { get; set; } = false;
    public int MaxNodesToLoad { get; set; } = 30000;
}

/// <summary>
/// Bridges CodeGraph AST models to hardware-accelerated CanvasEngine via public ICanvasGraph contract.
/// </summary>
public static class GraphCanvasAdapter
{
    public static void PopulateCanvas( CanvasWidget canvas, CodeGraph graph, GraphFilterOptions? options = null )
    {
        options ??= new GraphFilterOptions();

        var matchingNodes = new List<NodeBlock>();

        // 1. Filter Nodes
        foreach ( var node in graph.Nodes.Values )
        {
            var body = node.Body;

            if ( !options.IncludeCompilerGenerated && IsCompilerGenerated( body.Name, body.DocId ) )
                continue;

            if ( !options.IncludeSystemPrimitives && body.Origin == NodeOrigin.SystemPrimitive )
                continue;

            if ( options.UserCodeOnly && body.Origin != NodeOrigin.UserProject )
                continue;

            if ( options.ComponentsOnly && body.Category != SandboxTypeCategory.SceneComponent )
                continue;

            if ( options.RazorOnly && body.Category != SandboxTypeCategory.UiPanel && body.Category != SandboxTypeCategory.UiPanelComponent )
                continue;

            if ( !string.IsNullOrWhiteSpace( options.SearchQuery ) )
            {
                bool match = body.Name.Contains( options.SearchQuery, StringComparison.OrdinalIgnoreCase ) ||
                             body.Namespace.Contains( options.SearchQuery, StringComparison.OrdinalIgnoreCase ) ||
                             body.Title.Contains( options.SearchQuery, StringComparison.OrdinalIgnoreCase ) ||
                             body.DocId.Contains( options.SearchQuery, StringComparison.OrdinalIgnoreCase );
                if ( !match ) continue;
            }

            matchingNodes.Add( node );
            if ( matchingNodes.Count >= options.MaxNodesToLoad ) break;
        }

        // 2. High-Performance Ingestion using CanvasEngine BatchUpdate
        canvas.Clear();

        if ( matchingNodes.Count == 0 )
        {
            canvas.Update();
            return;
        }

        const float goldenAngle = 137.507764f * (MathF.PI / 180f);
        float spacing = matchingNodes.Count > 1000 ? 55f : 40f;
        var validIds = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

        canvas.BatchUpdate( g =>
        {
            // Add all nodes
            for ( int i = 0; i < matchingNodes.Count; i++ )
            {
                var node = matchingNodes[i];
                var body = node.Body;
                int degree = Math.Max( 1, node.Relations.OutgoingCount + node.Relations.IncomingCount );

                float phi = i * goldenAngle;
                float r = spacing * MathF.Sqrt( i + 1 );
                Vector2 spiralPos = new( r * MathF.Cos( phi ), r * MathF.Sin( phi ) );

                float radius = Math.Clamp( 7.0f + MathF.Sqrt( degree ) * 1.2f, 7.0f, 45.0f );
                string icon = !string.IsNullOrWhiteSpace( body.Icon ) ? body.Icon : GetCategoryIcon( body.Category );

                g.AddNode( body.DocId, body.Title, body.Namespace )
                 .WithShape( GetCategoryShape( body.Category ) )
                 .WithColor( GetCategoryColor( body.Category ) )
                 .WithSize( radius )
                 .WithPosition( spiralPos )
                 .WithData( node );

                int idx = canvas.Registry.Count - 1;
                if ( idx >= 0 && idx < canvas.Registry.Count )
                {
                    var payload = canvas.Registry.GetPayload( idx );
                    payload.Summary = body.Summary;
                    payload.FilePath = body.FilePath;
                    payload.LineNumber = body.LineNumber > 0 ? body.LineNumber : 1;
                    payload.Icon = icon;
                    payload.TotalDegree = degree;
                    payload.PhysicsMass = 1.0f + MathF.Min( degree * 0.2f, 10.0f );
                }

                validIds.Add( body.DocId );
            }

            // Connect semantic wires
            foreach ( var node in matchingNodes )
            {
                foreach ( var edge in node.Relations.Outgoing )
                {
                    if ( !validIds.Contains( edge.RecipientDocId ) ) continue;

                    var (edgeStyle, flowSpeed) = GetRelationStyle( edge.Action, edge.IsPolymorphicFanout );

                    g.Connect( node.DocId, edge.RecipientDocId )
                     .WithStyle( edgeStyle )
                     .WithSpeed( flowSpeed )
                     .WithColor( GetRelationColor( edge.Action, edge.IsPolymorphicFanout ) )
                     .WithLabel( GetRelationLabel( edge.Action, edge.IsPolymorphicFanout ) );
                }
            }
        } );

        canvas.RebuildAdjacency();
        canvas.FitToScreen();
        canvas.Update();
    }

    /// <summary>
    /// Zero-allocation Roslyn synthetic artifact filter based on ECMA-335 grammar (from technical research).
    /// </summary>
    public static bool IsCompilerGenerated( string? name, string? docId )
    {
        if ( string.IsNullOrWhiteSpace( name ) ) return true;

        var span = name.AsSpan();

        // Strip namespace / nested class separators (e.g. MyClass+<>c -> <>c)
        int lastSep = span.LastIndexOfAny( '.', '+', '/' );
        if ( lastSep >= 0 && lastSep < span.Length - 1 )
            span = span.Slice( lastSep + 1 );

        // 1. Exact match compiler infrastructure
        if ( span.SequenceEqual( "<Module>".AsSpan() ) ||
             span.SequenceEqual( "<PrivateImplementationDetails>".AsSpan() ) ||
             span.SequenceEqual( "<Program>$".AsSpan() ) )
        {
            return true;
        }

        // 2. Fixed buffer raw structs
        if ( span.StartsWith( "__StaticArrayInitTypeSize=".AsSpan(), StringComparison.Ordinal ) ||
             span.StartsWith( "StaticArrayInitTypeSize".AsSpan(), StringComparison.Ordinal ) ||
             span.StartsWith( "__StaticArrayInit".AsSpan(), StringComparison.Ordinal ) )
        {
            return true;
        }

        // 3. Roslyn bracketed naming convention (<Identifier>...)
        if ( span[0] == '<' )
        {
            // Anonymous types, lambda singletons (<>c, <>f__AnonymousType)
            if ( span.Length > 1 && span[1] == '>' )
                return true;

            int closingAngle = span.IndexOf( '>' );
            if ( closingAngle > 0 && closingAngle < span.Length - 1 )
            {
                var suffix = span.Slice( closingAngle + 1 );

                if ( suffix.StartsWith( "d__".AsSpan(), StringComparison.Ordinal ) ||
                     suffix.StartsWith( "k__BackingField".AsSpan(), StringComparison.Ordinal ) ||
                     suffix.StartsWith( "g__".AsSpan(), StringComparison.Ordinal ) ||
                     suffix.StartsWith( "b__".AsSpan(), StringComparison.Ordinal ) ||
                     suffix.StartsWith( "e__FixedBuffer".AsSpan(), StringComparison.Ordinal ) ||
                     suffix.StartsWith( "P".AsSpan(), StringComparison.Ordinal ) ||
                     suffix.StartsWith( "i__Field".AsSpan(), StringComparison.Ordinal ) )
                {
                    return true;
                }
            }

            return true;
        }

        // 4. Cached lambda delegates and DLR call sites
        if ( span.StartsWith( "<>9".AsSpan(), StringComparison.Ordinal ) ||
             span.StartsWith( "<>p__".AsSpan(), StringComparison.Ordinal ) ||
             span.StartsWith( "<>o__".AsSpan(), StringComparison.Ordinal ) ||
             span.Contains( "DisplayClass".AsSpan(), StringComparison.Ordinal ) )
        {
            return true;
        }

        // 5. DocId verification
        if ( !string.IsNullOrEmpty( docId ) && (docId.Contains( "<>c" ) || docId.Contains( "<PrivateImplementationDetails>" )) )
            return true;

        return false;
    }

    public static NodeShape GetCategoryShape( SandboxTypeCategory category ) => category switch
    {
        SandboxTypeCategory.SceneComponent => NodeShape.RoundedBox,
        SandboxTypeCategory.Interface => NodeShape.Hexagon,
        SandboxTypeCategory.Enum => NodeShape.Diamond,
        SandboxTypeCategory.GameResource => NodeShape.Ring,
        _ => NodeShape.Circle
    };

    public static (EdgeStyle Style, float Speed) GetRelationStyle( RelationKind kind, bool isPolyFanout )
    {
        if ( isPolyFanout ) return (EdgeStyle.Dashed, 0.8f);

        return kind switch
        {
            RelationKind.Inherits or RelationKind.Implements => (EdgeStyle.DirectionalArrows, 1.2f),
            RelationKind.RpcDispatch => (EdgeStyle.LaserPulse, 3.0f),
            RelationKind.AsyncAwait => (EdgeStyle.Dashed, 0.5f),
            RelationKind.EventSubscription => (EdgeStyle.LaserPulse, 1.8f),
            RelationKind.Instantiates => (EdgeStyle.Dashed, 1.0f),
            RelationKind.RazorMarkupTag => (EdgeStyle.DoubleLine, 0.0f),
            _ => (EdgeStyle.Solid, 0.0f)
        };
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

    public static string? GetRelationLabel( RelationKind kind, bool isPolyFanout )
    {
        if ( isPolyFanout ) return "┄┄[polymorphic]┄┄►";

        return kind switch
        {
            RelationKind.Inherits => "inherits",
            RelationKind.Implements => "implements",
            RelationKind.RpcDispatch => "[rpc]",
            RelationKind.AsyncAwait => "await",
            RelationKind.RazorMarkupTag => "<tag />",
            RelationKind.EventSubscription => "+=",
            RelationKind.Instantiates => "new",
            RelationKind.ComponentFetch => "GetComponent",
            _ => null
        };
    }

    public static Color GetRelationColor( RelationKind kind, bool isPolyFanout )
    {
        if ( isPolyFanout ) return new Color( 0.61f, 0.35f, 0.71f, 0.5f );

        return kind switch
        {
            RelationKind.Inherits or RelationKind.Implements => new Color( 0.91f, 0.30f, 0.24f, 0.8f ),
            RelationKind.RpcDispatch => new Color( 0.95f, 0.20f, 0.90f, 0.9f ),
            RelationKind.AsyncAwait => new Color( 0.20f, 0.80f, 0.95f, 0.75f ),
            RelationKind.RazorMarkupTag => new Color( 1.0f, 0.62f, 0.26f, 0.8f ),
            RelationKind.EventSubscription => new Color( 0.68f, 0.38f, 0.95f, 0.8f ),
            RelationKind.Instantiates => new Color( 0.18f, 0.80f, 0.44f, 0.7f ),
            RelationKind.ComponentFetch => new Color( 0.95f, 0.55f, 0.15f, 0.8f ),
            _ => new Color( 0.35f, 0.42f, 0.55f, 0.45f )
        };
    }
}