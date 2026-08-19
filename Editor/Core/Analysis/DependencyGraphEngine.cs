#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using Editor;
using Editor.Core.Analysis;
using Editor.Core.Extractors;
using Editor.Core.Models;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;

namespace Editor.Core;

/// <summary>
/// Master coordinator for building, maintaining, and hot-reloading the active project dependency graph in memory.
/// </summary>
public static class DependencyGraphEngine
{
    private static DependencyGraph? _currentGraph;

    /// <summary>
    /// The active in-memory dependency graph.
    /// </summary>
    public static DependencyGraph Current
    {
        get
        {
            if ( _currentGraph == null )
            {
                Rebuild();
            }
            return _currentGraph!;
        }
    }

    /// <summary>
    /// Completely rebuilds the graph from Engine TypeLibrary and active User Project source files.
    /// </summary>
    public static DependencyGraph Rebuild()
    {
        var sw = Stopwatch.StartNew();
        var graph = new DependencyGraph();

        // 1. Extract all loaded Engine & TypeLibrary types
        EngineTypeExtractor.ExtractAllTypes( graph );

        // 2. Scan and parse all project source files (.cs and .razor)
        foreach ( var file in ProjectSourceScanner.EnumerateProjectFiles() )
        {
            string ext = Path.GetExtension( file );

            if ( ext.Equals( ".cs", StringComparison.OrdinalIgnoreCase ) )
            {
                string code = File.ReadAllText( file );
                var syntaxTree = CSharpSyntaxTree.ParseText( code );
                var root = syntaxTree.GetCompilationUnitRoot();

                var walker = new RoslynAstWalker( graph, file );
                walker.Visit( root );
            }
            else if ( ext.Equals( ".razor", StringComparison.OrdinalIgnoreCase ) )
            {
                RazorTagExtractor.ExtractMarkupDependencies( file, graph );
            }
        }

        _currentGraph = graph;
        sw.Stop();

        Log.Info( $"[DependencyGraph] Graph rebuilt in {sw.ElapsedMilliseconds}ms! Nodes: {graph.Nodes.Count}, Edges: {graph.Edges.Count}" );
        return graph;
    }

    /// <summary>
    /// Automatically rebuilds the dependency graph on s&box hotload recompilations.
    /// </summary>
    [EditorEvent.Hotload]
    public static void OnHotload()
    {
        Rebuild();
    }
}