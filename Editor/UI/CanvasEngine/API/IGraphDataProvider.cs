#nullable enable
namespace ArchitectureVisualizer.UI.CanvasEngine.API;

/// <summary>
/// Universal data provider contract for streaming external data models into the Graph Canvas Engine.
/// </summary>
public interface IGraphDataProvider
{
    /// <summary>
    /// Populates the canvas with nodes, relations, and initial layout positions.
    /// </summary>
    void Populate( ICanvasGraph graph );
}