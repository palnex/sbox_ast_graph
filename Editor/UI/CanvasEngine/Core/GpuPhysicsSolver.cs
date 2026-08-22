#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Core;

[StructLayout( LayoutKind.Sequential )]
public struct GpuNodePhysicsData
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Radius;
    public float Mass;
    public uint Flags; // Bit 0: Pinned, Bit 1: Hidden
    public uint TotalDegree;
}

[StructLayout( LayoutKind.Sequential )]
public struct GpuEdgePhysicsData
{
    public uint SourceIndex;
    public uint TargetIndex;
    public float DesiredDistance;
    public float SpringStrength;
}

/// <summary>
/// GPU-accelerated Atomic Force-Directed physics solver running 100% on Compute Shaders.
/// </summary>
public sealed class GpuPhysicsSolver : IDisposable
{
    private ComputeShader? _computeShader;
    private GpuBuffer<GpuNodePhysicsData>? _nodesBufferA;
    private GpuBuffer<GpuNodePhysicsData>? _nodesBufferB;
    private GpuBuffer<GpuEdgePhysicsData>? _edgesBuffer;
    private GpuBuffer<int>? _accumForcesBuffer;

    private GpuNodePhysicsData[] _hostNodes = Array.Empty<GpuNodePhysicsData>();
    private GpuEdgePhysicsData[] _hostEdges = Array.Empty<GpuEdgePhysicsData>();
    private int _nodeCount = 0;
    private int _edgeCount = 0;

    public GpuBuffer<GpuNodePhysicsData>? CurrentNodesBuffer => _nodesBufferA;
    public GpuBuffer<GpuEdgePhysicsData>? EdgesBuffer => _edgesBuffer;

    public float Alpha { get; private set; } = 1.0f;
    public float AlphaTarget { get; set; } = 0.0f;
    public float AlphaDecay { get; set; } = 0.022f;
    public float AlphaMin { get; set; } = 0.001f;

    public float RepulsionConstant { get; set; } = 10.0f;
    public float RepulsionMaxDist { get; set; } = 2500f;
    public float LinkDistanceSetting { get; set; } = 160f;
    public float LinkForceSetting { get; set; } = 1.0f;
    public float CenterForceSetting { get; set; } = 0.35f;
    public float Damping { get; set; } = 0.68f;
    public float TerminalVelocity { get; set; } = 15f;

    public bool PauseDuringPlay { get; set; } = true;
    public bool IsSleeping => Alpha < AlphaMin;

    public void Reheat( float energy = 1.0f ) => Alpha = Math.Max( Alpha, energy );
    public void WakeUp() => Reheat( 1.0f );

    public GpuPhysicsSolver()
    {
        try
        {
            _computeShader = new ComputeShader( "shaders/graph_physics.shader" );
        }
        catch ( Exception ex )
        {
            Log.Warning( $"Compute shader failed to load: {ex.Message}" );
        }
    }

    public void InitializeBuffers( SpatialRegistry registry, IReadOnlyList<CanvasEdge> edges )
    {
        _nodeCount = registry.Count;
        _edgeCount = edges.Count;

        if ( _nodeCount == 0 ) return;

        if ( _hostNodes.Length < _nodeCount )
            Array.Resize( ref _hostNodes, Math.Max( _nodeCount * 2, 64 ) );

        if ( _hostEdges.Length < _edgeCount )
            Array.Resize( ref _hostEdges, Math.Max( _edgeCount * 2, 64 ) );

        var spatials = registry.GetReadOnlySpatialSpan();

        for ( int i = 0; i < _nodeCount; i++ )
        {
            ref readonly var s = ref spatials[i];
            var payload = registry.GetPayload( i );

            uint flags = 0;
            if ( s.IsPinned ) flags |= (1 << 0);
            if ( s.IsHidden ) flags |= (1 << 1);

            _hostNodes[i] = new GpuNodePhysicsData
            {
                Position = s.Position,
                Velocity = s.Velocity,
                Radius = s.Radius,
                Mass = Math.Max( 0.5f, payload.PhysicsMass ),
                Flags = flags,
                TotalDegree = (uint)Math.Max( 1, payload.TotalDegree )
            };
        }

        for ( int i = 0; i < _edgeCount; i++ )
        {
            var e = edges[i];
            _hostEdges[i] = new GpuEdgePhysicsData
            {
                SourceIndex = (uint)e.SourceIndex,
                TargetIndex = (uint)e.TargetIndex,
                DesiredDistance = e.DesiredSpringLength,
                SpringStrength = 1.0f
            };
        }

        _nodesBufferA?.Dispose();
        _nodesBufferB?.Dispose();
        _edgesBuffer?.Dispose();
        _accumForcesBuffer?.Dispose();

        _nodesBufferA = new GpuBuffer<GpuNodePhysicsData>( _nodeCount, GpuBuffer.UsageFlags.Structured );
        _nodesBufferB = new GpuBuffer<GpuNodePhysicsData>( _nodeCount, GpuBuffer.UsageFlags.Structured );
        _edgesBuffer = new GpuBuffer<GpuEdgePhysicsData>( Math.Max( 1, _edgeCount ), GpuBuffer.UsageFlags.Structured );
        _accumForcesBuffer = new GpuBuffer<int>( Math.Max( 1, _nodeCount * 2 ), GpuBuffer.UsageFlags.Structured );

        _nodesBufferA.SetData( _hostNodes.AsSpan( 0, _nodeCount ) );
        _nodesBufferB.SetData( _hostNodes.AsSpan( 0, _nodeCount ) );

        if ( _edgeCount > 0 ) _edgesBuffer.SetData( _hostEdges.AsSpan( 0, _edgeCount ) );
    }

