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
/// High-Performance Zero-Copy Graph Canvas Widget rendering directly to Native GPU Swapchain via SceneRenderingWidget.
/// </summary>
public class CanvasWidget : SceneRenderingWidget, IDisposable
{
    private Scene _scene = null!;
    private SceneWorld _sceneWorld = null!;
    private GameObject _cameraObject = null!;
    private CameraComponent _camera = null!;
    public GraphCameraController CameraController { get; private set; } = null!;

    private GraphNodeSceneObject _nodeObject = null!;
    private GraphEdgeSceneObject _edgeObject = null!;

    private Transform[] _nodeTransformsStaging = new Transform[2048];
    private Color[] _nodeColorsStaging = new Color[2048];
    private (Vector3 Start, Vector3 End, Color32 Color, EdgeStyle Style, float Speed)[] _edgeStaging = new (Vector3, Vector3, Color32, EdgeStyle, float)[8192];

    public CanvasTheme Theme { get; set; } = CanvasTheme.DefaultDark;
    public SpatialRegistry Registry { get; } = new();
    public SleepyPhysicsSolver Physics { get; } = new();
    public List<CanvasEdge> Edges { get; } = new();

    public int SelectedNodeIndex { get; private set; } = -1;
    public int HoveredNodeIndex { get; private set; } = -1;

    public event Action<int>? OnNodeSelected;
    public event Action<int>? OnNodeDoubleClicked;

    private readonly HashSet<int> _focusedNeighbors = new();

    private bool _isPanning;
    private bool _isOrbiting;
    private Vector2 _lastMousePos;

    private int _draggedNodeIndex = -1;
    private Vector3 _dragOffset;
    private Vector2 _dragStartMouse;
    private bool _isDraggingNode;
    private bool _dragNodeWasPinnedOriginally;

    private readonly FloatingInspectorOverlay _inspectorOverlay;

    public CanvasWidget( Widget parent ) : base( parent )
    {
        FocusMode = FocusMode.Click;
        Cursor = CursorShape.Arrow;
        AcceptDrops = true;
        MouseTracking = true;

        InitScene();

        _inspectorOverlay = new FloatingInspectorOverlay( this );
        _inspectorOverlay.Visible = false;
        _inspectorOverlay.OnNavigateRequested += targetId =>
        {
            for ( int i = 0; i < Registry.Count; i++ )
            {
                if ( Registry.GetPayload( i ).Id == targetId )
                {
                    FocusOnNode( i, targetSize: 1500f );
                    break;
                }
            }
        };
    }

    private void InitScene()
    {
        _scene = new Scene();
        _sceneWorld = _scene.SceneWorld;

        _cameraObject = _scene.CreateObject();
        _camera = _cameraObject.Components.Create<CameraComponent>();
        _camera.ZNear = 1.0f;
        _camera.ZFar = 200000.0f;
        _camera.FieldOfView = 60.0f;
        _camera.BackgroundColor = Theme.BackgroundColor;

        Scene = _scene;
        Camera = _camera;
        EnableEngineOverlays = false;

        CameraController = new GraphCameraController( _camera, _cameraObject );

        _nodeObject = new GraphNodeSceneObject( _sceneWorld );
        _edgeObject = new GraphEdgeSceneObject( _sceneWorld );
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

        SyncGpuBuffers();
        Update();
    }

