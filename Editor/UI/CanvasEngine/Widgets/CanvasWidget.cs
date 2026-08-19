#nullable enable
using System;
using System.Collections.Generic;
using ArchitectureVisualizer.UI.CanvasEngine.Core;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using ArchitectureVisualizer.UI.CanvasEngine.Rendering;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Widgets;

/// <summary>
/// High-performance interactive 2D Canvas Widget for node-graph visualization and editing.
/// </summary>
public class CanvasWidget : Widget
{
    public CanvasTransform Transform { get; } = new();
    public CanvasTheme Theme { get; set; } = CanvasTheme.DefaultDark;
    public SleepyPhysicsSolver Physics { get; } = new();

    public INodeRenderer NodeRenderer { get; set; } = new DefaultNodeRenderer();
    public IEdgeRenderer EdgeRenderer { get; set; } = new DefaultEdgeRenderer();

    public List<CanvasNode> Nodes { get; } = new();
    public List<CanvasEdge> Edges { get; } = new();

    public CanvasNode? SelectedNode { get; private set; }
    public CanvasNode? HoveredNode { get; private set; }

    public event Action<CanvasNode?>? OnNodeSelected;
    public event Action<CanvasNode>? OnNodeDoubleClicked;
    public event Action<CanvasNode, ContextMenuEvent>? OnNodeContextMenu;

    private bool _isPanning;
    private Vector2 _panStartMouse;
    private Vector2 _panStartOffset;

    private CanvasNode? _draggedNode;
    private Vector2 _dragOffset;

    public CanvasWidget( Widget parent ) : base( parent )
    {
        FocusMode = FocusMode.Click;
        Cursor = CursorShape.Arrow;
        AcceptDrops = true;
    }

    /// <summary>
    /// Clears all visual nodes and edges from the canvas.
    /// </summary>
    public void Clear()
    {
        Nodes.Clear();
        Edges.Clear();
        SelectedNode = null;
        HoveredNode = null;
        _draggedNode = null;
        Update();
    }

    /// <summary>
    /// Adds a node and wakes up the physics engine.
    /// </summary>
    public void AddNode( CanvasNode node )
    {
        Nodes.Add( node );
        Physics.WakeUp();
        Update();
    }

    /// <summary>
    /// Adds an edge connection.
    /// </summary>
    public void AddEdge( CanvasEdge edge )
    {
        Edges.Add( edge );
        Physics.WakeUp();
        Update();
    }

    /// <summary>
    /// Finds the topmost CanvasNode under the given world position.
    /// </summary>
    public CanvasNode? FindNodeAt( Vector2 worldPos )
    {
        for ( int i = Nodes.Count - 1; i >= 0; i-- )
        {
            var node = Nodes[i];
            if ( node.GetWorldBounds().IsInside( worldPos ) )
                return node;
        }
        return null;
    }

    /// <summary>
    /// Centers the camera on a specific node.
    /// </summary>
    public void FocusOnNode( CanvasNode node )
    {
        Transform.CenterOn( node.Center );
        Update();
    }

    protected override void OnResize()
    {
        base.OnResize();
        Transform.ViewportSize = Size;
        Physics.WakeUp();
    }

    protected override void OnPaint()
    {
        // 1. Tick Physics Step
        if ( !Physics.IsSleeping )
        {
            Physics.Step( Nodes, Edges );
            // Schedule next repaint until physics goes to sleep
            Update();
        }

        Transform.ViewportSize = Size;
        Rect visibleWorldRect = Transform.GetVisibleWorldRect( margin: 120f );
        PaintContext ctx = new( Transform, Theme, visibleWorldRect )
        {
            HoveredNode = HoveredNode,
            SelectedNode = SelectedNode
        };

        // 2. Draw Background
        Paint.ClearPen();
        Paint.SetBrush( Theme.BackgroundColor );
        Paint.DrawRect( LocalRect );

        // 3. Draw Background Grid
        if ( Theme.ShowGrid )
        {
            DrawGrid();
        }

        // 4. Draw Edges (with Frustum Culling)
        int edgeCount = Edges.Count;
        for ( int i = 0; i < edgeCount; i++ )
        {
            var edge = Edges[i];
            Rect edgeBounds = Rect.FromPoints( edge.Source.Center, edge.Target.Center ).Grow( 40f );
            if ( !Transform.IsWorldRectVisible( edgeBounds, visibleWorldRect ) )
                continue;

            EdgeRenderer.RenderEdge( ctx, edge );
        }

        // 5. Draw Nodes (with Frustum Culling)
        int nodeCount = Nodes.Count;
        for ( int i = 0; i < nodeCount; i++ )
        {
            var node = Nodes[i];
            Rect nodeBounds = node.GetWorldBounds();
            if ( !Transform.IsWorldRectVisible( nodeBounds, visibleWorldRect ) )
                continue;

            NodeRenderer.RenderNode( ctx, node );
        }
    }

