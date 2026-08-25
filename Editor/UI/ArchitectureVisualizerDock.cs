#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Editor.Core;
using Editor.Core.Models;
using ArchitectureVisualizer.UI.Bridge;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using ArchitectureVisualizer.UI;
using ArchitectureVisualizer.UI.Floating;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI;

/// <summary>
/// Main full-window GPU architecture visualizer dock.
/// </summary>
[Dock( "Editor", "Architecture Visualizer", "schema" )]
public sealed class ArchitectureVisualizerDock : Widget
{
    private static readonly HashSet<ArchitectureVisualizerDock> ActiveInstances = new();

    private CanvasWidget? _canvas;
    private CanvasTopHud? _topHud;
    private CanvasForcesHud? _forcesHud;

    private readonly GraphFilterOptions _filters = new()
    {
        UserCodeOnly = false,
        IncludeSystemPrimitives = false,
        MaxNodesToLoad = 30000
    };

    private string? _savedSelectedId;

    public ArchitectureVisualizerDock( Widget parent ) : base( parent )
    {
        ActiveInstances.Add( this );

        Layout = Layout.Column();
        Layout.Margin = 0;
        Layout.Spacing = 0;

        BuildUI();
        RefreshCanvas( preservePositions: false );
    }

    private void BuildUI()
    {
        // 1. Native GPU Viewport Canvas
        _canvas = Layout.Add( new CanvasWidget( this ), 1 );

        _canvas.OnNodeSelected += idx =>
        {
            _savedSelectedId = (idx >= 0 && idx < _canvas.Registry.Count)
                ? _canvas.Registry.GetPayload( idx ).Id
                : null;
        };

        _canvas.OnNodeDoubleClicked += idx =>
        {
            if ( idx < 0 || idx >= _canvas.Registry.Count ) return;
            var payload = _canvas.Registry.GetPayload( idx );
            if ( !string.IsNullOrEmpty( payload.FilePath ) )
            {
                string path = payload.FilePath;
                if ( !Path.IsPathRooted( path ) && Project.Current != null )
                    path = Path.GetFullPath( Path.Combine( Project.Current.RootDirectory.FullName, path ) );

                if ( File.Exists( path ) ) CodeEditor.OpenFile( path, payload.LineNumber );
            }
        };

        // 2. Floating Top Search & Scope HUD
        _topHud = new CanvasTopHud( _canvas );
        _topHud.FilterUserOnly = _filters.UserCodeOnly;
        _topHud.IncludeSystemPrimitives = _filters.IncludeSystemPrimitives;
        _topHud.FilterComponentsOnly = _filters.ComponentsOnly;
        _topHud.FilterRazorOnly = _filters.RazorOnly;

        _topHud.OnSearchChanged += query =>
        {
            _filters.SearchQuery = query;
            RefreshCanvas( preservePositions: true );
        };

        _topHud.OnFilterChanged += () =>
        {
            _filters.UserCodeOnly = _topHud.FilterUserOnly;
            _filters.IncludeSystemPrimitives = _topHud.IncludeSystemPrimitives;
            _filters.ComponentsOnly = _topHud.FilterComponentsOnly;
            _filters.RazorOnly = _topHud.FilterRazorOnly;
            RefreshCanvas( preservePositions: true );
        };

        _topHud.OnRebuildRequested += () =>
        {
            DependencyGraphEngine.Rebuild();
            RefreshCanvas( preservePositions: false );
        };

        // 3. Floating Forces HUD (Top-Right)
        _forcesHud = new CanvasForcesHud( _canvas );
    }

    protected override void OnResize()
    {
        base.OnResize();

        if ( _topHud != null )
        {
            _topHud.Position = new Vector2( 14, 14 );
            _topHud.AdjustSize();
        }

        if ( _forcesHud != null )
        {
            _forcesHud.AdjustSize();
            _forcesHud.UpdatePosition();
        }
    }

    private void RefreshCanvas( bool preservePositions )
    {
        if ( _canvas == null ) return;
        var graph = DependencyGraphEngine.Current;
        if ( graph == null ) return;

        Dictionary<string, (Vector2 Pos, bool Pinned)>? savedState = null;
        if ( preservePositions && _canvas.Registry.Count > 0 )
        {
            savedState = new Dictionary<string, (Vector2, bool)>();
            for ( int i = 0; i < _canvas.Registry.Count; i++ )
            {
                var payload = _canvas.Registry.GetPayload( i );
                ref readonly var spatial = ref _canvas.Registry.GetSpatialRef( i );
                savedState[payload.Id] = (spatial.Position, spatial.IsPinned);
            }
        }

        GraphCanvasAdapter.PopulateCanvas( _canvas, graph, _filters );

        if ( savedState != null )
        {
            for ( int i = 0; i < _canvas.Registry.Count; i++ )
            {
                var payload = _canvas.Registry.GetPayload( i );
                if ( savedState.TryGetValue( payload.Id, out var state ) )
                {
                    ref var spatial = ref _canvas.Registry.GetSpatialRef( i );
                    spatial.Position = state.Pos;
                    spatial.SetFlag( NodeFlags.Pinned, state.Pinned );
                }
            }
            _canvas.SyncGpuBuffers();
            _canvas.Physics.Reheat( 0.20f );
        }

        _topHud?.UpdateStatus( _canvas.Registry.Count, graph.Nodes.Count, _canvas.Edges.Count );

        if ( !string.IsNullOrEmpty( _savedSelectedId ) )
        {
            for ( int i = 0; i < _canvas.Registry.Count; i++ )
            {
                if ( _canvas.Registry.GetPayload( i ).Id == _savedSelectedId )
                {
                    _canvas.SelectNode( i );
                    break;
                }
            }
        }

        _canvas.Update();
    }

    [EditorEvent.Hotload]
    public static void OnGlobalHotload()
    {
        ActiveInstances.RemoveWhere( x => !x.IsValid );
        foreach ( var instance in ActiveInstances ) instance.RebuildOnHotload();
    }

    private void RebuildOnHotload()
    {
        _canvas?.Dispose();

        Layout.Clear( true );
        BuildUI();
        RefreshCanvas( preservePositions: true );
        OnResize();
        Update();
    }
}