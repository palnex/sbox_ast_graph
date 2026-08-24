#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ArchitectureVisualizer.UI.CanvasEngine.Core;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;
using Sandbox.Rendering;

namespace ArchitectureVisualizer.UI.CanvasEngine.Rendering;

[StructLayout( LayoutKind.Sequential, Pack = 16 )]
public struct NodeLabelGpuData
{
    public Vector3 WorldPosition;  // 12 bytes
    public float NodeRadiusPx;     // 4 bytes
    public Vector4 ScreenRect;     // 16 bytes (Width, Height, OffsetX, OffsetY)
    public Vector4 AtlasUv;        // 16 bytes (uMin, vMin, uMax, vMax)
    public Color Color;            // 16 bytes (R, G, B, A)
}

public sealed class GpuComputeTextPipeline : SceneCustomObject, IDisposable
{
    private readonly DynamicUnicodeAtlas _atlas;
    private readonly Model _unitQuadModel;
    private readonly RenderAttributes _attributes = new();

    private GpuBuffer<NodeLabelGpuData>? _instanceBuffer;
    private int _allocatedCapacity = 0;
    private int _activeNodeCount = 0;

    private LabelAtlasMetadata[] _cachedMetadata = Array.Empty<LabelAtlasMetadata>();
    private bool _isCacheValid = false;

    public void InvalidateCache() => _isCacheValid = false;
    private NodeLabelGpuData[] _stagingNodes = new NodeLabelGpuData[4096];

    public GpuComputeTextPipeline( SceneWorld world, DynamicUnicodeAtlas atlas, int initialCapacity = 4096 ) : base( world )
    {
        _atlas = atlas;
        _unitQuadModel = CreateUnitQuadModel();

        EnsureBufferCapacity( initialCapacity );

        Flags.CastShadows = false;
        Bounds = new BBox( new Vector3( -500000, -500000, -500000 ), new Vector3( 500000, 500000, 500000 ) );
        RenderingEnabled = true;
    }

    private static Model CreateUnitQuadModel()
    {
        var material = Material.Load( "materials/fonts/msdf_text.vmat" )
                       ?? Material.FromShader( "shaders/msdf_text.shader" );
        var mesh = new Mesh( material );

        // Standard Top-Anchored Unit Quad: x in [-0.5, 0.5], y in [0.0, -1.0]
        var vertices = new Vertex[]
        {
            new() { Position = new Vector3( -0.5f,  0.0f, 0 ), TexCoord0 = new Vector4( 0, 0, 0, 0 ) }, // Top-Left
            new() { Position = new Vector3(  0.5f,  0.0f, 0 ), TexCoord0 = new Vector4( 1, 0, 0, 0 ) }, // Top-Right
            new() { Position = new Vector3(  0.5f, -1.0f, 0 ), TexCoord0 = new Vector4( 1, 1, 0, 0 ) }, // Bottom-Right
            new() { Position = new Vector3( -0.5f, -1.0f, 0 ), TexCoord0 = new Vector4( 0, 1, 0, 0 ) }  // Bottom-Left
        };

        var indices = new int[] { 0, 1, 2, 0, 2, 3 };

        mesh.CreateVertexBuffer( vertices.Length, vertices );
        mesh.CreateIndexBuffer( indices.Length, indices );
        mesh.Bounds = new BBox( new Vector3( -500, -500, -50 ), new Vector3( 500, 500, 50 ) );

        var builder = new ModelBuilder();
        builder.AddMesh( mesh );
        return builder.Create();
    }

    private void EnsureBufferCapacity( int requiredCapacity )
    {
        if ( _allocatedCapacity >= requiredCapacity && _instanceBuffer != null )
            return;

        int newCap = Math.Max( _allocatedCapacity == 0 ? 4096 : _allocatedCapacity * 2, requiredCapacity );

        _instanceBuffer?.Dispose();
        _instanceBuffer = new GpuBuffer<NodeLabelGpuData>( newCap, GpuBuffer.UsageFlags.Structured );
        _allocatedCapacity = newCap;

        if ( _stagingNodes.Length < newCap )
            Array.Resize( ref _stagingNodes, newCap );
    }

