#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Editor.Analysis.Models;
using Editor.Analysis.Models.Blocks;

namespace Editor.Analysis.Internal.Extractors;

/// <summary>
/// Extracts UI markup tags and embedded C# expressions from .razor files as SemanticWires.
/// </summary>
public static class RazorExtractor
{
    private static readonly Regex TagRegex = new( @"<([A-Z][A-Za-z0-9_]*)\b", RegexOptions.Compiled );
    private static readonly Regex CSharpExprRegex = new( @"@([A-Z][A-Za-z0-9_]*)\.", RegexOptions.Compiled );

    public static void Extract( string razorFilePath, CodeGraph graph )
    {
        if ( !File.Exists( razorFilePath ) ) return;

        string fileName = Path.GetFileNameWithoutExtension( razorFilePath );
        string docId = TypeResolver.MakeTypeDocId( fileName );
        string content = File.ReadAllText( razorFilePath );

        var existing = graph.GetNode( docId ) ?? graph.GetNode( fileName );
        if ( existing == null )
        {
            var node = new NodeBlock
            {
                Level = FractalLevel.Class,
                Body = new BodyBlock
                {
                    DocId = docId,
                    Name = fileName,
                    Title = fileName,
                    Category = SandboxTypeCategory.UiPanel,
                    Origin = NodeOrigin.UserProject,
                    FilePath = razorFilePath,
                    LineNumber = 1
                }
            };
            graph.AddNode( node );
        }

        // 1. UI Markup Tags (<Crosshair />, <InventoryGrid />)
        var seenTags = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        foreach ( Match match in TagRegex.Matches( content ) )
        {
            string tagName = match.Groups[1].Value;
            if ( string.Equals( tagName, fileName, StringComparison.OrdinalIgnoreCase ) || !seenTags.Add( tagName ) )
                continue;

            var targetNode = graph.GetNode( tagName );
            string targetDocId = targetNode != null ? targetNode.DocId : TypeResolver.MakeTypeDocId( tagName );

            graph.AddEdge( new SemanticWire
            {
                AgentDocId = docId,
                Action = RelationKind.RazorMarkupTag,
                RecipientDocId = targetDocId,
                Instrument = "UI Markup Tag",
                Condition = $"<{tagName} />",
                LineNumber = 1
            } );
        }

        // 2. Embedded C# Expressions (@GameSettings.Volume)
        var seenExprs = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        foreach ( Match match in CSharpExprRegex.Matches( content ) )
        {
            string targetClass = match.Groups[1].Value;
            if ( string.Equals( targetClass, fileName, StringComparison.OrdinalIgnoreCase ) || !seenExprs.Add( targetClass ) )
                continue;

            var targetNode = graph.GetNode( targetClass );
            string targetDocId = targetNode != null ? targetNode.DocId : TypeResolver.MakeTypeDocId( targetClass );

            graph.AddEdge( new SemanticWire
            {
                AgentDocId = docId,
                Action = RelationKind.MethodCall,
                RecipientDocId = targetDocId,
                Instrument = "Razor Expression",
                Condition = $"@{targetClass}.",
                LineNumber = 1
            } );
        }
    }
}