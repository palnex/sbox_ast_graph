#nullable enable
using System;
using System.Collections.Generic;
using ArchitectureVisualizer.UI.CanvasEngine.API;
using ArchitectureVisualizer.UI.CanvasEngine.Core;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using ArchitectureVisualizer.UI.CanvasEngine.Rendering;
using ArchitectureVisualizer.UI.Floating;
using ArchitectureVisualizer.UI.Interaction;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI;

/// <summary>
/// High-Performance Zero-Copy Graph Canvas Widget rendering directly to Native GPU Swapchain.
/// Coordinates rendering systems, physics, and user interaction.
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
    private readonly CanvasAdjacencyIndex _adjacencyIndex = new();
    private readonly CanvasInteractionHandler _interaction;
    private readonly FloatingInspectorOverlay _inspectorOverlay;

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

    public CanvasWidget( Widget parent ) : base( parent )
    {
        FocusMode = FocusMode.Click;
        Cursor = CursorShape.Arrow;
        AcceptDrops = true;
        MouseTracking = true;

        InitScene();

        _interaction = new CanvasInteractionHandler( this );
        _inspectorOverlay = new FloatingInspectorOverlay( this );
        _inspectorOverlay.Visible = false;
        _inspectorOverlay.OnNavigateRequested += targetId => FocusNode( targetId, 1500f );
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
        _adjacencyIndex.Build( Registry.Count, Edges );

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
            _adjacencyIndex.Build( Registry.Count, Edges );
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
            _adjacencyIndex.Build( Registry.Count, Edges );
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
            _adjacencyIndex.Build( Registry.Count, Edges );
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
        else
        {
            for ( int i = 0; i < Registry.Count; i++ )
            {
                if ( Registry.GetPayload( i ).Id == id )
                {
                    FocusOnNode( i, targetZoom );
                    break;
                }
            }
        }
    }

    public void Clear()
    {
        Registry.Clear();
        Edges.Clear();
        _idToIndexMap.Clear();
        _adjacencyIndex.Clear();
        SelectedNodeIndex = -1;
        HoveredNodeIndex = -1;
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

            // Instantly position next to node
            UpdateFloatingCardPosition();
            _inspectorOverlay.Visible = true;
            OnNodeClicked?.Invoke( payload.Id );
        }
        else
        {
            _inspectorOverlay.Visible = false;
        }

        _nodeObject.MarkTextureDirty();
        SyncGpuBuffers();
        OnNodeSelected?.Invoke( SelectedNodeIndex );
        Update();
    }

    public void SetHoveredNode( int nodeIndex )
    {
        if ( HoveredNodeIndex >= 0 && HoveredNodeIndex < Registry.Count )
        {
            Registry.GetSpatialRef( HoveredNodeIndex ).SetFlag( NodeFlags.Hovered, false );
            OnNodeHoverChanged?.Invoke( Registry.GetPayload( HoveredNodeIndex ).Id, false );
        }

        HoveredNodeIndex = nodeIndex;

        if ( HoveredNodeIndex >= 0 && HoveredNodeIndex < Registry.Count )
        {
            Registry.GetSpatialRef( HoveredNodeIndex ).SetFlag( NodeFlags.Hovered, true );
            OnNodeHoverChanged?.Invoke( Registry.GetPayload( HoveredNodeIndex ).Id, true );
        }

        _nodeObject.MarkTextureDirty();
        SyncGpuBuffers();
        Cursor = HoveredNodeIndex >= 0 ? CursorShape.Finger : CursorShape.Arrow;
        Update();
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

        // 1. Advance camera interpolation
        CameraController.UpdateAnimation( dt );

        // 2. Physics simulation step
        bool isPhysicsActive = !Physics.IsSleeping && (!Physics.PauseDuringPlay || !Game.IsPlaying);
        if ( isPhysicsActive )
        {
            Physics.Step( Registry, Edges, dt, Theme.NodeSizeScale );
        }

        // 3. Sync GPU buffers ONLY when physics is moving OR camera is actively flying
        if ( isPhysicsActive || CameraController.IsAnimating )
        {
            SyncGpuBuffers();
        }

        // 4. Update GPU edge animation clock
        _edgeObject.UpdateTimeUniform();

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
        int active = HoveredNodeIndex >= 0 ? HoveredNodeIndex : SelectedNodeIndex;
        var focusedNeighbors = _adjacencyIndex.GetFocusedNeighbors( active );

        // 1. GPU Instanced Nodes
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

            bool inFocus = !hasFocus || i == SelectedNodeIndex || i == HoveredNodeIndex || focusedNeighbors.Contains( i );
            var payload = Registry.GetPayload( i );

            Color col = node.IsSelected ? Theme.SelectionColor :
                        node.IsHovered ? Theme.HoverColor :
                        payload.AccentColor;

            if ( !inFocus ) col = col.WithAlpha( 0.15f );

            _nodeTransformsStaging[i] = new Transform( pos, Rotation.Identity, scale );
            _nodeColorsStaging[i] = col;
        }

        _nodeObject.UpdateNodes( _nodeTransformsStaging.AsSpan( 0, nodeCount ), spatials, _nodeColorsStaging.AsSpan( 0, nodeCount ) );

        // 2. GPU Ribbon Edges
        _edgeObject.UpdateEdges( Registry, Edges, Theme, SelectedNodeIndex, HoveredNodeIndex, CameraController.Is3DMode );

        // 3. Multi-Script Unicode Text Pipeline
        _textPipeline.UpdateLabels( _camera, Registry, Theme, Size, SelectedNodeIndex, HoveredNodeIndex, focusedNeighbors, CameraController.Is3DMode );
    }

    public void UpdateFloatingCardPosition()
    {
        if ( !_inspectorOverlay.Visible || SelectedNodeIndex < 0 || SelectedNodeIndex >= Registry.Count || _camera == null )
            return;

        Vector3 worldPos = GetNodeWorldPosition3D( SelectedNodeIndex );
        ref readonly var spatial = ref Registry.GetSpatialRef( SelectedNodeIndex );
        float worldRadius = MathF.Max( 6.0f, spatial.Radius * Theme.NodeSizeScale );

        var layout = Interaction.NodeScreenProjection.CalculateAnchorLayout(
            camera: _camera,
            widgetSize: Size,
            worldPos: worldPos,
            worldRadius: worldRadius,
            cardSize: _inspectorOverlay.Size,
            padding: 16f,
            nodeGap: 16f
        );

        if ( !layout.IsVisible )
        {
            _inspectorOverlay.Visible = false;
            return;
        }

        _inspectorOverlay.Visible = true;
        _inspectorOverlay.Position = layout.CardRect.Position;
    }

    public void RebuildAdjacency() => _adjacencyIndex.Build( Registry.Count, Edges );

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
        if ( Theme.ShowGrid && !CameraController.Is3DMode ) DrawGrid();
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

    protected override void OnMousePress( MouseEvent e ) => _interaction.HandleMousePress( e );
    protected override void OnMouseMove( MouseEvent e ) => _interaction.HandleMouseMove( e );
    protected override void OnMouseReleased( MouseEvent e ) => _interaction.HandleMouseReleased( e );
    protected override void OnMouseWheel( WheelEvent e ) => _interaction.HandleWheel( e );

    protected override void OnDoubleClick( MouseEvent e )
    {
        int idx = _interaction.PickNodeFromRay( GetRay( e.LocalPosition ) );
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
        int idx = _interaction.PickNodeFromRay( GetRay( e.LocalPosition ) );
        CanvasContextMenu.Open( this, idx );
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