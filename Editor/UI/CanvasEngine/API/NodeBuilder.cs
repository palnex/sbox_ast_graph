#nullable enable
using System;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using ArchitectureVisualizer.UI;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.API;

/// <summary>
/// Fluent builder for zero-allocation configuration of canvas nodes.
/// </summary>
public readonly ref struct NodeBuilder
{
    private readonly CanvasWidget _canvas;
    private readonly int _nodeIndex;
    private readonly string _nodeId;

    public string Id => _nodeId;
    public int Index => _nodeIndex;
    public bool IsValid => _nodeIndex >= 0;

    public NodeBuilder( CanvasWidget canvas, int nodeIndex, string nodeId )
    {
        _canvas = canvas;
        _nodeIndex = nodeIndex;
        _nodeId = nodeId;
    }

    public NodeBuilder WithShape( NodeShape shape )
    {
        if ( !IsValid ) return this;
        _canvas.Registry.GetSpatialRef( _nodeIndex ).Shape = shape;
        _canvas.MarkNodesDirty();
        return this;
    }

    public NodeBuilder WithColor( Color color )
    {
        if ( !IsValid ) return this;
        _canvas.Registry.GetPayload( _nodeIndex ).AccentColor = color;
        _canvas.MarkNodesDirty();
        return this;
    }

    public NodeBuilder WithSize( float radius )
    {
        if ( !IsValid ) return this;
        _canvas.Registry.GetSpatialRef( _nodeIndex ).Radius = MathF.Max( 2f, radius );
        _canvas.MarkNodesDirty();
        return this;
    }

    public NodeBuilder WithZLevel( int level )
    {
        if ( !IsValid ) return this;
        _canvas.Registry.GetSpatialRef( _nodeIndex ).ZLevel = (ushort)Math.Clamp( level, 0, 32 );
        return this;
    }

    public NodeBuilder WithPosition( Vector2 position )
    {
        if ( !IsValid ) return this;
        _canvas.Registry.GetSpatialRef( _nodeIndex ).Position = position;
        return this;
    }

    public NodeBuilder WithPinned( bool isPinned )
    {
        if ( !IsValid ) return this;
        _canvas.Registry.GetSpatialRef( _nodeIndex ).SetFlag( NodeFlags.Pinned, isPinned );
        return this;
    }

    public NodeBuilder WithData( object? userData, string? summary = null, string? filePath = null, int lineNumber = 1 )
    {
        if ( !IsValid ) return this;
        var payload = _canvas.Registry.GetPayload( _nodeIndex );
        payload.UserData = userData;
        payload.Summary = summary;
        payload.FilePath = filePath;
        payload.LineNumber = lineNumber;
        return this;
    }
}