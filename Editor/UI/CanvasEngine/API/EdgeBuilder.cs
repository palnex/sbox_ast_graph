#nullable enable
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using ArchitectureVisualizer.UI;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.API;

/// <summary>
/// Fluent builder for zero-allocation configuration of canvas edges.
/// </summary>
public readonly ref struct EdgeBuilder
{
    private readonly CanvasWidget _canvas;
    private readonly int _edgeIndex;

    public int Index => _edgeIndex;
    public bool IsValid => _edgeIndex >= 0 && _edgeIndex < _canvas.Edges.Count;

    public EdgeBuilder( CanvasWidget canvas, int edgeIndex )
    {
        _canvas = canvas;
        _edgeIndex = edgeIndex;
    }

    public EdgeBuilder WithStyle( EdgeStyle style )
    {
        if ( !IsValid ) return this;
        _canvas.Edges[_edgeIndex].Style = style;
        return this;
    }

    public EdgeBuilder WithColor( Color color )
    {
        if ( !IsValid ) return this;
        _canvas.Edges[_edgeIndex].CustomColor = color;
        return this;
    }

    public EdgeBuilder WithSpeed( float flowSpeed )
    {
        if ( !IsValid ) return this;
        _canvas.Edges[_edgeIndex].FlowSpeed = flowSpeed;
        return this;
    }

    public EdgeBuilder WithSpringLength( float desiredLength )
    {
        if ( !IsValid ) return this;
        _canvas.Edges[_edgeIndex].DesiredSpringLength = desiredLength;
        return this;
    }

    public EdgeBuilder WithLabel( string? label )
    {
        if ( !IsValid ) return this;
        _canvas.Edges[_edgeIndex].Label = label;
        return this;
    }

    public EdgeBuilder WithData( object? userData )
    {
        if ( !IsValid ) return this;
        _canvas.Edges[_edgeIndex].UserData = userData;
        return this;
    }
}