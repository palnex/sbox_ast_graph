#nullable enable
using System;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.Interaction;

/// <summary>
/// Encapsulates user input, camera manipulation, ray-picking, and node dragging.
/// </summary>
public sealed class CanvasInteractionHandler
{
    private readonly CanvasWidget _canvas;

    private bool _isPanning;
    private bool _isOrbiting;
    private Vector2 _lastMousePos;

    private int _draggedNodeIndex = -1;
    private Vector3 _dragOffset;
    private Vector2 _dragStartMouse;
    private bool _isDraggingNode;
    private bool _dragNodeWasPinnedOriginally;

    public CanvasInteractionHandler(CanvasWidget canvas)
    {
        _canvas = canvas;
    }

    public void HandleMousePress(MouseEvent e)
    {
        _lastMousePos = e.LocalPosition;

        bool isPan = e.MiddleMouseButton || (e.LeftMouseButton && Editor.Application.KeyboardModifiers.HasFlag(Sandbox.KeyboardModifiers.Alt));
        bool isOrbit = e.RightMouseButton;

        if (isPan)
        {
            _isPanning = true;
            _canvas.Cursor = CursorShape.SizeAll;
            e.Accepted = true;
            return;
        }

        if (isOrbit && _canvas.CameraController.Is3DMode)
        {
            _isOrbiting = true;
            _canvas.Cursor = CursorShape.Cross;
            e.Accepted = true;
            return;
        }

        if (e.LeftMouseButton)
        {
            int targetIdx = PickNodeFromRay(_canvas.GetRay(e.LocalPosition));

            if (targetIdx >= 0)
            {
                _draggedNodeIndex = targetIdx;
                Vector3 nodeWorld = _canvas.GetNodeWorldPosition3D(targetIdx);
                Vector3? planeHit = _canvas.CameraController.GetWorldPosOnPlane(_canvas.GetRay(e.LocalPosition));
                _dragOffset = (planeHit ?? nodeWorld) - nodeWorld;
                _dragStartMouse = e.LocalPosition;
                _isDraggingNode = false;
                _dragNodeWasPinnedOriginally = _canvas.Registry.GetSpatialRef(targetIdx).IsPinned;

                _canvas.SelectNode(targetIdx);
            }
            else
            {
                _canvas.SelectNode(-1);
            }

            e.Accepted = true;
        }
    }

    public void HandleMouseMove(MouseEvent e)
    {
        Vector2 delta = e.LocalPosition - _lastMousePos;
        _lastMousePos = e.LocalPosition;

        if (_isPanning)
        {
            _canvas.CameraController.Pan(delta, _canvas.Size);
            _canvas.SyncGpuBuffers();
            _canvas.UpdateFloatingCardPosition();
            _canvas.Update();
            return;
        }

        if (_isOrbiting)
        {
            _canvas.CameraController.Orbit(delta);
            _canvas.SyncGpuBuffers();
            _canvas.UpdateFloatingCardPosition();
            _canvas.Update();
            return;
        }

        if (_draggedNodeIndex >= 0)
        {
            if (!_isDraggingNode && (e.LocalPosition - _dragStartMouse).Length >= 5.0f)
            {
                _isDraggingNode = true;
                _canvas.Registry.GetSpatialRef(_draggedNodeIndex).SetFlag(NodeFlags.Pinned, true);
                _canvas.Cursor = CursorShape.DragMove;
            }

            if (_isDraggingNode)
            {
                Vector3? worldPlaneHit = _canvas.CameraController.GetWorldPosOnPlane(_canvas.GetRay(e.LocalPosition));
                if (worldPlaneHit.HasValue)
                {
                    ref var draggedSpatial = ref _canvas.Registry.GetSpatialRef(_draggedNodeIndex);
                    Vector3 target = worldPlaneHit.Value - _dragOffset;
                    draggedSpatial.Position = new Vector2(target.x, target.y);
                    draggedSpatial.Velocity = Vector2.Zero;
                    _canvas.Physics.WakeUp();
                    _canvas.SyncGpuBuffers();
                    _canvas.UpdateFloatingCardPosition();
                    _canvas.Update();
                }
                return;
            }
        }

        int hovered = PickNodeFromRay(_canvas.GetRay(e.LocalPosition));
        if (_canvas.HoveredNodeIndex != hovered)
        {
            _canvas.SetHoveredNode(hovered);
        }
    }

    public void HandleMouseReleased(MouseEvent e)
    {
        if (_isPanning || _isOrbiting)
        {
            _isPanning = false;
            _isOrbiting = false;
            _canvas.Cursor = CursorShape.Arrow;
            _canvas.UpdateFloatingCardPosition();
            _canvas.Update();
        }

        if (_draggedNodeIndex >= 0)
        {
            bool wasActuallyDragged = _isDraggingNode;

            if (!_dragNodeWasPinnedOriginally)
            {
                _canvas.Registry.GetSpatialRef(_draggedNodeIndex).SetFlag(NodeFlags.Pinned, false);
            }

            _draggedNodeIndex = -1;
            _isDraggingNode = false;
            _canvas.Cursor = _canvas.HoveredNodeIndex >= 0 ? CursorShape.Finger : CursorShape.Arrow;

            if (wasActuallyDragged)
            {
                _canvas.Physics.WakeUp();
            }

            _canvas.SyncGpuBuffers();
            _canvas.Update();
        }
    }

    public void HandleWheel(WheelEvent e)
    {
        _canvas.CameraController.Zoom(e.Delta);
        _canvas.SyncGpuBuffers();
        _canvas.UpdateFloatingCardPosition();
        _canvas.Update();
        e.Accepted = true;
    }

    public int PickNodeFromRay(Ray ray)
    {
        int bestIdx = -1;
        float bestDist = float.MaxValue;
        var spatials = _canvas.Registry.GetReadOnlySpatialSpan();

        for (int i = 0; i < spatials.Length; i++)
        {
            ref readonly var node = ref spatials[i];
            if (node.IsHidden) continue;

            Vector3 center = _canvas.GetNodeWorldPosition3D(i);
            float radius = MathF.Max(4.0f, node.Radius * _canvas.Theme.NodeSizeScale) * 1.25f;

            Vector3 m = ray.Position - center;
            float b = Vector3.Dot(m, ray.Forward);
            float c = Vector3.Dot(m, m) - (radius * radius);

            if (c > 0.0f && b > 0.0f) continue;

            float discr = b * b - c;
            if (discr < 0.0f) continue;

            float t = -b - MathF.Sqrt(discr);
            if (t < 0.0f) t = 0.0f;

            if (t < bestDist)
            {
                bestDist = t;
                bestIdx = i;
            }
        }

        return bestIdx;
    }
}