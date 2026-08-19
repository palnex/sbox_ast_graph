#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Editor;
using Editor.Core.Analysis;
using Editor.Core.Models;
using Sandbox;

namespace Editor.Core;

/// <summary>
/// Native s&box Editor Dock tab for testing and visualizing the Phase 1 Core Data Engine.
/// </summary>
[Dock( "Editor", "Dependency Graph", "account_tree", DockArea.Right )]
public class TestGraphDock : Widget
{
    private readonly Label _statsLabel;
    private readonly TextEdit _outputConsole;
    private readonly Button _runButton;

    public TestGraphDock( Widget? parent ) : base( parent, false )
    {
        Layout = Layout.Column();
        Layout.Margin = 12;
        Layout.Spacing = 8;
        SetStyles( "background-color: #1a1b20; color: #e0e0e0;" );

        // 1. Header
        var title = Layout.Add( new Label( "⚡ Dependency Graph Engine", this ) );
        title.SetStyles( "font-size: 16px; font-weight: bold; color: #58a6ff; margin-bottom: 4px;" );

        var subtitle = Layout.Add( new Label( "Phase 1 Core Data Engine Diagnostics (RAM Cache & Roslyn AST)", this ) );
        subtitle.SetStyles( "font-size: 11px; color: #8b949e; margin-bottom: 8px;" );

        // 2. Control Toolbar
        var toolRow = Layout.AddRow();
        toolRow.Spacing = 8;

        _runButton = toolRow.Add( new Button( "▶ Run Deep Diagnostics", this ) );
        _runButton.SetStyles( "background-color: #238636; color: #ffffff; font-weight: bold; padding: 6px 12px; border-radius: 4px;" );
        _runButton.Clicked += RunDiagnostics;

        var clearBtn = toolRow.Add( new Button( "Clear", this ) );
        clearBtn.SetStyles( "background-color: #30363d; color: #c9d1d9; padding: 6px 12px; border-radius: 4px;" );
        clearBtn.Clicked += () => _outputConsole.PlainText = string.Empty;

        toolRow.AddStretchCell();

        // 3. Quick Stats Badge
        _statsLabel = Layout.Add( new Label( "Click 'Run Deep Diagnostics' to scan project...", this ) );
        _statsLabel.SetStyles( "background-color: #0d1117; border: 1px solid #30363d; border-radius: 4px; padding: 8px; font-family: monospace; font-size: 12px; color: #7ee787;" );

        // 4. Output Terminal / Console Area
        var consoleHeader = Layout.Add( new Label( "DIAGNOSTIC REPORT:", this ) );
        consoleHeader.SetStyles( "font-size: 11px; font-weight: bold; color: #8b949e; margin-top: 6px;" );

        _outputConsole = Layout.Add( new TextEdit( this ), 1 );
        _outputConsole.ReadOnly = true;
        _outputConsole.SetStyles( "background-color: #0d1117; color: #c9d1d9; border: 1px solid #30363d; border-radius: 4px; font-family: monospace; font-size: 11px; padding: 8px;" );
    }

    [Menu( "Editor", "Tools/Dependency Graph Tab", "account_tree" )]
    public static void OpenWindow()
    {
        var window = new TestGraphDock( null );
        window.Show();
    }

