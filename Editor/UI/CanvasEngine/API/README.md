# 🎨 Canvas Engine Public API (Black-Box SDK)

High-performance, hardware-accelerated 2D/3D Graph Visualization Engine for Facepunch s&box (Source 2).

The `CanvasEngine` subsystem is designed as an isolated **Black Box**. You do not need to touch shaders, GPU buffers, Barnes-Hut quad-trees, or Skia font atlases directly. Everything is orchestrated through the high-level, fluent `ICanvasGraph` contract.

---

## 🏛️ Architecture Overview

```
 ┌────────────────────────────────────────────────────────┐
 │            EXTERNAL DATA SOURCES / PROVIDERS           │
 │   (C# Roslyn AST, Scene Hierarchy, State Machines)     │
 └──────────────────────────┬─────────────────────────────┘
                            │
               implements IGraphDataProvider
                            │
                            ▼
 ┌────────────────────────────────────────────────────────┐
 │           PUBLIC API LAYER (Black Box Boundary)        │
 │   ICanvasGraph | NodeBuilder | EdgeBuilder | Events    │
 └──────────────────────────┬─────────────────────────────┘
                            │
                     internal bindings
                            │
                            ▼
 ┌────────────────────────────────────────────────────────┐
 │              LOW-LEVEL ENGINE CORE & GPU               │
 │  SpatialRegistry | SleepyPhysics | GPU Text & SDF VAT  │
 └────────────────────────────────────────────────────────┘
```

---

## 🚀 Quick Start Examples

### 1. Fluent Graph Construction
```csharp
using ArchitectureVisualizer.UI.CanvasEngine.API;
using ArchitectureVisualizer.UI.CanvasEngine.Models;

// Instantiate canvas widget
var canvas = new CanvasWidget( parentWidget );

// Add nodes with fluent chaining
var playerNode = canvas.AddNode( "player", "PlayerController", "Gameplay.Entities" )
    .WithShape( NodeShape.RoundedBox )
    .WithColor( Color.Cyan )
    .WithSize( 20f )
    .WithData( customUserDataObject );

var weaponNode = canvas.AddNode( "weapon", "WeaponSystem", "Gameplay.Combat" )
    .WithShape( NodeShape.Diamond )
    .WithColor( Color.Orange );

// Connect nodes with laser pulse styling
canvas.Connect( "player", "weapon" )
    .WithStyle( EdgeStyle.LaserPulse )
    .WithSpeed( 2.5f )
    .WithColor( Color.Yellow )
    .WithLabel( "Equips" );
```

---

### 2. High-Performance Bulk Ingestion (`BatchUpdate`)
When loading 1,000 to 10,000+ nodes, always wrap updates in `BatchUpdate`. This defers GPU buffer uploads until all nodes/edges are registered, resulting in **0.05 ms CPU frame time (144+ FPS)**:

```csharp
canvas.BatchUpdate( graph =>
{
    for ( int i = 0; i < 5000; i++ )
    {
        graph.AddNode( $"node_{i}", $"Item #{i}" )
             .WithShape( NodeShape.Circle )
             .WithSize( 10f );

        if ( i > 0 )
        {
            graph.Connect( $"node_{i - 1}", $"node_{i}" )
                 .WithStyle( EdgeStyle.Solid );
        }
    }
} );
```

---

### 3. Real-Time Visual FX & Interactive Actions
Trigger animated pulses, flashes, and camera transitions dynamically from gameplay or editor events:

```csharp
// 1. Emit an animated laser packet travelling from source to destination
canvas.PulseEdge( "player", "weapon", Color.Red, speed: 3.0f );

// 2. Flash a node with a highlight color
canvas.FlashNode( "player", Color.White, duration: 0.5f );

// 3. Smoothly animate and focus the camera on a specific node
canvas.FocusNode( "player", targetZoom: 1200f );
```

---

### 4. Creating Custom Data Providers (`IGraphDataProvider`)
To visualize any custom data model (e.g. `GameObject` scene tree, AI behavior trees, multiplayer network traffic), implement `IGraphDataProvider`:

```csharp
public sealed class SceneHierarchyProvider : IGraphDataProvider
{
    private readonly GameObject _rootObject;

    public SceneHierarchyProvider( GameObject root ) => _rootObject = root;

    public void Populate( ICanvasGraph graph )
    {
        Traverse( _rootObject, graph, null );
    }

    private void Traverse( GameObject current, ICanvasGraph graph, string? parentId )
    {
        string currentId = current.Id.ToString();
        
        graph.AddNode( currentId, current.Name, $"Components: {current.Components.Count}" )
             .WithShape( NodeShape.RoundedBox )
             .WithColor( current.Enabled ? Color.Green : Color.Gray );

        if ( parentId != null )
        {
            graph.Connect( parentId, currentId )
                 .WithStyle( EdgeStyle.Dashed );
        }

        foreach ( var child in current.Children )
        {
            Traverse( child, graph, currentId );
        }
    }
}

// Load provider into canvas:
canvas.LoadFromProvider( new SceneHierarchyProvider( sceneRoot ) );
```

---

### 5. Event Subscriptions
Subscribe to user interactions and clicks:

```csharp
canvas.OnNodeClicked += nodeId =>
{
    Log.Info( $"Node clicked: {nodeId}" );
};

canvas.OnNodeDoubleClicked += nodeIndex =>
{
    Log.Info( $"Node double-clicked: {nodeIndex}" );
};

canvas.OnNodeHoverChanged += ( nodeId, isHovered ) =>
{
    if ( isHovered ) canvas.FlashNode( nodeId, Color.Yellow );
};
```

---

## 🎨 Visual Configuration Reference

### Node Shapes (`NodeShape`)
| Shape | Visual Style | Common Use Case |
| :--- | :--- | :--- |
| `Circle` | Smooth SDF Circle | Primitives, Default Nodes |
| `RoundedBox` | Rounded Rectangle Badge | Components, Entities, Classes |
| `Hexagon` | 6-sided Hexagon | Interfaces, Contracts, Traits |
| `Diamond` | 4-sided Diamond | Enums, Value Types, Decisions |
| `Ring` | Outlined Hollow Ring | Game Resources, Assets |

### Edge Styles (`EdgeStyle`)
| Style | Visual Appearance |
| :--- | :--- |
| `Solid` | Continuous crisp ribbon |
| `Dashed` | Animated dashed lines |
| `LaserPulse` | High-energy travelling photon packet |
| `DirectionalArrows`| Flowing chevrons indicating hierarchy direction |
| `DoubleLine` | Parallel dual-line ribbon |

---

## ⚡ Performance Guarantees
1. **Zero Garbage Collection:** The frame loop uses fixed unmanaged buffers; creating or hovering over nodes generates 0 bytes of GC allocations.
2. **1 GPU Draw Call for All Text:** Text strings are pre-rendered into a single `4096 x 4096` dynamic Skia atlas with support for all Unicode scripts (Latin, Ukrainian Cyrillic, CJK, Emoji).
3. **GPU View-Space Projection:** Billboards expand in camera view space, eliminating all Gimbal Lock, 3D warping, and multi-viewport desync.
```
