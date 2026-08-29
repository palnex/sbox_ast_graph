#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Editor.Analysis.Internal.Extractors;
using Editor.Analysis.Models;
using Editor.Analysis.Models.Blocks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;

namespace Editor.Analysis;

/// <summary>
/// Master coordinator for True Semantic Code Analysis and Live Telemetry.
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

    /// <summary>
    /// Compiles all project source files with full Roslyn SemanticModel and extracts 100% facts.
    /// </summary>
    public static CodeGraph Rebuild()
    {
        var sw = Stopwatch.StartNew();
        var graph = new CodeGraph();

        // 1. Ingest Engine Types from TypeLibrary
        TypeLibraryExtractor.Extract( graph );

        // 2. Enumerate Project & Library Source Files Dynamically
        var allSourceFiles = ProjectSourceScanner.EnumerateAllProjectFiles().ToList();
        var csFiles = allSourceFiles.Where( f => f.FilePath.EndsWith( ".cs", StringComparison.OrdinalIgnoreCase ) ).ToList();
        var razorFiles = allSourceFiles.Where( f => f.FilePath.EndsWith( ".razor", StringComparison.OrdinalIgnoreCase ) ).ToList();

        // 3. Build True Roslyn CSharpCompilation for User Code + Libraries
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

        // Gather all loaded assembly metadata references (Sandbox.Game, Sandbox.Engine, System, etc.)
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

        // 4. Run True Semantic Extraction over each syntax tree
        foreach ( var tree in compilation.SyntaxTrees )
        {
            var semanticModel = compilation.GetSemanticModel( tree );
            var walker = new RoslynSemanticExtractor( graph, semanticModel, tree.FilePath );
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

    public static void Diagnose( string idOrName )
    {
        var node = GetNode( idOrName );
        if ( node == null )
        {
            Log.Warning( $"[CodeAnalysis] Node '{idOrName}' not found in graph!" );
            return;
        }

        var body = node.Body;
        Log.Info( $"==================================================" );
        Log.Info( $"🔍 [5D SEMANTIC DIAGNOSTIC] {body.DocId} ({body.Origin} / {body.Category} / Realm: {body.Realm})" );
        Log.Info( $"   Title: '{body.Title}' | Icon: '{body.Icon}' | Group: '{body.Group}'" );
        Log.Info( $"   Source: {body.FilePath}:{body.LineNumber}" );
        Log.Info( $"   Summary: {body.Summary}" );

        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"📋 Attributes ({node.Attributes.Items.Count}):" );
        foreach ( var attr in node.Attributes.Items ) Log.Info( $"   • [{attr.Name}]" );

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
            foreach ( var impl in impls ) Log.Info( $"   • ──[IS-A]──► {impl}" );
        }

        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"🔗 Outgoing Semantic Wires ({node.Relations.OutgoingCount}):" );
        foreach ( var e in node.Relations.Outgoing.Take( 12 ) )
        {
            string polyMarker = e.IsPolymorphicFanout ? " [Polymorphic Ghost]" : "";
            Log.Info( $"   ─[{e.Action}]─► {e.RecipientDocId} ({e.Instrument}){polyMarker}" );
        }
        if ( node.Relations.OutgoingCount > 12 ) Log.Info( $"   ... and {node.Relations.OutgoingCount - 12} more" );

        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"📥 Incoming Semantic Wires ({node.Relations.IncomingCount}):" );
        foreach ( var e in node.Relations.Incoming.Take( 12 ) )
        {
            Log.Info( $"   ◄─[{e.Action}]─ {e.AgentDocId} ({e.Instrument})" );
        }
        if ( node.Relations.IncomingCount > 12 ) Log.Info( $"   ... and {node.Relations.IncomingCount - 12} more" );
        Log.Info( $"==================================================" );
    }

    /// <summary>
    /// Performs an exhaustive diagnostic inspection on a single node, logging 100% of its members, attributes, and relationships without truncation.
    /// </summary>
    public static void DiagnoseFull( string idOrName )
    {
        var node = GetNode( idOrName );
        if ( node == null )
        {
            Log.Warning( $"[CodeAnalysis] Node '{idOrName}' not found in graph!" );
            return;
        }

        Log.Info( $"==================================================" );
        Log.Info( $"🔍 [FULL DIAGNOSTIC] {node.Header.Id} ({node.Header.Origin} / {node.Header.Category})" );
        Log.Info( $"   Title: '{node.Header.Title}' | Icon: '{node.Header.Icon}' | Group: '{node.Header.Group}'" );
        Log.Info( $"   File: {node.Header.FilePath}:{node.Header.LineNumber}" );
        Log.Info( $"   Summary: {node.Header.Summary}" );

        // Attributes
        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"📋 Attributes ({node.Attributes.Items.Count}):" );
        foreach ( var attr in node.Attributes.Items )
        {
            Log.Info( $"   • [{attr.Name}]" );
        }

        // Fields (if tracked in Members)
        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"🏷️ Fields ({node.Members.Fields.Count}):" );
        foreach ( var f in node.Members.Fields )
        {
            Log.Info( $"   • {f.TypeName} {f.Name}" );
        }

        // Properties
        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"📦 Properties ({node.Members.Properties.Count}):" );
        foreach ( var p in node.Members.Properties )
        {
            Log.Info( $"   • {p.TypeName} {p.Name} {(p.HasPropertyAttribute ? "[Property]" : "")}" );
        }

        // Methods
        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"⚡ Methods ({node.Members.Methods.Count}):" );
        foreach ( var m in node.Members.Methods )
        {
            Log.Info( $"   • {m.FullSignature}" );
        }

        // Outgoing Dependencies
        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"🔗 Outgoing Dependencies ({node.Relations.OutgoingCount}):" );
        foreach ( var e in node.Relations.Outgoing )
        {
            Log.Info( $"   ─[{e.Kind}]─> {e.TargetId} ({e.Details})" );
        }

        // Incoming References
        Log.Info( $"--------------------------------------------------" );
        Log.Info( $"📥 Incoming References ({node.Relations.IncomingCount}):" );
        foreach ( var e in node.Relations.Incoming )
        {
            Log.Info( $"   <─[{e.Kind}]─ {e.SourceId} ({e.Details})" );
        }

        Log.Info( $"==================================================" );
    }

    [EditorEvent.Hotload]
    public static void OnHotload()
    {
        Rebuild();
        DiagnoseFull( "CodeAnalysis" );
    }
}