#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

using Editor.Core;
using Editor.Core.Models;
using ArchitectureVisualizer.UI.CanvasEngine.Core;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using ArchitectureVisualizer.UI.CanvasEngine.Rendering;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Widgets;

/// <summary>
/// Data-oriented interactive 2D Canvas Widget featuring world-anchored inspection and Z-level picking.
/// </summary>
public class CanvasWidget : Widget
{
    public CanvasTransform Transform { get; } = new();
    public CanvasTheme Theme { get; set; } = CanvasTheme.DefaultDark;
    public SpatialRegistry Registry { get; } = new();
    public SleepyPhysicsSolver Physics { get; } = new();
    public List<CanvasEdge> Edges { get; } = new();
    public GraphCanvasRenderer Renderer { get; } = new();

    public int SelectedNodeIndex { get; private set; } = -1;
    public int HoveredNodeIndex { get; private set; } = -1;

    public event Action<int>? OnNodeSelected;
    public event Action<int>? OnNodeDoubleClicked;

    // Smooth Camera
    private Vector2 _targetPan;
    private float _targetZoom = 1.0f;
    private bool _isAnimatingCamera;

    // Focus / Neighbors Cache
    private readonly HashSet<int> _focusedNeighbors = new();

    // Interaction & Drag State
    private bool _isPanning;
    private Vector2 _panStartMouse;
    private Vector2 _panStartOffset;

    private int _draggedNodeIndex = -1;
    private Vector2 _dragOffset;
    private Vector2 _dragStartMouse;
    private Vector2 _currentMouseWorldPos;
    private bool _isDraggingNode;
    private bool _dragNodeWasPinnedOriginally;

    // World-Anchored Floating Inspector Card
    private readonly FloatingInspectorOverlay _inspectorOverlay;

    public CanvasWidget( Widget parent ) : base( parent )
    {
        FocusMode = FocusMode.Click;
        Cursor = CursorShape.Arrow;
        AcceptDrops = true;
        MouseTracking = true;

        _targetPan = Transform.PanOffset;
        _targetZoom = Transform.Zoom;

        // Floating inspection card anchored to selected node in world space
        _inspectorOverlay = new FloatingInspectorOverlay( this );
        _inspectorOverlay.Visible = false;
        _inspectorOverlay.OnNavigateRequested += targetId =>
        {
            for ( int i = 0; i < Registry.Count; i++ )
            {
                if ( Registry.GetPayload( i ).Id == targetId )
                {
                    FocusOnNode( i, zoom: 1.3f );
                    break;
                }
            }
        };
    }

    public void Clear()
    {
        Registry.Clear();
        Edges.Clear();
        SelectedNodeIndex = -1;
        HoveredNodeIndex = -1;
        _draggedNodeIndex = -1;
        _focusedNeighbors.Clear();
        _inspectorOverlay.Visible = false;
        Update();
    }

    public void FocusOnNode( int nodeIndex, float zoom = 1.3f )
    {
        if ( nodeIndex < 0 || nodeIndex >= Registry.Count ) return;
        Vector2 pos = Registry.GetSpatialRef( nodeIndex ).Position;
        AnimateTo( pos, zoom );
        SelectNode( nodeIndex );
    }

    public void AnimateTo( Vector2 targetWorldPos, float targetZoom = 1.2f )
    {
        _targetZoom = Math.Clamp( targetZoom, Transform.MinZoom, Transform.MaxZoom );
        _targetPan = -targetWorldPos * _targetZoom;
        _isAnimatingCamera = true;
        Update();
    }

    public void SelectNode( int nodeIndex )
    {
        if ( SelectedNodeIndex == nodeIndex ) return;

        if ( SelectedNodeIndex >= 0 && SelectedNodeIndex < Registry.Count )
            Registry.GetSpatialRef( SelectedNodeIndex ).SetFlag( NodeFlags.Selected, false );

        SelectedNodeIndex = nodeIndex;

        if ( SelectedNodeIndex >= 0 && SelectedNodeIndex < Registry.Count )
        {
            Registry.GetSpatialRef( SelectedNodeIndex ).SetFlag( NodeFlags.Selected, true );
            var payload = Registry.GetPayload( SelectedNodeIndex );
            _inspectorOverlay.Bind( payload, this );
            _inspectorOverlay.Visible = true;
        }
        else
        {
            _inspectorOverlay.Visible = false;
        }

        RebuildFocusedNeighbors();
        UpdateFloatingCardPosition();
        OnNodeSelected?.Invoke( SelectedNodeIndex );
        Update();
    }

