#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Editor.Analysis.Internal.Extractors;
using Editor.Analysis.Models;
using Editor.Analysis.Models.Blocks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;

namespace Editor.Analysis;

/// <summary>
/// Main public developer-facing API facade for querying fractal architecture graphs, semantic wires, and type diagnostics.
/// </summary>
public static class CodeAnalysis
{
    private static CodeGraph? _activeGraph;

    /// <summary> Event fired whenever the architecture graph is rebuilt. </summary>
    public static event Action? OnGraphRebuilt;

    /// <summary> The active in-memory semantic code graph. </summary>
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

    // ==========================================
    // 1. PUBLIC QUERY API
    // ==========================================

    /// <summary> Retrieves a node by DocId, FQN, or short name (e.g. "BBox", "PlayerController", "T:Sandbox.Component"). </summary>
    public static NodeBlock? GetNode( string idOrName )
    {
        return Graph.GetNode( idOrName );
    }

    /// <summary> Retrieves a node by its strongly-typed generic System.Type. </summary>
    public static NodeBlock? GetNode<T>()
    {
        return Graph.GetNode( typeof( T ).FullName ?? typeof( T ).Name );
    }

    /// <summary> Returns all nodes matching a specific architectural category (e.g. SceneComponent, UiPanel, GameResource). </summary>
    public static IEnumerable<NodeBlock> GetNodes( SandboxTypeCategory category )
    {
        return Graph.Nodes.Values.Where( n => n.Body.Category == category );
    }