    public void FocusOnNode( int nodeIndex, float targetSize = 1500f )
    {
        if ( nodeIndex < 0 || nodeIndex >= Registry.Count ) return;
        Vector3 pos = GetNodeWorldPosition3D( nodeIndex );
        CameraController.AnimateTo( pos, targetSize );
        SelectNode( nodeIndex );
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
        SyncGpuBuffers();
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

    public Vector3 GetNodeWorldPosition3D( int nodeIndex )
    {
        ref readonly var spatial = ref Registry.GetSpatialRef( nodeIndex );
        float z = CameraController.Is3DMode ? (spatial.ZLevel * 45.0f) : 0f;
        return new Vector3( spatial.Position.x, spatial.Position.y, z );
    }

    [EditorEvent.Frame]
    public void FrameTick()
    {
        if ( !IsValid || _camera == null || CameraController == null || _sceneWorld == null || !_sceneWorld.IsValid() )
            return;

        if ( _nodeObject == null || !_nodeObject.IsValid() || _edgeObject == null || !_edgeObject.IsValid() )
            return;

        float dt = RealTime.Delta;

        if ( _isDraggingNode && _draggedNodeIndex >= 0 && _draggedNodeIndex < Registry.Count )
        {
            Vector3? worldPlaneHit = CameraController.GetWorldPosOnPlane( GetRay( _lastMousePos ) );
            if ( worldPlaneHit.HasValue )
            {
                ref var draggedSpatial = ref Registry.GetSpatialRef( _draggedNodeIndex );
                Vector3 target = worldPlaneHit.Value - _dragOffset;
                draggedSpatial.Position = new Vector2( target.x, target.y );
                draggedSpatial.Velocity = Vector2.Zero;
            }
        }

        if ( !Physics.IsSleeping && (!Physics.PauseDuringPlay || !Game.IsPlaying) )
        {
            Physics.Step( Registry, Edges, dt, Theme.NodeSizeScale );
            SyncGpuBuffers();
            UpdateFloatingCardPosition();
            Update();
        }

        CameraController.UpdateAnimation( dt );
    }

    public void SyncGpuBuffers()
    {
        int nodeCount = Registry.Count;
        int edgeCount = Edges.Count;

        if ( _nodeTransformsStaging.Length < nodeCount )
        {
            int newCap = Math.Max( _nodeTransformsStaging.Length * 2, nodeCount );
            Array.Resize( ref _nodeTransformsStaging, newCap );
            Array.Resize( ref _nodeColorsStaging, newCap );
        }

        if ( _edgeStaging.Length < edgeCount )
        {
            Array.Resize( ref _edgeStaging, Math.Max( _edgeStaging.Length * 2, edgeCount ) );
        }

        var spatials = Registry.GetReadOnlySpatialSpan();
        bool hasFocus = HoveredNodeIndex >= 0 || SelectedNodeIndex >= 0;

        // 1. Nodes
        for ( int i = 0; i < nodeCount; i++ )
        {
            ref readonly var node = ref spatials[i];
            if ( node.IsHidden )
            {
                _nodeTransformsStaging[i] = new Transform( new Vector3( 0, 0, -999999 ), Rotation.Identity, 0.001f );
                _nodeColorsStaging[i] = Color.Transparent;
                continue;
            }

            Vector3 pos = GetNodeWorldPosition3D( i );
            float baseRadius = MathF.Max( 4.0f, node.Radius * Theme.NodeSizeScale );
            float scale = baseRadius / 10.0f;
            if ( node.IsSelected || node.IsHovered ) scale *= 1.35f;

            bool inFocus = !hasFocus || i == SelectedNodeIndex || i == HoveredNodeIndex || _focusedNeighbors.Contains( i );
            var payload = Registry.GetPayload( i );

            Color col = node.IsSelected ? Theme.SelectionColor :
                        node.IsHovered ? Theme.HoverColor :
                        payload.AccentColor;

            if ( !inFocus ) col = col.WithAlpha( 0.15f );

            _nodeTransformsStaging[i] = new Transform( pos, Rotation.Identity, scale );
            _nodeColorsStaging[i] = col;
        }

        _nodeObject.UpdateNodes( _nodeTransformsStaging.AsSpan( 0, nodeCount ), spatials, _nodeColorsStaging.AsSpan( 0, nodeCount ) );

        // 2. Edges
        int validEdgeCount = 0;
        for ( int i = 0; i < edgeCount; i++ )
        {
            var edge = Edges[i];
            if ( edge.SourceIndex < 0 || edge.SourceIndex >= spatials.Length ||
                 edge.TargetIndex < 0 || edge.TargetIndex >= spatials.Length )
            {
                continue;
            }

            ref readonly var src = ref spatials[edge.SourceIndex];
            ref readonly var dst = ref spatials[edge.TargetIndex];

            if ( src.IsHidden || dst.IsHidden ) continue;

            Vector3 p0 = GetNodeWorldPosition3D( edge.SourceIndex );
            Vector3 p1 = GetNodeWorldPosition3D( edge.TargetIndex );

            bool isEdgeFocused = !hasFocus || (edge.SourceIndex == SelectedNodeIndex || edge.TargetIndex == SelectedNodeIndex ||
                                               edge.SourceIndex == HoveredNodeIndex || edge.TargetIndex == HoveredNodeIndex);

            Color edgeCol = isEdgeFocused
                ? (src.IsSelected || dst.IsSelected ? Theme.SelectionColor : Theme.HoverColor).WithAlpha( 0.85f )
                : (edge.CustomColor ?? Theme.DefaultEdgeColor).WithAlpha( 0.35f );

            _edgeStaging[validEdgeCount++] = (p0, p1, (Color32)edgeCol, edge.Style, edge.FlowSpeed);
        }

        float edgeThickness = MathF.Max( 1.5f, 2.5f * Theme.LinkThicknessScale );
        _edgeObject.UploadEdges( _edgeStaging.AsSpan( 0, validEdgeCount ), edgeThickness );
    }

    private void UpdateFloatingCardPosition()
    {
        if ( !_inspectorOverlay.Visible || SelectedNodeIndex < 0 || SelectedNodeIndex >= Registry.Count || _camera == null )
            return;

        Vector3 worldPos = GetNodeWorldPosition3D( SelectedNodeIndex );
        Vector2 screenNorm = _camera.PointToScreenNormal( worldPos );
        Vector2 screenAnchor = screenNorm * Size;

        Vector2 targetPos = screenAnchor + new Vector2( 24, -30 );

        float pad = 12f;
        float clampedX = Math.Clamp( targetPos.x, pad, MathF.Max( pad, Width - _inspectorOverlay.Width - pad ) );
        float clampedY = Math.Clamp( targetPos.y, pad, MathF.Max( pad, Height - _inspectorOverlay.Height - pad ) );

        _inspectorOverlay.Position = new Vector2( clampedX, clampedY );
    }

    protected override void OnResize()
    {
        base.OnResize();
        UpdateFloatingCardPosition();
        Update();
    }

    protected override void OnPaint()
    {
        // 1. Native GPU Scene rendered directly via Swapchain!
        base.OnPaint();

        // 2. Background Grid Overlay
        if ( Theme.ShowGrid && !CameraController.Is3DMode )
        {
            DrawGrid();
        }

        // 3. Crisp Typography Overlay
        RenderLabels();
    }

    private void DrawGrid()
    {
        if ( _camera == null ) return;

        float zoom = 2500.0f / MathF.Max( 100f, CameraController.OrthoSize );
        float step = Theme.GridStep * zoom;
        if ( step < 12f ) return;

        Vector2 screenCenter = Size * 0.5f;
        Vector2 screenNorm = _camera.PointToScreenNormal( Vector3.Zero );
        Vector2 screenOffset = (screenNorm * Size) - screenCenter;

        float startX = (screenCenter.x + screenOffset.x) % step;
        float startY = (screenCenter.y + screenOffset.y) % step;

        Paint.ClearBrush();
        Paint.SetPen( Theme.GridColor.WithAlpha( 0.05f ), 1f );

        for ( float x = startX; x < Size.x; x += step )
            Paint.DrawLine( new Vector2( x, 0 ), new Vector2( x, Size.y ) );

        for ( float y = startY; y < Size.y; y += step )
            Paint.DrawLine( new Vector2( 0, y ), new Vector2( Size.x, y ) );
    }

    private void RenderLabels()
    {
        if ( _camera == null ) return;

        var spatials = Registry.GetReadOnlySpatialSpan();
        int count = spatials.Length;

        for ( int i = 0; i < count; i++ )
        {
            ref readonly var node = ref spatials[i];
            if ( node.IsHidden ) continue;

            bool isPrimary = i == SelectedNodeIndex || i == HoveredNodeIndex;
            bool isNeighbor = _focusedNeighbors.Contains( i );

            if ( !isPrimary && !isNeighbor ) continue;

            Vector3 worldPos = GetNodeWorldPosition3D( i ) + new Vector3( 0, -node.Radius * Theme.NodeSizeScale - 8f, 0 );

            if ( CameraController.Is3DMode && Vector3.Dot( _cameraObject.WorldRotation.Forward, worldPos - _cameraObject.WorldPosition ) <= 0 )
                continue;

            Vector2 screenPos = _camera.PointToScreenNormal( worldPos ) * Size;

            if ( screenPos.x < -80 || screenPos.x > Size.x + 80 || screenPos.y < -40 || screenPos.y > Size.y + 40 )
                continue;

            var payload = Registry.GetPayload( i );
            int fontSize = isPrimary ? 13 : 11;
            Paint.SetFont( "Segoe UI", fontSize, isPrimary ? 700 : 500 );

            var textRect = new Rect( screenPos.x - 100, screenPos.y, 200, 20 );

            Paint.SetPen( Color.Black.WithAlpha( 0.85f ) );
            Paint.DrawText( new Rect( textRect.Position + new Vector2( 1, 1 ), textRect.Size ), payload.Title, TextFlag.Center );

            Color textColor = isPrimary ? Color.White : Theme.TextColor.WithAlpha( 0.90f );
            Paint.SetPen( textColor );
            Paint.DrawText( textRect, payload.Title, TextFlag.Center );
        }
    }

    protected override void OnMousePress( MouseEvent e )
    {
        _lastMousePos = e.LocalPosition;

        bool isPan = e.MiddleMouseButton || (e.LeftMouseButton && Editor.Application.KeyboardModifiers.HasFlag( Sandbox.KeyboardModifiers.Alt ));
        bool isOrbit = e.RightMouseButton;

        if ( isPan )
        {
            _isPanning = true;
            Cursor = CursorShape.SizeAll;
            e.Accepted = true;
            return;
        }

        if ( isOrbit && CameraController.Is3DMode )
        {
            _isOrbiting = true;
            Cursor = CursorShape.Cross;
            e.Accepted = true;
            return;
        }

        if ( e.LeftMouseButton )
        {
            int targetIdx = PickNodeFromRay( GetRay( e.LocalPosition ) );

            if ( targetIdx >= 0 )
            {
                _draggedNodeIndex = targetIdx;
                Vector3 nodeWorld = GetNodeWorldPosition3D( targetIdx );
                Vector3? planeHit = CameraController.GetWorldPosOnPlane( GetRay( e.LocalPosition ) );
                _dragOffset = (planeHit ?? nodeWorld) - nodeWorld;
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
        Vector2 delta = e.LocalPosition - _lastMousePos;
        _lastMousePos = e.LocalPosition;

        if ( _isPanning )
        {
            CameraController.Pan( delta, Size );
            UpdateFloatingCardPosition();
            Update();
            return;
        }

        if ( _isOrbiting )
        {
            CameraController.Orbit( delta );
            UpdateFloatingCardPosition();
            Update();
            return;
        }

        if ( _draggedNodeIndex >= 0 )
        {
            if ( !_isDraggingNode && (e.LocalPosition - _dragStartMouse).Length >= 5.0f )
            {
                _isDraggingNode = true;
                Registry.GetSpatialRef( _draggedNodeIndex ).SetFlag( NodeFlags.Pinned, true );
                Cursor = CursorShape.DragMove;
            }

            if ( _isDraggingNode )
            {
                Vector3? worldPlaneHit = CameraController.GetWorldPosOnPlane( GetRay( e.LocalPosition ) );
                if ( worldPlaneHit.HasValue )
                {
                    ref var draggedSpatial = ref Registry.GetSpatialRef( _draggedNodeIndex );
                    Vector3 target = worldPlaneHit.Value - _dragOffset;
                    draggedSpatial.Position = new Vector2( target.x, target.y );
                    draggedSpatial.Velocity = Vector2.Zero;
                    Physics.WakeUp();
                    SyncGpuBuffers();
                    UpdateFloatingCardPosition();
                    Update();
                }
                return;
            }
        }

        int hovered = PickNodeFromRay( GetRay( e.LocalPosition ) );
        if ( HoveredNodeIndex != hovered )
        {
            if ( HoveredNodeIndex >= 0 && HoveredNodeIndex < Registry.Count )
                Registry.GetSpatialRef( HoveredNodeIndex ).SetFlag( NodeFlags.Hovered, false );

            HoveredNodeIndex = hovered;

            if ( HoveredNodeIndex >= 0 && HoveredNodeIndex < Registry.Count )
                Registry.GetSpatialRef( HoveredNodeIndex ).SetFlag( NodeFlags.Hovered, true );

            RebuildFocusedNeighbors();
            SyncGpuBuffers();
            Cursor = HoveredNodeIndex >= 0 ? CursorShape.Finger : CursorShape.Arrow;
            Update();
        }
    }

    protected override void OnMouseReleased( MouseEvent e )
    {
        if ( _isPanning || _isOrbiting )
        {
            _isPanning = false;
            _isOrbiting = false;
            Cursor = CursorShape.Arrow;
            UpdateFloatingCardPosition();
            Update();
        }

        if ( _draggedNodeIndex >= 0 )
        {
            bool wasActuallyDragged = _isDraggingNode;

            if ( !_dragNodeWasPinnedOriginally )
            {
                Registry.GetSpatialRef( _draggedNodeIndex ).SetFlag( NodeFlags.Pinned, false );
            }

            _draggedNodeIndex = -1;
            _isDraggingNode = false;
            Cursor = HoveredNodeIndex >= 0 ? CursorShape.Finger : CursorShape.Arrow;

            if ( wasActuallyDragged )
            {
                Physics.WakeUp();
            }

            SyncGpuBuffers();
            Update();
        }
    }

    protected override void OnMouseWheel( WheelEvent e )
    {
        CameraController.Zoom( e.Delta );
        UpdateFloatingCardPosition();
        Update();
        e.Accepted = true;
    }

    protected override void OnDoubleClick( MouseEvent e )
    {
        int idx = PickNodeFromRay( GetRay( e.LocalPosition ) );
        if ( idx >= 0 )
        {
            OnNodeDoubleClicked?.Invoke( idx );
            e.Accepted = true;
        }
    }

    protected override void OnContextMenu( ContextMenuEvent e )
    {
        int idx = PickNodeFromRay( GetRay( e.LocalPosition ) );
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

            menu.AddOption( "Focus Camera", "my_location", () => FocusOnNode( idx, 1500f ) );

            bool isPinned = Registry.GetSpatialRef( idx ).IsPinned;
            menu.AddOption( isPinned ? "Unpin Position" : "Pin in Place 📌", "push_pin", () =>
            {
                Registry.GetSpatialRef( idx ).SetFlag( NodeFlags.Pinned, !isPinned );
                SyncGpuBuffers();
                Update();
            } );

            menu.AddSeparator();
            menu.AddOption( "Copy Type Name", "content_copy", () => EditorUtility.Clipboard.Copy( payload.Title ) );
        }
        else
        {
            menu.AddOption( CameraController.Is3DMode ? "Switch to 2D Ortho" : "Switch to 3D Orbit 🪐", "3d_rotation", () =>
            {
                CameraController.ToggleMode();
                SyncGpuBuffers();
                Update();
            } );
            menu.AddOption( "Fit All to Screen", "fit_screen", FitToScreen );
            menu.AddOption( "Reheat Physics 🔥", "bolt", () => { Physics.WakeUp(); Update(); } );
        }

        menu.OpenAtCursor();
        e.Accepted = true;
    }

    private int PickNodeFromRay( Ray ray )
    {
        int bestIdx = -1;
        float bestDist = float.MaxValue;
        var spatials = Registry.GetReadOnlySpatialSpan();

        for ( int i = 0; i < spatials.Length; i++ )
        {
            ref readonly var node = ref spatials[i];
            if ( node.IsHidden ) continue;

            Vector3 center = GetNodeWorldPosition3D( i );
            float radius = MathF.Max( 4.0f, node.Radius * Theme.NodeSizeScale ) * 1.25f;

            Vector3 m = ray.Position - center;
            float b = Vector3.Dot( m, ray.Forward );
            float c = Vector3.Dot( m, m ) - (radius * radius);

            if ( c > 0.0f && b > 0.0f ) continue;

            float discr = b * b - c;
            if ( discr < 0.0f ) continue;

            float t = -b - MathF.Sqrt( discr );
            if ( t < 0.0f ) t = 0.0f;

            if ( t < bestDist )
            {
                bestDist = t;
                bestIdx = i;
            }
        }

        return bestIdx;
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

        Vector3 center = new( (minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0 );
        float span = MathF.Max( maxX - minX, maxY - minY ) + 400f;

        CameraController.AnimateTo( center, span );
    }

    public void Dispose()
    {
        _edgeObject?.Dispose();
        _nodeObject?.Dispose();
        _cameraObject?.Destroy();
        _scene?.Clear();
    }
}

/// <summary>
/// Floating inspection card anchored to the selected node in world space.
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