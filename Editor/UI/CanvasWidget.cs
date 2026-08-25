#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using ArchitectureVisualizer.UI.CanvasEngine.API;
using ArchitectureVisualizer.UI.CanvasEngine.Core;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using ArchitectureVisualizer.UI.CanvasEngine.Rendering;
using ArchitectureVisualizer.UI.Floating;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI;

/// <summary>
/// High-Performance Zero-Copy Graph Canvas Widget rendering directly to Native GPU Swapchain via SceneRenderingWidget.
/// Implements the public ICanvasGraph API for extensible graph orchestration.
/// </summary>
public class CanvasWidget : SceneRenderingWidget, ICanvasGraph, IDisposable
{
    private Scene _scene = null!;
    private SceneWorld _sceneWorld = null!;
    private GameObject _cameraObject = null!;
    private CameraComponent _camera = null!;
    public GraphCameraController CameraController { get; private set; } = null!;

    private GraphNodeSceneObject _nodeObject = null!;
    private GraphEdgeSceneObject _edgeObject = null!;
    private DynamicUnicodeAtlas _dynamicAtlas = null!;
    private GpuComputeTextPipeline _textPipeline = null!;

    private Transform[] _nodeTransformsStaging = new Transform[2048];
    private Color[] _nodeColorsStaging = new Color[2048];

    private readonly Dictionary<string, int> _idToIndexMap = new( StringComparer.Ordinal );
    private bool _isBatching = false;

    public CanvasTheme Theme { get; set; } = CanvasTheme.DefaultDark;
    public SpatialRegistry Registry { get; } = new();
    public SleepyPhysicsSolver Physics { get; } = new();
    public List<CanvasEdge> Edges { get; } = new();

    public int SelectedNodeIndex { get; private set; } = -1;
    public int HoveredNodeIndex { get; private set; } = -1;

    public int NodeCount => Registry.Count;
    public int EdgeCount => Edges.Count;

