## 🚀 Quick Start & Public API Cheat Sheet

All analysis operations are accessed through the static facade `Editor.Analysis.CodeAnalysis`:

### 1. Get Node & Inspect Anatomy
```csharp
using Editor.Analysis;
using Editor.Analysis.Models;

// Query by short name, FQN, or DocId
NodeBlock? playerNode = CodeAnalysis.GetNode( "PlayerController" );
NodeBlock? bboxNode = CodeAnalysis.GetNode( "BBox" );
NodeBlock? compNode = CodeAnalysis.GetNode<Sandbox.Component>();

if ( playerNode != null )
{
    // Access 1. BODY (Identity & Location)
    string docId = playerNode.DocId;             // "T:MyGame.PlayerController"
    string title = playerNode.Body.Title;        // "Player Controller"
    string file = playerNode.Body.FilePath;      // "code/PlayerController.cs"
    int line = playerNode.Body.LineNumber;       // 42

    // Jump to IDE at exact line number
    playerNode.OpenInEditor();

    // Access 2. MEMBERS (Methods, Properties, Fields)
    foreach ( var method in playerNode.Members.Methods )
    {
        Log.Info( $"{method.FullSignature} - {method.Summary}" );
    }

    // Access 3. WIRES (Semantic Relations)
    foreach ( var wire in playerNode.Relations.Outgoing )
    {
        Log.Info( $"Calls ─[{wire.Action}]─► {wire.RecipientDocId} ({wire.Instrument})" );
    }
}
```

---

### 2. Query Polymorphic Interface Implementations
```csharp
// Find all concrete classes that implement an interface
IReadOnlyList<string> damageables = CodeAnalysis.GetImplementations( "IDamageable" );
// Returns: ["T:MyGame.Monster", "T:MyGame.Boss", "T:MyGame.Player"]

IReadOnlyList<string> components = CodeAnalysis.GetImplementations<Sandbox.Component>();
```

---

### 3. Discover APIs by Type (e.g. The `Vector3` Discovery)
```csharp
// Find all methods across the entire engine and game that accept Vector3
foreach ( var (node, method) in CodeAnalysis.FindMethodsAccepting( "Vector3" ) )
{
    Log.Info( $"{node.Body.Name}.{method.FullSignature}" );
}

// Find all methods returning BBox
foreach ( var (node, method) in CodeAnalysis.FindMethodsReturning( "BBox" ) )
{
    Log.Info( $"{node.Body.Name}.{method.Name}() -> Returns BBox" );
}
```

---

### 4. Query by Category or Package
```csharp
// Get all UI Panel Components
var uiPanels = CodeAnalysis.GetNodes( SandboxTypeCategory.UiPanel );

// Get all nodes originating from your game project
var gameNodes = CodeAnalysis.GetNodesByPackage( "towertinno" );

// Get all nodes from this library
var libNodes = CodeAnalysis.GetNodesByPackage( "sbox_ast_graph" );
```

---

### 5. Run Full 5D Diagnostic Output to Console
```csharp
CodeAnalysis.Diagnose( "CardManager" );
CodeAnalysis.Diagnose( "BBox" );
CodeAnalysis.Diagnose( "CanvasWidget" );
```

---

## 🏛️ Architecture & The 3-Pillar Data Model

Every code entity (Class, Struct, Interface, Method) is represented as a unified **`NodeBlock`**:

```
┌─────────────────────────────────────────────────────────────┐
│                          NodeBlock                          │
├──────────────────────────────┬──────────────────────────────┤
│ 1. BODY (Static Anatomy)     │ 2. WIRES (Semantic Nervous)  │
│    • Unique ECMA-334 DocId   │    • Outgoing Semantic Wires │
│    • Title, Icon, Namespace  │    • Incoming Semantic Wires │
│    • Source File & Line      │    • Polymorphic Fan-Outs    │
│    • Signatures & XML Docs   ├──────────────────────────────┤
│    • Hierarchy (Parent/Kids) │ 3. ACTIVITY (Live Telemetry) │
│                              │    • Invocation Frequency    │
│                              │    • Duration ($T_{ms}$)     │
│                              │    • Thermal Heat (0.0..1.0) │
└──────────────────────────────┴──────────────────────────────┘
```

### The 5D Semantic Wire Formula:
$$\text{Wire} = \langle \text{Agent}, \text{Action}, \text{Recipient}, \text{Instrument}, \text{Condition} \rangle$$

* **Agent (`AgentDocId`):** Unique caller DocId (`M:Tower.Fire`).
* **Action (`Action`):** Relationship verb (`MethodCall`, `Instantiates`, `ComponentFetch`, `Inherits`, `Implements`, `AsyncAwait`, `RpcDispatch`).
* **Recipient (`RecipientDocId`):** Unique target DocId (`M:Monster.TakeDamage`).
* **Instrument (`Instrument`):** Arguments / Return payload (`DamageInfo`, `Vector3`).
* **Condition (`Condition`):** Execution context or guards (`[Host]`, `[Authority]`, `[Rpc.Broadcast]`).

---

## ⚡ Performance & Scale
* **True Compiler Resolution:** Driven by Roslyn `CSharpCompilation` + `SemanticModel` and s&box `Sandbox.TypeLibrary`.
* **Zero String Hacks:** No regex parsing, no naming heuristics (`On...` checks), no fake LINQ types.
* **$O(1)$ Hash Table Deduplication:** Indexing 9,800+ nodes and 250,000+ wires in ~1 second.
* **Hotload-Safe:** Automatically rebuilds and fires `CodeAnalysis.OnGraphRebuilt` upon C# hotloading in the s&box Editor.