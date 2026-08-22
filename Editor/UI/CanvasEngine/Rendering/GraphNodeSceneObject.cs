#nullable enable
using System;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;
using Sandbox.Rendering;

namespace ArchitectureVisualizer.UI.CanvasEngine.Rendering;

/// <summary>
/// Ultra high-performance GPU SDF node renderer using procedural quads and VAT byte packing.
/// </summary>
public sealed class GraphNodeSceneObject : SceneCustomObject
{
    private readonly Model _nodeModel;
    private readonly Texture _colorTexture;
    private readonly RenderAttributes _renderAttributes;

    private readonly Color32[] _colorStaging = new Color32[512 * 512];
    private Transform[] _transforms;
    private int _count = 0;

    public GraphNodeSceneObject( SceneWorld world, int initialCapacity = 4096 ) : base( world )
    {
        _renderAttributes = new RenderAttributes();

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

        // 4-vertex procedural quad with UVs [-1..1] for SDF raymarching
        _nodeModel = CreateProceduralQuadModel( material );

        _transforms = new Transform[initialCapacity];
        Bounds = new BBox( new Vector3( -100000, -100000, -100000 ), new Vector3( 100000, 100000, 100000 ) );
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

            // Bits 0..3: Shape, Bit 4: Hover, Bit 5: Selected, Bit 6: Dimmed, Bit 7: Pinned
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