    private void RebuildFocusedNeighbors()
    {
        _focusedNeighbors.Clear();
        int active = HoveredNodeIndex >= 0 ? HoveredNodeIndex : SelectedNodeIndex;
        if ( active < 0 ) return;

        for ( int i = 0; i < Edges.Count; i++ )
        {
            var edge = Edges[i];
            if ( edge.SourceIndex == active ) _focusedNeighbors.Add( edge.TargetIndex );
            else if ( edge.TargetIndex == active ) _focusedNeighbors.Add( edge.SourceIndex );
        }
    }

    [EditorEvent.Frame]
    public void FrameTick()
    {
        // 1. If currently dragging a node, continuously lock its position to the mouse
        if ( _isDraggingNode && _draggedNodeIndex >= 0 && _draggedNodeIndex < Registry.Count )
        {
            ref var draggedSpatial = ref Registry.GetSpatialRef( _draggedNodeIndex );
            draggedSpatial.Position = _currentMouseWorldPos - _dragOffset;
            draggedSpatial.Velocity = Vector2.Zero; // Prevent physics from pushing it away
        }

        // 2. Step Physics if active
        if ( !Physics.IsSleeping && (!Physics.PauseDuringPlay || !Game.IsPlaying) )
        {
            Physics.Step( Registry, Edges, RealTime.Delta, Theme.NodeSizeScale );
            UpdateFloatingCardPosition();
            Update();
        }

        // 3. Smooth Camera Animation
        if ( _isAnimatingCamera )
        {
            float dt = RealTime.Delta;
            float t = 1.0f - MathF.Exp( -12.0f * dt );

            Transform.PanOffset = Vector2.Lerp( Transform.PanOffset, _targetPan, t );
            Transform.Zoom = MathX.Lerp( Transform.Zoom, _targetZoom, t );

            if ( (Transform.PanOffset - _targetPan).Length < 0.1f && MathF.Abs( Transform.Zoom - _targetZoom ) < 0.001f )
            {
                Transform.PanOffset = _targetPan;
                Transform.Zoom = _targetZoom;
                _isAnimatingCamera = false;
            }

            UpdateFloatingCardPosition();
            Update();
        }
    }

    private void UpdateFloatingCardPosition()
    {
        if ( !_inspectorOverlay.Visible || SelectedNodeIndex < 0 || SelectedNodeIndex >= Registry.Count )
            return;

        Vector2 worldPos = Registry.GetSpatialRef( SelectedNodeIndex ).Position;
        Vector2 screenAnchor = Transform.WorldToScreen( worldPos );

        Vector2 targetPos = screenAnchor + new Vector2( 22, -30 );

        // Clamping inside Dock borders
        float pad = 12f;
        float clampedX = Math.Clamp( targetPos.x, pad, MathF.Max( pad, Width - _inspectorOverlay.Width - pad ) );
        float clampedY = Math.Clamp( targetPos.y, pad, MathF.Max( pad, Height - _inspectorOverlay.Height - pad ) );

        _inspectorOverlay.Position = new Vector2( clampedX, clampedY );
    }

    protected override void OnResize()
    {
        base.OnResize();
        Transform.ViewportSize = Size;
        UpdateFloatingCardPosition();
        Update();
    }

    protected override void OnPaint()
    {
        Transform.ViewportSize = Size;
        Rect visibleWorldRect = Transform.GetVisibleWorldRect( margin: 120f );

        PaintContext ctx = new( Transform, Theme, visibleWorldRect )
        {
            HoveredNodeIndex = HoveredNodeIndex,
            SelectedNodeIndex = SelectedNodeIndex,
            FocusedNeighborIndices = _focusedNeighbors
        };

        // 1. Background
        Paint.ClearPen();
        Paint.SetBrush( Theme.BackgroundColor );
        Paint.DrawRect( LocalRect );

        // 2. Grid
        if ( Theme.ShowGrid ) DrawGrid();

        // 3. Render Graph Pipeline
        Renderer.Render( ctx, Registry, Edges );
    }

