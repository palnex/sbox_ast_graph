#nullable enable
using System;
using System.Collections.Generic;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.API;

/// <summary>
/// High-performance public API contract for the hardware-accelerated Graph Canvas Engine.
/// </summary>
public interface ICanvasGraph
{
    // ==========================================
    // NODE OPERATIONS
    // ==========================================
    NodeBuilder AddNode( string id, string title, string? subtitle = null );
    bool RemoveNode( string id );
    bool HasNode( string id );
    int NodeCount { get; }

    // ==========================================
    // EDGE OPERATIONS
    // ==========================================
    EdgeBuilder Connect( string sourceId, string targetId );
    bool Disconnect( string sourceId, string targetId );
    int EdgeCount { get; }

    // ==========================================
    // BATCH OPERATIONS & PROVIDERS
    // ==========================================
    /// <summary>
    /// Executes bulk graph updates with delayed GPU buffer synchronization for zero-lag batch ingestion.
    /// </summary>
    void BatchUpdate( Action<ICanvasGraph> updateAction );

    /// <summary>
    /// Loads graph topology from any external data provider (Roslyn AST, Scene Graph, State Machine, etc.).
    /// </summary>
    void LoadFromProvider( IGraphDataProvider provider );

    // ==========================================
    // REAL-TIME FX, ANIMATIONS & TRIGGERS
    // ==========================================
    /// <summary>
    /// Triggers an animated laser pulse packet travelling from source to target node.
    /// </summary>
    void PulseEdge( string sourceId, string targetId, Color? pulseColor = null, float speed = 2.0f );

    /// <summary>
    /// Smoothly focuses and animates the camera onto the specified node.
    /// </summary>
    void FocusNode( string id, float targetZoom = 1500f );

    /// <summary>
    /// Clears all nodes, edges, and active physics state.
    /// </summary>
    void Clear();

    // ==========================================
    // EVENT SUBSCRIPTIONS
    // ==========================================
    event Action<string>? OnNodeClicked;
    event Action<string>? OnNodeIdDoubleClicked;
    event Action<string, bool>? OnNodeHoverChanged;
    event Action<string, string>? OnEdgeClicked;
    event Action<int>? OnNodeSelected;
    event Action<int>? OnNodeDoubleClicked;
}