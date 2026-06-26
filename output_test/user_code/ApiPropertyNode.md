---
type: class
namespace: SboxAstGraph.Model
tags:
  - user/logic
---

# ApiPropertyNode

**Namespace:** `SboxAstGraph.Model`  
**Source:** `EngineApiModel.cs`  

---

## Out

*None*

## In

- [[EngineAnalyzer]] ─[References]─> `Property: Values`
- [[EngineAnalyzer]] ─[References]─> `Property: PropertyType`
- [[EngineAnalyzer]] ─[References]─> `Property: Name`
- [[EngineApiParser]] ─[Calls]─> `Method: ContainsKey()`
- [[ApiTypeNode]] ─[References]─> `Property: Properties`