    private void RunDiagnostics()
    {
        _runButton.Enabled = false;
        _runButton.Text = "Scanning...";

        try
        {
            var sw = Stopwatch.StartNew();
            var graph = DependencyGraphEngine.Rebuild();
            sw.Stop();

            var userNodes = graph.Nodes.Values.Where( n => n.Origin == NodeOrigin.UserProject ).ToList();
            var runtimeNodes = graph.Nodes.Values.Where( n => n.Origin == NodeOrigin.EngineRuntime ).ToList();
            var editorNodes = graph.Nodes.Values.Where( n => n.Origin == NodeOrigin.EngineEditor ).ToList();
            var systemNodes = graph.Nodes.Values.Where( n => n.Origin == NodeOrigin.SystemPrimitive ).ToList();

            _statsLabel.Text = $"Nodes: {graph.Nodes.Count} (User: {userNodes.Count} | Engine: {runtimeNodes.Count} | Editor: {editorNodes.Count}) | Edges: {graph.Edges.Count} | Time: {sw.ElapsedMilliseconds}ms";

            var sb = new StringBuilder();
            sb.AppendLine( "================================================================================" );
            sb.AppendLine( $"🔍 DEPENDENCY GRAPH REPORT (Built in {sw.ElapsedMilliseconds} ms)" );
            sb.AppendLine( "================================================================================" );
            sb.AppendLine( $"Total Nodes: {graph.Nodes.Count} | Total Edges: {graph.Edges.Count}" );
            sb.AppendLine( $"  ├─ 🎮 User Project Nodes:    {userNodes.Count}" );
            sb.AppendLine( $"  ├─ ⚙️  Engine Runtime Nodes:  {runtimeNodes.Count}" );
            sb.AppendLine( $"  ├─ 🛠️  Engine Editor Nodes:   {editorNodes.Count}" );
            sb.AppendLine( $"  └─ 📚 System/Primitive Nodes: {systemNodes.Count}" );
            sb.AppendLine();

            // 1. Categories
            sb.AppendLine( "--- 📊 SANDBOX CATEGORIES ---" );
            foreach ( var group in graph.Nodes.Values.GroupBy( n => n.Category ).OrderByDescending( g => g.Count() ) )
            {
                sb.AppendLine( $"  • {group.Key}: {group.Count()}" );
            }
            sb.AppendLine();

            // 2. Edge Kinds
            sb.AppendLine( "--- 🔗 RELATION KINDS ---" );
            foreach ( var group in graph.Edges.GroupBy( e => e.Kind ).OrderByDescending( g => g.Count() ) )
            {
                sb.AppendLine( $"  • {group.Key}: {group.Count()} links" );
            }
            sb.AppendLine();

            // 3. User Code Sample
            sb.AppendLine( "--- 🔬 USER CODE INSPECTION ---" );
            foreach ( var node in userNodes.Take( 5 ) )
            {
                string doc = string.IsNullOrWhiteSpace( node.Summary ) ? "None" : node.Summary;
                sb.AppendLine( $"▶ [{node.Category}] {node.Id} (File: {node.FilePath})" );
                sb.AppendLine( $"    Summary: \"{doc}\"" );
                sb.AppendLine( $"    Props ({node.Properties.Count}): {string.Join( ", ", node.Properties.Take( 4 ).Select( p => p.Name ) )}" );
                sb.AppendLine( $"    Methods ({node.Methods.Count}): {string.Join( ", ", node.Methods.Take( 4 ).Select( m => m.Name ) )}" );
            }
            sb.AppendLine();

            // 4. Hubs & God Classes
            var metrics = GraphMetrics.Calculate( graph, 5 );
            sb.AppendLine( "--- 👑 TOP HUBS (Most Depended On) ---" );
            foreach ( var hub in metrics.TopHubs )
            {
                sb.AppendLine( $"  ★ {hub.Key} ➔ {hub.Value} incoming references" );
            }
            sb.AppendLine();

            sb.AppendLine( "--- 🧠 TOP GOD CLASSES (Most Outgoing Dependencies) ---" );
            foreach ( var god in metrics.TopGodNodes )
            {
                sb.AppendLine( $"  ⚠ {god.Key} ➔ references {god.Value} classes" );
            }
            sb.AppendLine();

            // 5. Cycles
            var cycles = GraphAlgorithms.DetectCycles( graph );
            sb.AppendLine( $"--- 🔄 CIRCULAR DEPENDENCIES ({cycles.Count} detected) ---" );
            if ( cycles.Count == 0 )
            {
                sb.AppendLine( "  ✅ Clean DAG! No circular dependency loops detected." );
            }
            else
            {
                foreach ( var cycle in cycles.Take( 5 ) )
                {
                    sb.AppendLine( $"  ⚠️ {cycle.Representation}" );
                }
            }

            _outputConsole.PlainText = sb.ToString();
            Log.Info( $"[Graph Engine] Diagnostics completed in {sw.ElapsedMilliseconds}ms! Check the 'Dependency Graph' dock tab." );
        }
        catch ( Exception ex )
        {
            _outputConsole.PlainText = $"[ERROR] Failed to run diagnostics:\n{ex}";
            Log.Error( ex );
        }
        finally
        {
            _runButton.Enabled = true;
            _runButton.Text = "▶ Run Deep Diagnostics";
        }
    }
}