#nullable enable
using System;
using System.Linq;
using Editor.Core;
using Editor.Core.Analysis;
using Editor.Core.Models;
using ArchitectureVisualizer.UI.Bridge;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using ArchitectureVisualizer.UI.CanvasEngine.Widgets;
using ArchitectureVisualizer.UI.Components;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI;

/// <summary>
/// Main dockable window for the Architecture Visualizer & Dependency Graph tool.
/// </summary>
[Dock( "Editor", "Architecture Visualizer", "schema" )]
public sealed class ArchitectureVisualizerDock : Widget
{
    private readonly LineEdit _searchBox;
    private readonly Checkbox _userOnlyCheck;
    private readonly Checkbox _componentsOnlyCheck;
    private readonly Checkbox _razorOnlyCheck;
    private readonly Label _statusLabel;

    private readonly Splitter _splitter;
    private readonly Widget _leftSidebar;
    private readonly Widget _classListContainer;
    private readonly CanvasWidget _canvas;
    private readonly NodeInspectorWidget _inspector;

    private readonly GraphFilterOptions _filters = new();

    public ArchitectureVisualizerDock( Widget parent ) : base( parent )
    {
        Layout = Layout.Column();
        Layout.Margin = 0;
        Layout.Spacing = 0;

        // ================= 1. TOP TOOLBAR =================
        var toolbar = Layout.Add( new Widget( this ) );
        toolbar.FixedHeight = 42;
        toolbar.Layout = Layout.Row();
        toolbar.Layout.Margin = 8;
        toolbar.Layout.Spacing = 8;

        _searchBox = toolbar.Layout.Add( new LineEdit( toolbar ) );
        _searchBox.PlaceholderText = "Search classes, interfaces, namespaces...";
        _searchBox.ClearButtonEnabled = true;
        _searchBox.FixedWidth = 260;
        _searchBox.TextEdited += OnSearchEdited;

        var rebuildBtn = toolbar.Layout.Add( new Button( "Rebuild", "refresh", toolbar ) );
        rebuildBtn.Clicked = OnRebuildClicked;

        _userOnlyCheck = toolbar.Layout.Add( new Checkbox( "User Code Only", toolbar ) );
        _userOnlyCheck.Value = true;
        _userOnlyCheck.Toggled += () => { _filters.UserCodeOnly = _userOnlyCheck.Value; RefreshVisualizer(); };

        _componentsOnlyCheck = toolbar.Layout.Add( new Checkbox( "Components", toolbar ) );
        _componentsOnlyCheck.Value = false;
        _componentsOnlyCheck.Toggled += () => { _filters.ComponentsOnly = _componentsOnlyCheck.Value; RefreshVisualizer(); };

        _razorOnlyCheck = toolbar.Layout.Add( new Checkbox( "Razor UI", toolbar ) );
        _razorOnlyCheck.Value = false;
        _razorOnlyCheck.Toggled += () => { _filters.RazorOnly = _razorOnlyCheck.Value; RefreshVisualizer(); };

        toolbar.Layout.AddStretchCell();

        _statusLabel = toolbar.Layout.Add( new Label( "Ready", toolbar ) );

        // ================= 2. 3-PANEL SPLITTER =================
        _splitter = Layout.Add( new Splitter( this ), 1 );
        _splitter.IsHorizontal = true;

        // --- Left Column: Explorer ---
        _leftSidebar = new Widget( _splitter );
        _leftSidebar.Layout = Layout.Column();
        _leftSidebar.Layout.Margin = 8;
        _leftSidebar.Layout.Spacing = 6;
        _leftSidebar.FixedWidth = 240;

        _leftSidebar.Layout.Add( new Label( "EXPLORER", _leftSidebar ) );

        var scrollArea = _leftSidebar.Layout.Add( new ScrollArea( _leftSidebar ), 1 );
        scrollArea.Canvas = new Widget( scrollArea );
        scrollArea.Canvas.Layout = Layout.Column();
        scrollArea.Canvas.Layout.Spacing = 2;
        _classListContainer = scrollArea.Canvas;

        _splitter.AddWidget( _leftSidebar );

        // --- Center Column: 2D Canvas ---
        _canvas = new CanvasWidget( _splitter );
        _canvas.OnNodeSelected += OnCanvasNodeSelected;
        _canvas.OnNodeDoubleClicked += OnCanvasNodeDoubleClicked;
        _splitter.AddWidget( _canvas );

        // --- Right Column: Node Inspector ---
        _inspector = new NodeInspectorWidget( _splitter );
        _inspector.FixedWidth = 300;
        _inspector.OnNavigateToNodeRequested += OnInspectorNavigateRequested;
        _splitter.AddWidget( _inspector );

        // Initial Population
        RefreshVisualizer();
    }

    private void OnSearchEdited( string query )
    {
        _filters.SearchQuery = query;
        RefreshVisualizer();
    }

    private void OnRebuildClicked()
    {
        _statusLabel.Text = "Rebuilding graph...";
        // Triggers phase 1 engine rebuild
        DependencyGraphEngine.Rebuild();
        RefreshVisualizer();
    }

    private void RefreshVisualizer()
    {
        var graph = DependencyGraphEngine.Current;
        if ( graph == null )
        {
            _statusLabel.Text = "Graph is empty.";
            return;
        }

        // 1. Populate visual 2D Canvas
        GraphCanvasAdapter.PopulateCanvas( _canvas, graph, _filters );

        // 2. Populate Left Sidebar List
        RebuildSidebarList( graph );

        // 3. Update Status
        _statusLabel.Text = $"{_canvas.Nodes.Count} visible nodes | {_canvas.Edges.Count} connections (Total: {graph.Nodes.Count:N0})";
    }

    private void RebuildSidebarList( DependencyGraph graph )
    {
        _classListContainer.DestroyChildren();

        foreach ( var canvasNode in _canvas.Nodes )
        {
            var btn = _classListContainer.Layout.Add( new Button( canvasNode.Title, canvasNode.Icon, _classListContainer ) );
            btn.Clicked = () =>
            {
                _canvas.FocusOnNode( canvasNode );
                _inspector.InspectNode( canvasNode, graph );
            };
        }
    }

    private void OnCanvasNodeSelected( CanvasNode? node )
    {
        var graph = DependencyGraphEngine.Current;
        _inspector.InspectNode( node, graph );
    }

    private void OnCanvasNodeDoubleClicked( CanvasNode node )
    {
        if ( node.UserData is GraphNode gn && !string.IsNullOrEmpty( gn.FilePath ) )
        {
            string fullPath = gn.FilePath;
            if ( !System.IO.Path.IsPathRooted( fullPath ) && Project.Current != null )
            {
                fullPath = System.IO.Path.GetFullPath( System.IO.Path.Combine( Project.Current.RootDirectory.FullName, fullPath ) );
            }

            if ( System.IO.File.Exists( fullPath ) )
            {
                CodeEditor.OpenFile( fullPath, 1 );
            }
        }
    }

    private void OnInspectorNavigateRequested( string targetNodeId )
    {
        var targetCanvasNode = _canvas.Nodes.FirstOrDefault( n => n.Id == targetNodeId );
        if ( targetCanvasNode != null )
        {
            _canvas.FocusOnNode( targetCanvasNode );
            _inspector.InspectNode( targetCanvasNode, DependencyGraphEngine.Current );
        }
    }
}