---
type: class
namespace: SboxAstGraph.Model
tags:
  - user/logic
---

# ApiMethodNode

**Namespace:** `SboxAstGraph.Model`  
**Source:** `EngineApiModel.cs`  

---

## Out

- ─[References]─> [[ApiParameterNode]] `Property: Parameters`

## In

- [[EngineAnalyzer]] ─[References]─> `Property: Values`
- [[EngineAnalyzer]] ─[References]─> `Property: ReturnType`
- [[EngineAnalyzer]] ─[References]─> `Property: Name`
- [[EngineAnalyzer]] ─[References]─> `Property: Parameters`
- [[EngineApiParser]] ─[Calls]─> `Method: ContainsKey()`
- [[EngineApiParser]] ─[References]─> `Property: Parameters`
- [[ApiTypeNode]] ─[References]─> `Property: Methods`