    private void DrawGrid()
    {
        float step = Theme.GridStep * Transform.Zoom;
        if ( step < 12f ) return; // Skip tiny grid when zoomed far out

        Vector2 center = Size * 0.5f;
        float startX = (Transform.PanOffset.x + center.x) % step;
        float startY = (Transform.PanOffset.y + center.y) % step;

        Paint.ClearBrush();
        Paint.SetPen( Theme.GridColor, 1f );

        for ( float x = startX; x < Size.x; x += step )
        {
            Paint.DrawLine( new Vector2( x, 0 ), new Vector2( x, Size.y ) );
        }

        for ( float y = startY; y < Size.y; y += step )
        {
            Paint.DrawLine( new Vector2( 0, y ), new Vector2( Size.x, y ) );
        }
    }

    protected override void OnMousePress( MouseEvent e )
    {
        bool isPanButton = e.MiddleMouseButton || e.RightMouseButton;
        bool isAltPan = e.LeftMouseButton && Editor.Application.KeyboardModifiers.HasFlag( Sandbox.KeyboardModifiers.Alt );

        if ( isPanButton || isAltPan )
        {
            _isPanning = true;
            _panStartMouse = e.LocalPosition;
            _panStartOffset = Transform.PanOffset;
            Cursor = CursorShape.SizeAll;
            e.Accepted = true;
            return;
        }

        if ( e.LeftMouseButton )
        {
            Vector2 worldPos = Transform.ScreenToWorld( e.LocalPosition );
            var clickedNode = FindNodeAt( worldPos );

            if ( clickedNode != null )
            {
                _draggedNode = clickedNode;
                _draggedNode.IsDragging = true;
                _dragOffset = worldPos - clickedNode.Position;

                // Update selection
                if ( SelectedNode != clickedNode )
                {
                    if ( SelectedNode != null ) SelectedNode.IsSelected = false;
                    SelectedNode = clickedNode;
                    SelectedNode.IsSelected = true;
                    OnNodeSelected?.Invoke( SelectedNode );
                }

                Physics.WakeUp();
                Cursor = CursorShape.DragMove;
            }
            else
            {
                if ( SelectedNode != null )
                {
                    SelectedNode.IsSelected = false;
                    SelectedNode = null;
                    OnNodeSelected?.Invoke( null );
                }
            }

            Update();
            e.Accepted = true;
        }
    }

    protected override void OnMouseMove( MouseEvent e )
    {
        if ( _isPanning )
        {
            Transform.PanOffset = _panStartOffset + (e.LocalPosition - _panStartMouse);
            Update();
            return;
        }

        Vector2 worldPos = Transform.ScreenToWorld( e.LocalPosition );

        if ( _draggedNode != null )
        {
            _draggedNode.Position = worldPos - _dragOffset;
            Physics.WakeUp();
            Update();
            return;
        }

        // Update Hover State
        var hovered = FindNodeAt( worldPos );
        if ( HoveredNode != hovered )
        {
            if ( HoveredNode != null ) HoveredNode.IsHovered = false;
            HoveredNode = hovered;
            if ( HoveredNode != null ) HoveredNode.IsHovered = true;
            Cursor = HoveredNode != null ? CursorShape.Finger : CursorShape.Arrow;
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

        if ( _draggedNode != null )
        {
            _draggedNode.IsDragging = false;
            _draggedNode = null;
            Cursor = CursorShape.Arrow;
            Update();
        }
    }

    protected override void OnMouseWheel( WheelEvent e )
    {
        float factor = e.Delta > 0 ? 1.15f : 0.85f;
        Transform.ZoomAt( e.Position, factor );
        Update();
        e.Accepted = true;
    }

    protected override void OnDoubleClick( MouseEvent e )
    {
        Vector2 worldPos = Transform.ScreenToWorld( e.LocalPosition );
        var node = FindNodeAt( worldPos );

        if ( node != null )
        {
            OnNodeDoubleClicked?.Invoke( node );
            e.Accepted = true;
        }
    }

    protected override void OnContextMenu( ContextMenuEvent e )
    {
        Vector2 worldPos = Transform.ScreenToWorld( e.LocalPosition );
        var node = FindNodeAt( worldPos );

        if ( node != null && OnNodeContextMenu != null )
        {
            OnNodeContextMenu.Invoke( node, e );
            return;
        }

        var menu = new Menu( this );

        if ( node != null )
        {
            menu.AddHeading( node.Title );
            menu.AddOption( node.IsPinned ? "Unpin Position" : "Pin in Place 📌", "push_pin", () =>
            {
                node.IsPinned = !node.IsPinned;
                Update();
            } );
            menu.AddOption( "Center View Here", "my_location", () => FocusOnNode( node ) );
        }
        else
        {
            menu.AddOption( "Reset View", "center_focus_strong", () =>
            {
                Transform.PanOffset = Vector2.Zero;
                Transform.Zoom = 1.0f;
                Update();
            } );
            menu.AddOption( "Wake Up Physics", "bolt", () =>
            {
                Physics.WakeUp();
                Update();
            } );
        }

        menu.OpenAtCursor();
        e.Accepted = true;
    }
}