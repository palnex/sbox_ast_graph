---
type: class
namespace: SboxAstGraph.Model
tags:
  - user/logic
---

# ApiParameterNode

**Namespace:** `SboxAstGraph.Model`  
**Source:** `EngineApiModel.cs`  

---

## Out

*None*

## In

- [[EngineAnalyzer]] ─[References]─> `Property: ParameterType`
- [[EngineAnalyzer]] ─[References]─> `Property: Name`
- [[EngineApiParser]] ─[Calls]─> `Method: Add()`
- [[ApiMethodNode]] ─[References]─> `Property: Parameters`