    private void DrawGrid()
    {
        float step = Theme.GridStep * Transform.Zoom;
        if ( step < 14f ) return;

        Vector2 center = Size * 0.5f;
        float startX = (Transform.PanOffset.x + center.x) % step;
        float startY = (Transform.PanOffset.y + center.y) % step;

        Paint.ClearBrush();
        Paint.SetPen( Theme.GridColor, 1f );

        for ( float x = startX; x < Size.x; x += step )
            Paint.DrawLine( new Vector2( x, 0 ), new Vector2( x, Size.y ) );

        for ( float y = startY; y < Size.y; y += step )
            Paint.DrawLine( new Vector2( 0, y ), new Vector2( Size.x, y ) );
    }

    protected override void OnMousePress( MouseEvent e )
    {
        bool isPan = e.MiddleMouseButton || e.RightMouseButton || (e.LeftMouseButton && Editor.Application.KeyboardModifiers.HasFlag( Sandbox.KeyboardModifiers.Alt ));
        if ( isPan )
        {
            _isPanning = true;
            _panStartMouse = e.LocalPosition;
            _panStartOffset = Transform.PanOffset;
            _isAnimatingCamera = false;
            Cursor = CursorShape.SizeAll;
            e.Accepted = true;
            return;
        }

        if ( e.LeftMouseButton )
        {
            Vector2 worldPos = Transform.ScreenToWorld( e.LocalPosition );
            _currentMouseWorldPos = worldPos;

            // 100% Guarantee: If a node is currently hovered, select EXACTLY that node!
            int targetIdx = HoveredNodeIndex >= 0 ? HoveredNodeIndex : Registry.PickNode( worldPos, Theme.NodeSizeScale );

            if ( targetIdx >= 0 )
            {
                _draggedNodeIndex = targetIdx;
                _dragOffset = worldPos - Registry.GetSpatialRef( targetIdx ).Position;
                _dragStartMouse = e.LocalPosition;
                _isDraggingNode = false;
                _dragNodeWasPinnedOriginally = Registry.GetSpatialRef( targetIdx ).IsPinned;

                SelectNode( targetIdx );
            }
            else
            {
                SelectNode( -1 );
            }

            e.Accepted = true;
        }
    }

    protected override void OnMouseMove( MouseEvent e )
    {
        Vector2 worldPos = Transform.ScreenToWorld( e.LocalPosition );
        _currentMouseWorldPos = worldPos;

        if ( _isPanning )
        {
            Transform.PanOffset = _panStartOffset + (e.LocalPosition - _panStartMouse);
            _targetPan = Transform.PanOffset;
            UpdateFloatingCardPosition();
            Update();
            return;
        }

        if ( _draggedNodeIndex >= 0 )
        {
            // Check 5px threshold
            if ( !_isDraggingNode )
            {
                if ( (e.LocalPosition - _dragStartMouse).Length >= 5.0f )
                {
                    _isDraggingNode = true;
                    Registry.GetSpatialRef( _draggedNodeIndex ).SetFlag( NodeFlags.Pinned, true ); // Lock in physics
                    Cursor = CursorShape.DragMove;
                }
            }

            if ( _isDraggingNode )
            {
                Registry.GetSpatialRef( _draggedNodeIndex ).Position = worldPos - _dragOffset;
                Registry.GetSpatialRef( _draggedNodeIndex ).Velocity = Vector2.Zero;
                Physics.WakeUp();
                UpdateFloatingCardPosition();
                Update();
                return;
            }
        }

        // Hover Detection with correct NodeSizeScale
        int hovered = Registry.PickNode( worldPos, Theme.NodeSizeScale );
        if ( HoveredNodeIndex != hovered )
        {
            if ( HoveredNodeIndex >= 0 && HoveredNodeIndex < Registry.Count )
                Registry.GetSpatialRef( HoveredNodeIndex ).SetFlag( NodeFlags.Hovered, false );

            HoveredNodeIndex = hovered;

            if ( HoveredNodeIndex >= 0 && HoveredNodeIndex < Registry.Count )
                Registry.GetSpatialRef( HoveredNodeIndex ).SetFlag( NodeFlags.Hovered, true );

            RebuildFocusedNeighbors();
            Cursor = HoveredNodeIndex >= 0 ? CursorShape.Finger : CursorShape.Arrow;
            Update();
        }
    }