    public void UpdateLabels(
        CameraComponent camera,
        SpatialRegistry registry,
        CanvasTheme theme,
        Vector2 viewportSize,
        int selectedIndex,
        int hoveredIndex,
        HashSet<int> focusedNeighbors,
        bool is3DMode )
    {
        int nodeCount = registry.Count;
        if ( nodeCount == 0 || camera == null )
        {
            _activeNodeCount = 0;
            return;
        }

        // 1. One-time bake cache on graph load / rebuild
        if ( !_isCacheValid || _cachedMetadata.Length != nodeCount )
        {
            _cachedMetadata = new LabelAtlasMetadata[nodeCount];
            for ( int i = 0; i < nodeCount; i++ )
            {
                var payload = registry.GetPayload( i );
                string title = !string.IsNullOrEmpty( payload.Title ) ? payload.Title : "Node";
                _cachedMetadata[i] = _atlas.GetOrCreateLabel( title );
            }
            _atlas.FlushIfDirty();
            _isCacheValid = true;
        }

        EnsureBufferCapacity( nodeCount );
        _activeNodeCount = nodeCount;

        var spatials = registry.GetReadOnlySpatialSpan();
        bool hasFocus = hoveredIndex >= 0 || selectedIndex >= 0;

        Vector3 camPos = camera.WorldPosition;
        float nearFadeStart = is3DMode ? 1400f : 2400f;
        float farFadeEnd = is3DMode ? 4000f : 7500f;

        for ( int i = 0; i < nodeCount; i++ )
        {
            ref readonly var spatial = ref spatials[i];
            if ( spatial.IsHidden )
            {
                _stagingNodes[i] = new NodeLabelGpuData { ScreenRect = Vector4.Zero };
                continue;
            }

            var meta = _cachedMetadata[i];
            float z = is3DMode ? (spatial.ZLevel * 45.0f) : 0f;
            Vector3 worldPos = new( spatial.Position.x, spatial.Position.y, z );

            bool isDirectFocus = (i == selectedIndex) || (i == hoveredIndex);
            bool isNeighbor = focusedNeighbors.Contains( i );
            bool isHighPriority = spatial.Radius >= 12f;

            // Mode 1: HoverOnly
            if ( theme.LabelMode == TextLabelMode.HoverOnly && !isDirectFocus && !isNeighbor )
            {
                _stagingNodes[i] = new NodeLabelGpuData { ScreenRect = Vector4.Zero };
                continue;
            }

            // Mode 2: SmartLOD Distance Fade
            float dist = Vector3.DistanceBetween( worldPos, camPos );
            float alpha = 1.0f;

            if ( theme.LabelMode == TextLabelMode.SmartLOD )
            {
                if ( dist > farFadeEnd && !isDirectFocus && !isHighPriority )
                {
                    _stagingNodes[i] = new NodeLabelGpuData { ScreenRect = Vector4.Zero };
                    continue;
                }

                if ( dist > nearFadeStart && !isDirectFocus && !isHighPriority )
                {
                    alpha = 1.0f - Math.Clamp( (dist - nearFadeStart) / (farFadeEnd - nearFadeStart), 0f, 1f );
                }
            }

            if ( hasFocus && !isDirectFocus && !isNeighbor )
            {
                alpha *= 0.20f;
            }

            if ( alpha <= 0.01f )
            {
                _stagingNodes[i] = new NodeLabelGpuData { ScreenRect = Vector4.Zero };
                continue;
            }

            // Crisp, readable label scaling
            float baseRadius = MathF.Max( 6.0f, spatial.Radius * theme.NodeSizeScale );
            float labelScale = MathF.Max( 0.45f, baseRadius * 0.040f ) * theme.TextSizeScale;
            float worldWidth = MathF.Max( 30.0f, meta.PixelSize.x * labelScale );
            float worldHeight = MathF.Max( 12.0f, meta.PixelSize.y * labelScale );

            _stagingNodes[i] = new NodeLabelGpuData
            {
                WorldPosition = worldPos,
                NodeRadiusPx = baseRadius,
                ScreenRect = new Vector4( worldWidth, worldHeight, 0f, 0f ),
                AtlasUv = meta.UvBounds,
                Color = new Color( 1f, 1f, 1f, alpha )
            };
        }

        _instanceBuffer!.SetData( _stagingNodes.AsSpan( 0, nodeCount ) );

        // 2. Bind Explicit Widget Camera Matrices & Basis Vectors
        float aspect = MathF.Max( 0.01f, viewportSize.x / MathF.Max( 1f, viewportSize.y ) );
        Matrix viewProj = WidgetCameraHelper.ComputeViewProjection( camera, aspect );

        _attributes.Set( "g_matWidgetViewProj", viewProj );
        _attributes.Set( "g_vWidgetCamRight", camera.WorldRotation.Right );
        _attributes.Set( "g_vWidgetCamUp", camera.WorldRotation.Up );
    }

    public override void RenderSceneObject()
    {
        if ( _activeNodeCount == 0 || _instanceBuffer == null )
            return;

        _attributes.Set( "InstanceBuffer", _instanceBuffer );
        _attributes.Set( "AtlasTexture", _atlas.GpuTexture );

        Graphics.DrawModelInstanced( _unitQuadModel, _activeNodeCount, _attributes );
    }


    public void Dispose()
    {
        _instanceBuffer?.Dispose();
        Delete();
    }
}