    /// <summary> Returns all nodes originating from a specific package or library (e.g. "towertinno", "sbox_ast_graph"). </summary>
    public static IEnumerable<NodeBlock> GetNodesByPackage( string packageName )
    {
        return Graph.Nodes.Values.Where( n => string.Equals( n.Body.PackageName, packageName, StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary> Returns all concrete class DocIds that implement the specified interface. </summary>
    public static IReadOnlyList<string> GetImplementations( string interfaceDocIdOrName )
    {
        var ifaceNode = GetNode( interfaceDocIdOrName );
        string docId = ifaceNode != null ? ifaceNode.DocId : TypeResolver.MakeTypeDocId( interfaceDocIdOrName );

        if ( Graph.InterfaceImplementations.TryGetValue( docId, out var list ) )
            return list;

        return Array.Empty<string>();
    }

    /// <summary> Returns all concrete class DocIds that implement the specified interface generic type. </summary>
    public static IReadOnlyList<string> GetImplementations<T>() where T : class
    {
        return GetImplementations( typeof( T ).FullName ?? typeof( T ).Name );
    }

    /// <summary> Finds all incoming dependency wires referencing the specified type. </summary>
    public static IEnumerable<SemanticWire> FindReferences( string typeIdOrName )
    {
        var node = GetNode( typeIdOrName );
        if ( node == null ) return Enumerable.Empty<SemanticWire>();
        return node.Relations.Incoming;
    }

    /// <summary> Finds all methods across user and engine code that accept the target type as a parameter. </summary>
    public static IEnumerable<(NodeBlock Node, MethodItem Method)> FindMethodsAccepting( string targetTypeName )
    {
        string clean = TypeResolver.GetShortName( targetTypeName );
        foreach ( var node in Graph.Nodes.Values )
        {
            foreach ( var method in node.Members.Methods )
            {
                if ( method.Parameters.Any( p => string.Equals( TypeResolver.GetShortName( p.TypeName ), clean, StringComparison.OrdinalIgnoreCase ) ) )
                {
                    yield return (node, method);
                }
            }
        }
    }

    /// <summary> Finds all methods across user and engine code that return the specified type. </summary>
    public static IEnumerable<(NodeBlock Node, MethodItem Method)> FindMethodsReturning( string targetTypeName )
    {
        string clean = TypeResolver.GetShortName( targetTypeName );
        foreach ( var node in Graph.Nodes.Values )
        {
            foreach ( var method in node.Members.Methods )
            {
                if ( string.Equals( TypeResolver.GetShortName( method.ReturnTypeName ), clean, StringComparison.OrdinalIgnoreCase ) )
                {
                    yield return (node, method);
                }
            }
        }
    }

    // ==========================================
    // 2. REBUILD & PIPELINE COORDINATION
    // ==========================================

    /// <summary>
    /// Compiles all project source files with full Roslyn SemanticModel and rebuilds the fractal semantic graph.
    /// </summary>
    public static CodeGraph Rebuild()
    {
        var sw = Stopwatch.StartNew();
        var graph = new CodeGraph();

        // 1. Ingest Engine Types from TypeLibrary & EditorTypeLibrary
        TypeLibraryExtractor.Extract( graph );

        // 2. Enumerate Project & Library Source Files Dynamically
        var allSourceFiles = ProjectSourceScanner.EnumerateAllProjectFiles().ToList();
        var csFiles = allSourceFiles.Where( f => f.FilePath.EndsWith( ".cs", StringComparison.OrdinalIgnoreCase ) ).ToList();
        var razorFiles = allSourceFiles.Where( f => f.FilePath.EndsWith( ".razor", StringComparison.OrdinalIgnoreCase ) ).ToList();

        // 3. Build Roslyn CSharpCompilation for User Code + Libraries
        var syntaxTrees = new List<SyntaxTree>();
        foreach ( var item in csFiles )
        {
            try
            {
                string code = File.ReadAllText( item.FilePath );
                syntaxTrees.Add( CSharpSyntaxTree.ParseText( code, path: item.FilePath ) );
            }
            catch ( Exception ex )
            {
                Log.Warning( $"[CodeAnalysis] Failed to parse syntax for '{item.FilePath}': {ex.Message}" );
            }
        }

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where( a => !a.IsDynamic && !string.IsNullOrEmpty( a.Location ) && File.Exists( a.Location ) )
            .Select( a => MetadataReference.CreateFromFile( a.Location ) )
            .Cast<MetadataReference>()
            .ToList();

        var compilation = CSharpCompilation.Create(
            "ActiveProjectAnalysis",
            syntaxTrees,
            references,
            new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary )
        );

        // 4. Run Semantic Extraction over each syntax tree with true package name
        var fileToPackageMap = allSourceFiles.ToDictionary( f => f.FilePath, f => f.PackageName, StringComparer.OrdinalIgnoreCase );

        foreach ( var tree in compilation.SyntaxTrees )
        {
            var semanticModel = compilation.GetSemanticModel( tree );
            fileToPackageMap.TryGetValue( tree.FilePath, out string? pkg );
            var walker = new RoslynSemanticExtractor( graph, semanticModel, tree.FilePath, pkg ?? "" );
            walker.Visit( tree.GetRoot() );
        }

        // 5. Scan Razor markup dependencies
        foreach ( var razorFile in razorFiles )
        {
            try
            {
                RazorExtractor.Extract( razorFile.FilePath, graph );
            }
            catch ( Exception ex )
            {
                Log.Warning( $"[CodeAnalysis] Error parsing Razor for '{razorFile.FilePath}': {ex.Message}" );
            }
        }

        // 6. Generate Polymorphic Interface Fan-Out Wires
        GeneratePolymorphicFanOut( graph );

        _activeGraph = graph;
        sw.Stop();

        // Calculate Block Statistics
        int totalMethods = graph.Nodes.Values.Sum( n => n.Members.Methods.Count );
        int totalProperties = graph.Nodes.Values.Sum( n => n.Members.Properties.Count );
        int totalFields = graph.Nodes.Values.Sum( n => n.Members.Fields.Count );
        int totalAttributes = graph.Nodes.Values.Sum( n => n.Attributes.Items.Count );
        int rpcWires = graph.Edges.Count( e => e.Action == RelationKind.RpcDispatch );
        int asyncWires = graph.Edges.Count( e => e.Action == RelationKind.AsyncAwait );
        int polyWires = graph.Edges.Count( e => e.Action == RelationKind.PolymorphicDispatch );

        Log.Info( $"[CodeAnalysis] 100% True Semantic Graph built in {sw.ElapsedMilliseconds}ms!" );
        Log.Info( $"   ├─ Nodes: {graph.Nodes.Count:N0} | Semantic Wires: {graph.Edges.Count:N0}" );
        Log.Info( $"   ├─ Methods: {totalMethods:N0} | Properties: {totalProperties:N0} | Fields: {totalFields:N0}" );
        Log.Info( $"   ├─ Attributes: {totalAttributes:N0} | Polymorphic Fan-outs: {polyWires:N0}" );
        Log.Info( $"   └─ Network RPCs: {rpcWires:N0} | Async/Await Suspensions: {asyncWires:N0}" );

        OnGraphRebuilt?.Invoke();
        return graph;
    }

    private static void GeneratePolymorphicFanOut( CodeGraph graph )
    {
        var polyWiresToAdd = new List<SemanticWire>();

        foreach ( var edge in graph.Edges.ToList() )
        {
            if ( edge.Action == RelationKind.MethodCall && graph.InterfaceImplementations.TryGetValue( edge.RecipientDocId, out var implementors ) )
            {
                foreach ( var implDocId in implementors )
                {
                    polyWiresToAdd.Add( new SemanticWire
                    {
                        AgentDocId = edge.AgentDocId,
                        Action = RelationKind.PolymorphicDispatch,
                        RecipientDocId = implDocId,
                        Instrument = $"Polymorphic via {TypeResolver.GetShortName( edge.RecipientDocId )}",
                        Condition = edge.Condition,
                        LineNumber = edge.LineNumber,
                        IsPolymorphicFanout = true
                    } );
                }
            }
        }

        foreach ( var polyEdge in polyWiresToAdd )
        {
            graph.AddEdge( polyEdge );
        }
    }

    // ==========================================
    // 3. DIAGNOSTICS
    // ==========================================

    public static void Diagnose( string idOrName ) => DiagnoseFull( idOrName );

    public static void DiagnoseFull( string idOrName )
    {
        var node = GetNode( idOrName );
        if ( node == null )
        {
            Log.Warning( $"[CodeAnalysis] Node '{idOrName}' not found in graph!" );
            return;
        }

        var body = node.Body;
        Log.Info( $"==================================================" );
        Log.Info( $"🔍 [FULL DIAGNOSTIC] {body.DocId} ({body.Origin} / {body.Category})" );
        Log.Info( $"   Title: '{body.Title}' | Icon: '{body.Icon}' | Group: '{body.Group}'" );
        Log.Info( $"   File: {body.FilePath}:{body.LineNumber}" );
        Log.Info( $"   Summary: {body.Summary}" );

        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"📋 Attributes ({node.Attributes.Items.Count}):" );
        foreach ( var attr in node.Attributes.Items ) Log.Info( $"   • [{attr.Name}]" );

        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"️ Fields ({node.Members.Fields.Count}):" );
        foreach ( var f in node.Members.Fields.Take( 8 ) ) Log.Info( $"   • {f.TypeName} {f.Name}" );
        if ( node.Members.Fields.Count > 8 ) Log.Info( $"   ... and {node.Members.Fields.Count - 8} more" );

        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"📦 Properties ({node.Members.Properties.Count}):" );
        foreach ( var p in node.Members.Properties.Take( 8 ) ) Log.Info( $"   • {p.TypeName} {p.Name} {(p.HasPropertyAttribute ? "[Property]" : "")}" );
        if ( node.Members.Properties.Count > 8 ) Log.Info( $"   ... and {node.Members.Properties.Count - 8} more" );

        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"⚡ Methods ({node.Members.Methods.Count}):" );
        foreach ( var m in node.Members.Methods.Take( 8 ) ) Log.Info( $"   • {m.FullSignature}" );
        if ( node.Members.Methods.Count > 8 ) Log.Info( $"   ... and {node.Members.Methods.Count - 8} more" );

        if ( Graph.InterfaceImplementations.TryGetValue( body.DocId, out var impls ) )
        {
            Log.Info( $"--------------------------------------------------" );
            Log.Info( $"🎭 Concrete Implementations ({impls.Count}):" );
            foreach ( var impl in impls.Take( 10 ) ) Log.Info( $"   • ──[IS-A]──► {impl}" );
        }

        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"🔗 Outgoing Dependencies ({node.Relations.OutgoingCount}):" );
        foreach ( var e in node.Relations.Outgoing.Take( 15 ) )
        {
            string polyMarker = e.IsPolymorphicFanout ? " [Polymorphic Ghost]" : "";
            Log.Info( $"   ─[{e.Action}]─► {e.RecipientDocId} ({e.Instrument}){polyMarker}" );
        }
        if ( node.Relations.OutgoingCount > 15 ) Log.Info( $"   ... and {node.Relations.OutgoingCount - 15} more" );

        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"📥 Incoming References ({node.Relations.IncomingCount}):" );
        foreach ( var e in node.Relations.Incoming.Take( 15 ) )
        {
            Log.Info( $"   ◄─[{e.Action}]─ {e.AgentDocId} ({e.Instrument})" );
        }
        if ( node.Relations.IncomingCount > 15 ) Log.Info( $"   ... and {node.Relations.IncomingCount - 15} more" );
        Log.Info( $"==================================================" );
    }

    [EditorEvent.Hotload]
    public static void OnHotload()
    {
        Rebuild();
    }
}