    protected override void OnMouseReleased( MouseEvent e )
    {
        if ( _isPanning )
        {
            _isPanning = false;
            Cursor = CursorShape.Arrow;
            Update();
        }

        if ( _draggedNodeIndex >= 0 )
        {
            bool wasActuallyDragged = _isDraggingNode;

            // Restore original pin state if node wasn't pinned before drag
            if ( !_dragNodeWasPinnedOriginally )
            {
                Registry.GetSpatialRef( _draggedNodeIndex ).SetFlag( NodeFlags.Pinned, false );
            }

            _draggedNodeIndex = -1;
            _isDraggingNode = false;
            Cursor = HoveredNodeIndex >= 0 ? CursorShape.Finger : CursorShape.Arrow;

            // Wake up physics ONLY if the user actually dragged the node past the threshold!
            if ( wasActuallyDragged )
            {
                Physics.WakeUp();
            }

            Update();
        }
    }

    protected override void OnMouseWheel( WheelEvent e )
    {
        _isAnimatingCamera = false;
        float factor = e.Delta > 0 ? 1.15f : 0.85f;
        Transform.ZoomAt( e.Position, factor );
        _targetZoom = Transform.Zoom;
        _targetPan = Transform.PanOffset;
        UpdateFloatingCardPosition();
        Update();
        e.Accepted = true;
    }

    protected override void OnDoubleClick( MouseEvent e )
    {
        Vector2 worldPos = Transform.ScreenToWorld( e.LocalPosition );
        int idx = Registry.PickNode( worldPos );
        if ( idx >= 0 )
        {
            OnNodeDoubleClicked?.Invoke( idx );
            e.Accepted = true;
        }
    }

    protected override void OnContextMenu( ContextMenuEvent e )
    {
        Vector2 worldPos = Transform.ScreenToWorld( e.LocalPosition );
        int idx = Registry.PickNode( worldPos );

        var menu = new Menu( this );

        if ( idx >= 0 )
        {
            var payload = Registry.GetPayload( idx );
            menu.AddHeading( payload.Title );

            if ( !string.IsNullOrEmpty( payload.FilePath ) )
            {
                menu.AddOption( "Open in Code Editor", "code", () =>
                {
                    string path = payload.FilePath;
                    if ( !Path.IsPathRooted( path ) && Project.Current != null )
                        path = Path.GetFullPath( Path.Combine( Project.Current.RootDirectory.FullName, path ) );

                    if ( File.Exists( path ) ) CodeEditor.OpenFile( path, payload.LineNumber );
                } );
            }

            menu.AddOption( "Focus Camera", "my_location", () => FocusOnNode( idx, 1.4f ) );

            bool isPinned = Registry.GetSpatialRef( idx ).IsPinned;
            menu.AddOption( isPinned ? "Unpin Position" : "Pin in Place 📌", "push_pin", () =>
            {
                Registry.GetSpatialRef( idx ).SetFlag( NodeFlags.Pinned, !isPinned );
                Update();
            } );

            menu.AddSeparator();
            menu.AddOption( "Copy Type Name", "content_copy", () => EditorUtility.Clipboard.Copy( payload.Title ) );
        }
        else
        {
            menu.AddOption( "Fit All to Screen", "fit_screen", FitToScreen );
            menu.AddOption( "Reheat Physics 🔥", "bolt", () => { Physics.WakeUp(); Update(); } );
        }

        menu.OpenAtCursor();
        e.Accepted = true;
    }

    public void FitToScreen()
    {
        if ( Registry.Count == 0 ) return;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        var spatials = Registry.GetReadOnlySpatialSpan();

        for ( int i = 0; i < spatials.Length; i++ )
        {
            if ( spatials[i].IsHidden ) continue;
            var p = spatials[i].Position;
            if ( p.x < minX ) minX = p.x;
            if ( p.x > maxX ) maxX = p.x;
            if ( p.y < minY ) minY = p.y;
            if ( p.y > maxY ) maxY = p.y;
        }

        Vector2 center = new( (minX + maxX) * 0.5f, (minY + maxY) * 0.5f );
        float spanX = (maxX - minX) + 160f;
        float spanY = (maxY - minY) + 160f;

        float zoomX = Size.x / spanX;
        float zoomY = Size.y / spanY;
        float fitZoom = Math.Clamp( MathF.Min( zoomX, zoomY ), Transform.MinZoom, 1.2f );

        AnimateTo( center, fitZoom );
    }
}