    // Public API Events
    public event Action<string>? OnNodeClicked;
    public event Action<string>? OnNodeIdDoubleClicked;
    public event Action<string, bool>? OnNodeHoverChanged;
    public event Action<string, string>? OnEdgeClicked { add { } remove { } }
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
            if ( _idToIndexMap.TryGetValue( targetId, out int idx ) )
            {
                FocusOnNode( idx, targetSize: 1500f );
            }
            else
            {
                for ( int i = 0; i < Registry.Count; i++ )
                {
                    if ( Registry.GetPayload( i ).Id == targetId )
                    {
                        FocusOnNode( i, targetSize: 1500f );
                        break;
                    }
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
        _dynamicAtlas = new DynamicUnicodeAtlas();
        _textPipeline = new GpuComputeTextPipeline( _sceneWorld, _dynamicAtlas );
    }

    public void MarkNodesDirty() => _nodeObject?.MarkTextureDirty();

    // ==========================================
    // ICanvasGraph IMPLEMENTATION
    // ==========================================

    public NodeBuilder AddNode( string id, string title, string? subtitle = null )
    {
        if ( _idToIndexMap.TryGetValue( id, out int existingIndex ) )
        {
            var p = Registry.GetPayload( existingIndex );
            p.Title = title;
            p.Subtitle = subtitle ?? p.Subtitle;
            _textPipeline?.InvalidateCache();
            return new NodeBuilder( this, existingIndex, id );
        }

        var spatial = new NodeSpatialData
        {
            Position = Vector2.Zero,
            Velocity = Vector2.Zero,
            Radius = 10f,
            ZLevel = 0,
            Shape = NodeShape.Circle,
            Flags = NodeFlags.None
        };

        var payload = new NodePayload
        {
            Id = id,
            Title = title,
            Subtitle = subtitle ?? "",
            AccentColor = Color.White,
            UserData = null
        };

        int newIndex = Registry.Allocate( in spatial, payload );
        _idToIndexMap[id] = newIndex;

        _textPipeline?.InvalidateCache();
        MarkNodesDirty();

        if ( !_isBatching )
        {
            SyncGpuBuffers();
            Update();
        }

        return new NodeBuilder( this, newIndex, id );
    }

    public bool HasNode( string id ) => _idToIndexMap.ContainsKey( id );

    public bool RemoveNode( string id )
    {
        if ( !_idToIndexMap.TryGetValue( id, out int nodeIndex ) )
            return false;

        Registry.GetSpatialRef( nodeIndex ).SetFlag( NodeFlags.Hidden, true );
        _idToIndexMap.Remove( id );

        Edges.RemoveAll( e => e.SourceIndex == nodeIndex || e.TargetIndex == nodeIndex );

        _textPipeline?.InvalidateCache();
        MarkNodesDirty();

        if ( !_isBatching )
        {
            SyncGpuBuffers();
            Update();
        }

        return true;
    }

    public EdgeBuilder Connect( string sourceId, string targetId )
    {
        if ( !_idToIndexMap.TryGetValue( sourceId, out int srcIdx ) ||
             !_idToIndexMap.TryGetValue( targetId, out int dstIdx ) )
        {
            return new EdgeBuilder( this, -1 );
        }

        var edge = new CanvasEdge( srcIdx, dstIdx )
        {
            Style = EdgeStyle.Solid,
            FlowSpeed = 1.0f,
            CustomColor = Theme.DefaultEdgeColor,
            DesiredSpringLength = 160f
        };

        Edges.Add( edge );
        int edgeIndex = Edges.Count - 1;

        if ( !_isBatching )
        {
            SyncGpuBuffers();
            Update();
        }

        return new EdgeBuilder( this, edgeIndex );
    }

    public bool Disconnect( string sourceId, string targetId )
    {
        if ( !_idToIndexMap.TryGetValue( sourceId, out int srcIdx ) ||
             !_idToIndexMap.TryGetValue( targetId, out int dstIdx ) )
        {
            return false;
        }

        int removed = Edges.RemoveAll( e => e.SourceIndex == srcIdx && e.TargetIndex == dstIdx );
        if ( removed > 0 && !_isBatching )
        {
            SyncGpuBuffers();
            Update();
        }

        return removed > 0;
    }

    public void BatchUpdate( Action<ICanvasGraph> updateAction )
    {
        _isBatching = true;
        try
        {
            updateAction( this );
        }
        finally
        {
            _isBatching = false;
            SyncGpuBuffers();
            Physics.WakeUp();
            Update();
        }
    }

    public void LoadFromProvider( IGraphDataProvider provider )
    {
        Clear();
        BatchUpdate( graph => provider.Populate( graph ) );
    }

    public void PulseEdge( string sourceId, string targetId, Color? pulseColor = null, float speed = 2.0f )
    {
        if ( !_idToIndexMap.TryGetValue( sourceId, out int srcIdx ) ||
             !_idToIndexMap.TryGetValue( targetId, out int dstIdx ) )
        {
            return;
        }

        for ( int i = 0; i < Edges.Count; i++ )
        {
            var e = Edges[i];
            if ( e.SourceIndex == srcIdx && e.TargetIndex == dstIdx )
            {
                e.Style = EdgeStyle.LaserPulse;
                e.FlowSpeed = speed;
                if ( pulseColor.HasValue ) e.CustomColor = pulseColor.Value;
                break;
            }
        }

        SyncGpuBuffers();
        Update();
    }

    public void FlashNode( string id, Color flashColor, float duration = 0.5f )
    {
        if ( !_idToIndexMap.TryGetValue( id, out int nodeIndex ) ) return;
        Registry.GetPayload( nodeIndex ).AccentColor = flashColor;
        MarkNodesDirty();
        SyncGpuBuffers();
        Update();
    }

    public void FocusNode( string id, float targetZoom = 1500f )
    {
        if ( _idToIndexMap.TryGetValue( id, out int nodeIndex ) )
        {
            FocusOnNode( nodeIndex, targetZoom );
        }
    }

    public void Clear()
    {
        Registry.Clear();
        Edges.Clear();
        _idToIndexMap.Clear();
        SelectedNodeIndex = -1;
        HoveredNodeIndex = -1;
        _draggedNodeIndex = -1;
        _focusedNeighbors.Clear();
        _inspectorOverlay.Visible = false;

        _textPipeline?.InvalidateCache();
        _nodeObject?.MarkTextureDirty();
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
            _inspectorOverlay.Bind( payload );
            _inspectorOverlay.Visible = true;
            OnNodeClicked?.Invoke( payload.Id );
        }
        else
        {
            _inspectorOverlay.Visible = false;
        }

        _nodeObject.MarkTextureDirty();
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

        // 1. Dragging Node
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

        // 2. Physics step
        bool isPhysicsActive = !Physics.IsSleeping && (!Physics.PauseDuringPlay || !Game.IsPlaying);
        if ( isPhysicsActive )
        {
            Physics.Step( Registry, Edges, dt, Theme.NodeSizeScale );
            SyncGpuBuffers();
            UpdateFloatingCardPosition();
        }

        // 3. Keep edge shader real-time clock alive
        _edgeObject.UpdateTimeUniform();

        // 4. Update camera animations
        CameraController.UpdateAnimation( dt );

        Update();
    }

