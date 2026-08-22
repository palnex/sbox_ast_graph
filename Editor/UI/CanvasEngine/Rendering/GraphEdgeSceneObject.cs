#nullable enable
using System;
using System.Runtime.InteropServices;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Rendering;

/// <summary>
/// Explicit vertex structure matching the HLSL VertexInput layout exactly.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct CustomRibbonVertex
{
    [VertexLayout.Position]
    public Vector3 Position;

    [VertexLayout.Normal]
    public Vector3 Normal;

    [VertexLayout.Tangent]
    public Vector4 Tangent;

    [VertexLayout.TexCoord]
    public Vector2 TexCoord;

    [VertexLayout.Color]
    public Color32 Color;

    public CustomRibbonVertex( Vector3 pos, Vector2 uv, Color32 col )
    {
        Position = pos;
        Normal = Vector3.Up;
        Tangent = new Vector4( 1, 0, 0, 1 );
        TexCoord = uv;
        Color = col;
    }
}

/// <summary>
/// GPU-accelerated dynamic edge renderer managing procedural ribbon mesh on a SceneObject.
/// </summary>
public sealed class GraphEdgeSceneObject : IDisposable, Sandbox.IValid
{
    private readonly SceneWorld _sceneWorld;
    private readonly Material _lineMaterial;
    private SceneObject? _sceneObject;

    private CustomRibbonVertex[] _vertices = new CustomRibbonVertex[8192];
    private int[] _indices = new int[12288];

    public bool IsValid => _sceneObject != null && _sceneObject.IsValid();
    bool Sandbox.IValid.IsValid => IsValid;

    public GraphEdgeSceneObject( SceneWorld world )
    {
        _sceneWorld = world;
        _lineMaterial = Material.Load( "materials/graph_edge.vmat" )
                        ?? Material.FromShader( "shaders/graph_edge.shader" )
                        ?? Material.Load( "materials/dev/primary_white.vmat" );

        var builder = new ModelBuilder();
        var model = builder.Create();

        _sceneObject = new SceneObject( _sceneWorld, model )
        {
            Transform = new Transform( Vector3.Zero, Rotation.Identity, 1.0f ),
            Flags = { CastShadows = false }
        };

        var bigBounds = new BBox( new Vector3( -200000, -200000, -200000 ), new Vector3( 200000, 200000, 200000 ) );
        _sceneObject.Bounds = bigBounds;
    }

    public void UploadEdges( ReadOnlySpan<(Vector3 Start, Vector3 End, Color32 Color, EdgeStyle Style, float Speed)> edges, float thickness = 5.0f )
    {
        int edgeCount = edges.Length;
        if ( _sceneObject == null || !_sceneObject.IsValid() ) return;

        if ( edgeCount == 0 )
        {
            _sceneObject.Model = new ModelBuilder().Create();
            return;
        }

        int requiredVertices = edgeCount * 4;
        int requiredIndices = edgeCount * 6;

        if ( _vertices.Length < requiredVertices )
        {
            Array.Resize( ref _vertices, Math.Max( _vertices.Length * 2, requiredVertices ) );
        }

        if ( _indices.Length < requiredIndices )
        {
            Array.Resize( ref _indices, Math.Max( _indices.Length * 2, requiredIndices ) );
        }

        float halfThick = MathF.Max( 1.5f, thickness * 0.5f );
        const float zOffset = -1.0f;

        int vertIdx = 0;
        int indIdx = 0;

        for ( int i = 0; i < edgeCount; i++ )
        {
            var edge = edges[i];
            if ( edge.Color.a == 0 ) continue;

            Vector3 p0 = edge.Start + new Vector3( 0, 0, zOffset );
            Vector3 p1 = edge.End + new Vector3( 0, 0, zOffset );

            Vector3 dir = (p1 - p0).Normal;
            if ( dir.LengthSquared < 0.001f ) continue;

            Vector3 side = new Vector3( -dir.y, dir.x, 0f ).Normal * halfThick;

            int baseV = vertIdx;
            float styleVal = (float)edge.Style;
            float speedVal = edge.Speed;
            Vector3 edgeParams = new Vector3( styleVal, speedVal, 0f );

            _vertices[vertIdx++] = new CustomRibbonVertex( p0 - side, new Vector2( 0f, 0f ), edge.Color ) { Normal = edgeParams };
            _vertices[vertIdx++] = new CustomRibbonVertex( p0 + side, new Vector2( 0f, 1f ), edge.Color ) { Normal = edgeParams };
            _vertices[vertIdx++] = new CustomRibbonVertex( p1 + side, new Vector2( 1f, 1f ), edge.Color ) { Normal = edgeParams };
            _vertices[vertIdx++] = new CustomRibbonVertex( p1 - side, new Vector2( 1f, 0f ), edge.Color ) { Normal = edgeParams };

            // Triangle 1
            _indices[indIdx++] = baseV + 0;
            _indices[indIdx++] = baseV + 1;
            _indices[indIdx++] = baseV + 2;

            // Triangle 2
            _indices[indIdx++] = baseV + 0;
            _indices[indIdx++] = baseV + 2;
            _indices[indIdx++] = baseV + 3;
        }

        if ( vertIdx == 0 )
        {
            _sceneObject.Model = new ModelBuilder().Create();
            return;
        }

        var mesh = new Mesh( _lineMaterial );
        mesh.CreateVertexBuffer( vertIdx, _vertices.AsSpan( 0, vertIdx ) );
        mesh.CreateIndexBuffer( indIdx, _indices.AsSpan( 0, indIdx ) );

        var bigBounds = new BBox( new Vector3( -200000, -200000, -200000 ), new Vector3( 200000, 200000, 200000 ) );
        mesh.Bounds = bigBounds;

        var builder = new ModelBuilder();
        builder.AddMesh( mesh );
        _sceneObject.Model = builder.Create();
        _sceneObject.Bounds = bigBounds;
    }

    public void Dispose()
    {
        if ( _sceneObject != null && _sceneObject.IsValid() )
        {
            _sceneObject.Delete();
            _sceneObject = null;
        }
    }
}