/// <summary>
/// Lightweight floating inspection card anchored to the active node in world space.
/// </summary>
public sealed class FloatingInspectorOverlay : Widget
{
    private readonly Label _titleLabel;
    private readonly Label _namespaceLabel;
    private readonly Label _summaryLabel;
    private readonly Button _openIdeButton;
    private readonly Widget _depsContainer;

    private NodePayload? _currentPayload;
    public event Action<string>? OnNavigateRequested;

    public FloatingInspectorOverlay( Widget parent ) : base( parent )
    {
        FocusMode = FocusMode.Click;
        Cursor = CursorShape.Arrow;
        Size = new Vector2( 260, 240 );

        SetStyles( @"
            background-color: rgba( 18, 20, 26, 0.94 );
            border: 1px solid rgba( 255, 255, 255, 0.14 );
            border-radius: 8px;
            padding: 8px;
        " );

        Layout = Layout.Column();
        Layout.Margin = 4;
        Layout.Spacing = 4;

        var header = Layout.AddRow();
        _titleLabel = header.Add( new Label( "Node Title", this ), 1 );
        _titleLabel.SetStyles( "font-weight: bold; font-size: 12px; color: #ffffff;" );

        var closeBtn = header.Add( new Button( "close", this ) );
        closeBtn.Clicked = () => Visible = false;
        closeBtn.FixedWidth = 20;
        closeBtn.FixedHeight = 20;

        _namespaceLabel = Layout.Add( new Label( "", this ) );
        _namespaceLabel.SetStyles( "color: #8b949e; font-size: 10px;" );

        _openIdeButton = Layout.Add( new Button( "Open in Code Editor", "code", this ) );
        _openIdeButton.Clicked = OnOpenInIdeClicked;
        _openIdeButton.FixedHeight = 24;

        _summaryLabel = Layout.Add( new Label( "", this ) );
        _summaryLabel.SetStyles( "color: #c9d1d9; font-size: 10px;" );
        _summaryLabel.WordWrap = true;

        var scroll = Layout.Add( new ScrollArea( this ), 1 );
        scroll.Canvas = new Widget( scroll );
        scroll.Canvas.Layout = Layout.Column();
        scroll.Canvas.Layout.Spacing = 2;
        _depsContainer = scroll.Canvas;
    }

    public void Bind( NodePayload payload, CanvasWidget canvas )
    {
        _currentPayload = payload;
        _titleLabel.Text = payload.Title;
        _namespaceLabel.Text = payload.Subtitle;
        _summaryLabel.Text = string.IsNullOrWhiteSpace( payload.Summary ) ? "No summary provided." : payload.Summary.Trim();
        _openIdeButton.Enabled = !string.IsNullOrEmpty( payload.FilePath );

        _depsContainer.Layout.Clear( true );

        var graph = DependencyGraphEngine.Current;
        if ( graph != null )
        {
            var outgoing = graph.GetOutgoingEdges( payload.Id );
            foreach ( var edge in outgoing )
            {
                string name = graph.Nodes.TryGetValue( edge.TargetId, out var gn ) ? gn.Name : edge.TargetId;
                var btn = new Button( $"→ {name} ({edge.Kind})", _depsContainer );
                btn.SetStyles( "text-align: left; font-size: 10px; padding: 2px;" );
                string targetId = edge.TargetId;
                btn.Clicked = () => OnNavigateRequested?.Invoke( targetId );
                _depsContainer.Layout.Add( btn );
            }
        }

        AdjustSize();
    }

    private void OnOpenInIdeClicked()
    {
        if ( _currentPayload == null || string.IsNullOrEmpty( _currentPayload.FilePath ) ) return;
        string path = _currentPayload.FilePath;
        if ( !Path.IsPathRooted( path ) && Project.Current != null )
            path = Path.GetFullPath( Path.Combine( Project.Current.RootDirectory.FullName, path ) );

        if ( File.Exists( path ) ) CodeEditor.OpenFile( path, _currentPayload.LineNumber );
    }

    protected override void OnMousePress( MouseEvent e ) => e.Accepted = true;
    protected override void OnMouseMove( MouseEvent e ) => e.Accepted = true;
    protected override void OnMouseWheel( WheelEvent e ) => e.Accepted = true;
}