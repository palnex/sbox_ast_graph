#nullable enable
using System;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Models;

/// <summary>
/// Heavy metadata payload stored outside the per-tick physics loop.
/// </summary>
public sealed class NodePayload
{
    public int Index { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? FilePath { get; set; }
    public int LineNumber { get; set; } = 1;

    public Color AccentColor { get; set; } = Theme.Primary;
    public string Icon { get; set; } = "circle";

    public int TotalDegree { get; set; } = 1;
    public float PhysicsMass { get; set; } = 1.0f;

    /// <summary>
    /// Indices of outgoing connections in the edge registry.
    /// </summary>
    public int[] OutgoingEdges { get; set; } = Array.Empty<int>();

    /// <summary>
    /// Indices of incoming connections in the edge registry.
    /// </summary>
    public int[] IncomingEdges { get; set; } = Array.Empty<int>();

    /// <summary>
    /// Optional underlying AST / Dependency Graph model reference.
    /// </summary>
    public object? UserData { get; set; }
}