    public void Step( SpatialRegistry registry, float dt, float nodeSizeScale, int draggedIndex = -1, Vector2 dragPos = default )
    {
        if ( _nodeCount == 0 || _computeShader == null ) return;
        if ( _nodesBufferA == null || !_nodesBufferA.IsValid || _nodesBufferB == null || !_nodesBufferB.IsValid || _accumForcesBuffer == null || !_accumForcesBuffer.IsValid ) return;

        if ( draggedIndex >= 0 )
        {
            Alpha = Math.Max( Alpha, 0.40f );
        }

        if ( IsSleeping ) return;

        // 1. Cool down Alpha
        Alpha += (AlphaTarget - Alpha) * AlphaDecay;

        // 2. Set Attributes
        _computeShader.Attributes.Set( "DeltaTime", dt );
        _computeShader.Attributes.Set( "Alpha", Alpha );
        _computeShader.Attributes.Set( "RepulsionStrength", RepulsionConstant );
        _computeShader.Attributes.Set( "RepelMaxDist", RepulsionMaxDist );
        _computeShader.Attributes.Set( "LinkDistance", LinkDistanceSetting );
        _computeShader.Attributes.Set( "LinkForce", LinkForceSetting );
        _computeShader.Attributes.Set( "CenterForce", CenterForceSetting );
        _computeShader.Attributes.Set( "Damping", Damping );
        _computeShader.Attributes.Set( "TerminalSpeed", TerminalVelocity );
        _computeShader.Attributes.Set( "NodeSizeScale", nodeSizeScale );
        _computeShader.Attributes.Set( "NumNodes", (uint)_nodeCount );
        _computeShader.Attributes.Set( "NumEdges", (uint)_edgeCount );

        _computeShader.Attributes.Set( "DraggedNodeId", draggedIndex );
        _computeShader.Attributes.Set( "DragTargetPos", dragPos );

        _computeShader.Attributes.Set( "InNodes", _nodesBufferA );
        _computeShader.Attributes.Set( "InEdges", _edgesBuffer );
        _computeShader.Attributes.Set( "AccumForces", _accumForcesBuffer );
        _computeShader.Attributes.Set( "OutNodes", _nodesBufferB );

        // ==========================================
        // 3. THREE-STAGE ATOMIC GPU DISPATCH
        // ==========================================

        // Pass 0: Zero out accumulation buffer (1 thread per node)
        _computeShader.Attributes.Set( "PassMode", 0 );
        _computeShader.Dispatch( _nodeCount, 1, 1 );

        // Pass 1: Atomic springs evaluation (1 thread per edge -> O(E)!)
        if ( _edgeCount > 0 )
        {
            _computeShader.Attributes.Set( "PassMode", 1 );
            _computeShader.Dispatch( _edgeCount, 1, 1 );
        }

        // Pass 2: Repulsion, gravity & integration (1 thread per node)
        _computeShader.Attributes.Set( "PassMode", 2 );
        _computeShader.Dispatch( _nodeCount, 1, 1 );

        // 4. Ping-Pong Buffers in VRAM
        var temp = _nodesBufferA;
        _nodesBufferA = _nodesBufferB;
        _nodesBufferB = temp;

        // 5. Direct Readback into SpatialRegistry
        _nodesBufferA.GetData( _hostNodes, 0, _nodeCount );
        var liveSpatials = registry.GetSpatialSpan();

        for ( int i = 0; i < _nodeCount; i++ )
        {
            liveSpatials[i].Position = _hostNodes[i].Position;
            liveSpatials[i].Velocity = _hostNodes[i].Velocity;
        }
    }

    public void Dispose()
    {
        _nodesBufferA?.Dispose();
        _nodesBufferB?.Dispose();
        _edgesBuffer?.Dispose();
        _accumForcesBuffer?.Dispose();
        _nodesBufferA = null;
        _nodesBufferB = null;
        _edgesBuffer = null;
        _accumForcesBuffer = null;
    }
}