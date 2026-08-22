#nullable enable
using System;
using ArchitectureVisualizer.UI.CanvasEngine.Core;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;
using Sandbox.Rendering;

namespace ArchitectureVisualizer.UI.CanvasEngine.Rendering;

/// <summary>
/// Zero-Copy GPU SDF node renderer drawing instances directly from VRAM buffers.
/// </summary>
public sealed class GraphNodeSceneObject : SceneCustomObject
{
    private readonly Model _nodeModel;
    private readonly Texture _colorTexture;
    private readonly RenderAttributes _renderAttributes = new();
    private readonly Color32[] _colorStaging = new Color32[512 * 512];
    private int _count = 0;

    private Transform[] _transforms = new Transform[4096];
    public RenderAttributes RenderAttributes => _renderAttributes;

    public GraphNodeSceneObject( SceneWorld world ) : base( world )
    {
        _colorTexture = new Texture2DBuilder()
            .WithName( "g_tColors" )
            .WithSize( 512, 512 )
            .WithFormat( ImageFormat.RGBA8888 )
            .WithDynamicUsage()
            .WithAnonymous( true )
            .Finish();

        Array.Fill( _colorStaging, new Color32( 255, 255, 255, 255 ) );
        _colorTexture.Update<Color32>( _colorStaging );

        _renderAttributes.Set( "g_tColors", _colorTexture );

        var material = Material.Load( "materials/graph_node.vmat" )
                       ?? Material.FromShader( "shaders/graph_node.shader" )
                       ?? Material.Load( "materials/dev/primary_white.vmat" );

        _nodeModel = CreateProceduralQuadModel( material );
        Bounds = new BBox( new Vector3( -200000, -200000, -200000 ), new Vector3( 200000, 200000, 200000 ) );
    }

    private static Model CreateProceduralQuadModel( Material material )
    {
        const float radius = 10.0f;

        var vertices = new Vertex[]
        {
            new() { Position = new Vector3( -radius, -radius, 0 ), TexCoord0 = new Vector4( 0, 0, 0, 0 ) },
            new() { Position = new Vector3(  radius, -radius, 0 ), TexCoord0 = new Vector4( 1, 0, 0, 0 ) },
            new() { Position = new Vector3(  radius,  radius, 0 ), TexCoord0 = new Vector4( 1, 1, 0, 0 ) },
            new() { Position = new Vector3( -radius,  radius, 0 ), TexCoord0 = new Vector4( 0, 1, 0, 0 ) }
        };

        var indices = new int[]
        {
            0, 1, 2,
            0, 2, 3
        };

        var mesh = new Mesh( material );
        mesh.CreateVertexBuffer( vertices.Length, vertices );
        mesh.CreateIndexBuffer( indices.Length, indices );
        mesh.Bounds = new BBox( new Vector3( -radius, -radius, -1f ), new Vector3( radius, radius, 1f ) );

        var builder = new ModelBuilder();
        builder.AddMesh( mesh );
        return builder.Create();
    }

    public void UpdateNodes( ReadOnlySpan<Transform> transforms, ReadOnlySpan<NodeSpatialData> spatials, ReadOnlySpan<Color> colors )
    {
        _count = transforms.Length;
        if ( _count == 0 || _colorTexture == null || !_colorTexture.IsValid ) return;

        if ( _transforms.Length < _count )
        {
            Array.Resize( ref _transforms, Math.Max( _transforms.Length * 2, _count ) );
        }

        transforms.CopyTo( _transforms );

        int uploadCount = Math.Min( _count, 512 * 512 );
        for ( int i = 0; i < uploadCount; i++ )
        {
            ref readonly var spatial = ref spatials[i];
            Color baseCol = colors[i];

            byte flags = (byte)((byte)spatial.Shape & 0x0F);
            if ( spatial.IsHovered ) flags |= (1 << 4);
            if ( spatial.IsSelected ) flags |= (1 << 5);
            if ( spatial.IsDimmed ) flags |= (1 << 6);
            if ( spatial.IsPinned ) flags |= (1 << 7);

            _colorStaging[i] = new Color32(
                (byte)(baseCol.r * 255f),
                (byte)(baseCol.g * 255f),
                (byte)(baseCol.b * 255f),
                flags
            );
        }

        _colorTexture.Update<Color32>( _colorStaging );
    }

    public void Render( GpuBuffer<GpuNodePhysicsData>? nodesBuffer, float nodeScale )
    {
        if ( _count == 0 || _nodeModel == null || _colorTexture == null || !_colorTexture.IsValid ) return;
        if ( nodesBuffer == null || !nodesBuffer.IsValid ) return;

        _renderAttributes.Set( "g_tColors", _colorTexture );
        _renderAttributes.Set( "NodesBuffer", nodesBuffer );
        _renderAttributes.Set( "NodeSizeScale", nodeScale );

        // Direct VRAM Instanced Draw (0 transforms uploaded from CPU!)
        Graphics.DrawModelInstanced( _nodeModel, _count, _renderAttributes );
    }

    public override void RenderSceneObject()
    {
        if ( _count == 0 || _nodeModel == null || _colorTexture == null || !_colorTexture.IsValid ) return;

        _renderAttributes.Set( "g_tColors", _colorTexture );

        Span<Transform> instances = new( _transforms, 0, _count );
        Graphics.DrawModelInstanced( _nodeModel, instances, _renderAttributes );
    }

    public void Dispose()
    {
        _colorTexture?.Dispose();
        Delete();
    }
}