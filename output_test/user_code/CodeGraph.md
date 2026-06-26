---
type: class
namespace: SboxAstGraph.Model
tags:
  - user/logic
---

# CodeGraph

**Namespace:** `SboxAstGraph.Model`  
**Source:** `CodeGraph.cs`  

---

## Out

- ─[References]─> [[CodeNode]] `Property: Nodes`
- ─[References]─> [[CodeEdge]] `Property: Edges`
- ─[Calls]─> [[CodeNode]] `Method: ContainsKey()`
- ─[Calls]─> [[CodeEdge]] `Method: Exists()`
- ─[References]─> [[CodeEdge]] `Property: Source`
- ─[References]─> [[CodeEdge]] `Property: Target`
- ─[References]─> [[CodeEdge]] `Property: Type`
- ─[References]─> [[CodeEdge]] `Property: Details`
- ─[Calls]─> [[CodeEdge]] `Method: Add()`

## In

- [[CodeAnalyzer]] ─[Calls]─> `Method: AddEdge()`
- [[CodeAnalyzer]] ─[References]─> `Property: Nodes`
- [[CodeAnalyzer]] ─[References]─> `Property: Edges`
- [[EngineAnalyzer]] ─[Calls]─> `Method: AddNode()`
- [[EngineAnalyzer]] ─[References]─> `Property: Nodes`
- [[EngineAnalyzer]] ─[References]─> `Property: Edges`
- [[EngineAnalyzer]] ─[Calls]─> `Method: AddEdge()`
- [[SemanticWalker]] ─[References]─> `Field: _graph`
- [[SemanticWalker]] ─[Calls]─> `Method: AddNode()`
- [[SemanticWalker]] ─[Calls]─> `Method: AddEdge()`
- [[GraphExporter]] ─[References]─> `Property: Nodes`
- [[GraphExporter]] ─[References]─> `Property: Edges`
