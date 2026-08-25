#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Editor.Analysis.Internal.Extractors;
using Editor.Analysis.Models;
using Editor.Analysis.Models.Blocks;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;

namespace Editor.Analysis;

/// <summary>
/// Master coordinator for code analysis and architecture graphs.
/// </summary>
public static class CodeAnalysis
{
    private static CodeGraph? _activeGraph;

    public static event Action? OnGraphRebuilt;

    public static CodeGraph Graph
    {
        get
        {
            if ( _activeGraph == null )
            {
                Rebuild();
            }
            return _activeGraph!;
        }
    }

    public static NodeBlock? GetNode( string idOrName )
    {
        return Graph.GetNode( idOrName );
    }

    public static CodeGraph Rebuild()
    {
        var sw = Stopwatch.StartNew();
        var graph = new CodeGraph();

        // 1. Extract TypeLibrary types & members
        TypeLibraryExtractor.Extract( graph );

        // 2. Scan project source files
        foreach ( var file in ProjectSourceScanner.EnumerateSourceFiles() )
        {
            string ext = Path.GetExtension( file );

            if ( ext.Equals( ".cs", StringComparison.OrdinalIgnoreCase ) )
            {
                try
                {
                    string code = File.ReadAllText( file );
                    var syntaxTree = CSharpSyntaxTree.ParseText( code, path: file );
                    var root = syntaxTree.GetCompilationUnitRoot();

                    var walker = new RoslynAstExtractor( graph, file );
                    walker.Visit( root );
                }
                catch ( Exception ex )
                {
                    Log.Warning( $"[CodeAnalysis] Error parsing '{file}': {ex.Message}" );
                }
            }
            else if ( ext.Equals( ".razor", StringComparison.OrdinalIgnoreCase ) )
            {
                try
                {
                    RazorExtractor.Extract( file, graph );
                }
                catch ( Exception ex )
                {
                    Log.Warning( $"[CodeAnalysis] Error parsing Razor '{file}': {ex.Message}" );
                }
            }
        }

        _activeGraph = graph;
        sw.Stop();

        // Calculate Block Statistics
        int totalMethods = graph.Nodes.Values.Sum( n => n.Members.Methods.Count );
        int totalProperties = graph.Nodes.Values.Sum( n => n.Members.Properties.Count );
        int totalFields = graph.Nodes.Values.Sum( n => n.Members.Fields.Count );
        int totalAttributes = graph.Nodes.Values.Sum( n => n.Attributes.Items.Count );

        Log.Info( $"[CodeAnalysis] Graph built in {sw.ElapsedMilliseconds}ms!" );
        Log.Info( $"   ├─ Nodes: {graph.Nodes.Count:N0} | Edges: {graph.Edges.Count:N0}" );
        Log.Info( $"   ├─ Methods: {totalMethods:N0} | Properties: {totalProperties:N0} | Fields: {totalFields:N0}" );
        Log.Info( $"   └─ Attributes: {totalAttributes:N0}" );

        OnGraphRebuilt?.Invoke();
        return graph;
    }

    [EditorEvent.Hotload]
    public static void OnHotload()
    {
        Rebuild();
    }

    /// <summary>
    /// Performs deep diagnostic inspection on a single node and logs full details to console.
    /// </summary>
    public static void Diagnose( string idOrName )
    {
        var node = GetNode( idOrName );
        if ( node == null )
        {
            Log.Warning( $"[CodeAnalysis] Node '{idOrName}' not found in graph!" );
            return;
        }

        Log.Info( $"==================================================" );
        Log.Info( $"🔍 [DIAGNOSTIC] {node.Header.Id} ({node.Header.Origin} / {node.Header.Category})" );
        Log.Info( $"   Title: '{node.Header.Title}' | Icon: '{node.Header.Icon}' | Group: '{node.Header.Group}'" );
        Log.Info( $"   File: {node.Header.FilePath}:{node.Header.LineNumber}" );
        Log.Info( $"   Summary: {node.Header.Summary}" );
        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"📋 Attributes ({node.Attributes.Items.Count}):" );
        foreach ( var attr in node.Attributes.Items ) Log.Info( $"   • [{attr.Name}]" );

        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"📦 Properties ({node.Members.Properties.Count}):" );
        foreach ( var p in node.Members.Properties.Take( 10 ) ) Log.Info( $"   • {p.TypeName} {p.Name} {(p.HasPropertyAttribute ? "[Property]" : "")}" );
        if ( node.Members.Properties.Count > 10 ) Log.Info( $"   ... and {node.Members.Properties.Count - 10} more" );

        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"⚡ Methods ({node.Members.Methods.Count}):" );
        foreach ( var m in node.Members.Methods.Take( 10 ) ) Log.Info( $"   • {m.FullSignature}" );
        if ( node.Members.Methods.Count > 10 ) Log.Info( $"   ... and {node.Members.Methods.Count - 10} more" );

        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"🔗 Outgoing Dependencies ({node.Relations.OutgoingCount}):" );
        foreach ( var e in node.Relations.Outgoing.Take( 15 ) ) Log.Info( $"   ─[{e.Kind}]─> {e.TargetId} ({e.Details})" );
        if ( node.Relations.OutgoingCount > 15 ) Log.Info( $"   ... and {node.Relations.OutgoingCount - 15} more" );

        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"📥 Incoming References ({node.Relations.IncomingCount}):" );
        foreach ( var e in node.Relations.Incoming.Take( 15 ) ) Log.Info( $"   <─[{e.Kind}]─ {e.SourceId} ({e.Details})" );
        if ( node.Relations.IncomingCount > 15 ) Log.Info( $"   ... and {node.Relations.IncomingCount - 15} more" );
        Log.Info( $"==================================================" );
    }
}