#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Editor.Analysis.Models;
using Editor.Analysis.Models.Blocks;

namespace Editor.Analysis.Internal.Extractors;

/// <summary>
/// Extracts UI component dependencies and inline C# expressions from .razor markup files.
/// </summary>
public static class RazorExtractor
{
    // Matches UI custom markup tags: <CustomWidget ... /> or <CustomWidget>
    private static readonly Regex TagRegex = new( @"<([A-Z][A-Za-z0-9_]*)\b", RegexOptions.Compiled );

    // Matches embedded C# calls: @GameManager. or @PlayerState.
    private static readonly Regex CSharpExpressionRegex = new( @"@([A-Z][A-Za-z0-9_]*)\.", RegexOptions.Compiled );

    /// <summary>
    /// Scans a .razor file on disk and registers RazorMarkupTag and MethodCall edges.
    /// </summary>
    public static void Extract( string razorFilePath, CodeGraph graph )
    {
        if ( !File.Exists( razorFilePath ) )
            return;

        string fileName = Path.GetFileNameWithoutExtension( razorFilePath );
        string content = File.ReadAllText( razorFilePath );

        // Ensure this Razor component is registered in the graph
        var existing = graph.GetNode( fileName );
        if ( existing == null )
        {
            var node = new NodeBlock();
            node.Header = new HeaderBlock
            {
                Id = fileName,
                Name = fileName,
                Title = fileName,
                Category = SandboxTypeCategory.UiPanel,
                Origin = NodeOrigin.UserProject,
                FilePath = razorFilePath,
                LineNumber = 1
            };
            graph.AddNode( node );
        }

        // 1. Scan UI markup tags (<Crosshair />, <InventoryGrid />)
        var tagMatches = TagRegex.Matches( content );
        var seenTags = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

        foreach ( Match match in tagMatches )
        {
            string tagName = match.Groups[1].Value;

            if ( string.Equals( tagName, fileName, StringComparison.OrdinalIgnoreCase ) )
                continue;

            if ( !seenTags.Add( tagName ) )
                continue;

            var targetNode = graph.GetNode( tagName );
            if ( targetNode != null )
            {
                graph.AddEdge( new GraphEdge
                {
                    SourceId = fileName,
                    TargetId = targetNode.Id,
                    Kind = RelationKind.RazorMarkupTag,
                    Details = $"Markup tag <{tagName} />"
                } );
            }
        }

        // 2. Scan C# expressions inside markup (@GameSettings.SoundVolume)
        var exprMatches = CSharpExpressionRegex.Matches( content );
        var seenExprs = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

        foreach ( Match match in exprMatches )
        {
            string targetClass = match.Groups[1].Value;

            if ( string.Equals( targetClass, fileName, StringComparison.OrdinalIgnoreCase ) )
                continue;

            if ( !seenExprs.Add( targetClass ) )
                continue;

            var targetNode = graph.GetNode( targetClass );
            if ( targetNode != null )
            {
                graph.AddEdge( new GraphEdge
                {
                    SourceId = fileName,
                    TargetId = targetNode.Id,
                    Kind = RelationKind.MethodCall,
                    Details = $"Razor expression @{targetClass}."
                } );
            }
        }
    }
}