    public void SyncGpuBuffers()
    {
        if ( _isBatching ) return;

        int nodeCount = Registry.Count;

        if ( _nodeTransformsStaging.Length < nodeCount )
        {
            Array.Resize( ref _nodeTransformsStaging, Math.Max( _nodeTransformsStaging.Length * 2, nodeCount ) );
            Array.Resize( ref _nodeColorsStaging, Math.Max( _nodeColorsStaging.Length * 2, nodeCount ) );
        }

        var spatials = Registry.GetReadOnlySpatialSpan();
        bool hasFocus = HoveredNodeIndex >= 0 || SelectedNodeIndex >= 0;

        // 1. GPU Instanced Quads for Nodes
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

        // 2. Direct Single-Pass Ribbon Edges
        _edgeObject.UpdateEdges( Registry, Edges, Theme, SelectedNodeIndex, HoveredNodeIndex, CameraController.Is3DMode );

        // 3. GPU Multi-Script Text Label Pipeline
        _textPipeline.UpdateLabels( _camera, Registry, Theme, Size, SelectedNodeIndex, HoveredNodeIndex, _focusedNeighbors, CameraController.Is3DMode );
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
        SyncGpuBuffers();
        UpdateFloatingCardPosition();
        Update();
    }

    protected override void OnPaint()
    {
        base.OnPaint();

        if ( Theme.ShowGrid && !CameraController.Is3DMode )
        {
            DrawGrid();
        }
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
            SyncGpuBuffers();
            UpdateFloatingCardPosition();
            Update();
            return;
        }

        if ( _isOrbiting )
        {
            CameraController.Orbit( delta );
            SyncGpuBuffers();
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
            {
                Registry.GetSpatialRef( HoveredNodeIndex ).SetFlag( NodeFlags.Hovered, false );
                OnNodeHoverChanged?.Invoke( Registry.GetPayload( HoveredNodeIndex ).Id, false );
            }

            HoveredNodeIndex = hovered;

            if ( HoveredNodeIndex >= 0 && HoveredNodeIndex < Registry.Count )
            {
                Registry.GetSpatialRef( HoveredNodeIndex ).SetFlag( NodeFlags.Hovered, true );
                OnNodeHoverChanged?.Invoke( Registry.GetPayload( HoveredNodeIndex ).Id, true );
            }

            _nodeObject.MarkTextureDirty();
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
        SyncGpuBuffers();
        UpdateFloatingCardPosition();
        Update();
        e.Accepted = true;
    }

    protected override void OnDoubleClick( MouseEvent e )
    {
        int idx = PickNodeFromRay( GetRay( e.LocalPosition ) );
        if ( idx >= 0 )
        {
            string id = Registry.GetPayload( idx ).Id;
            OnNodeDoubleClicked?.Invoke( idx );
            OnNodeIdDoubleClicked?.Invoke( id );
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
        _textPipeline?.Dispose();
        _edgeObject?.Dispose();
        _nodeObject?.Dispose();
        _cameraObject?.Destroy();
        _scene?.Clear();
    }
}