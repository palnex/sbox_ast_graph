using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Editor.Core.Models;

namespace Editor.Core.Extractors;

/// <summary>
/// Lightweight markup scanner that extracts UI component tag references from .razor files.
/// </summary>
public static class RazorTagExtractor
{
    // Matches XML/HTML-style markup tags starting with an uppercase letter: <CustomWidget ... /> or <CustomWidget>
    private static readonly Regex TagRegex = new( @"<([A-Z][A-Za-z0-9_]*)\b", RegexOptions.Compiled );

    /// <summary>
    /// Scans a .razor file on disk and creates RazorMarkupTag edges for any known UI nodes.
    /// </summary>
    public static void ExtractMarkupDependencies( string razorFilePath, DependencyGraph graph )
    {
        if ( !File.Exists( razorFilePath ) )
            return;

        string fileName = Path.GetFileNameWithoutExtension( razorFilePath );
        string content = File.ReadAllText( razorFilePath );

        var matches = TagRegex.Matches( content );
        var seenTags = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

        foreach ( Match match in matches )
        {
            string tagName = match.Groups[1].Value;

            if ( string.Equals( tagName, fileName, StringComparison.OrdinalIgnoreCase ) )
                continue;

            if ( !seenTags.Add( tagName ) )
                continue;

            // Search for matching UI components in the graph
            foreach ( var node in graph.Nodes.Values )
            {
                if ( string.Equals( node.Name, tagName, StringComparison.OrdinalIgnoreCase ) &&
                    (node.Category == SandboxTypeCategory.UiPanel || node.Category == SandboxTypeCategory.UiPanelComponent) )
                {
                    graph.AddEdge( new GraphEdge
                    {
                        SourceId = fileName,
                        TargetId = node.Id,
                        Kind = RelationKind.RazorMarkupTag,
                        Details = $"Markup tag <{tagName} />"
                    } );
                    break;
                }
            }
        }
    }
}