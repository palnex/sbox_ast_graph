---
type: class
namespace: SboxAstGraph.Model
tags:
  - user/logic
---

# ApiTypeNode

**Namespace:** `SboxAstGraph.Model`  
**Source:** `EngineApiModel.cs`  

---

## Out

- ─[References]─> [[ApiFieldNode]] `Property: Fields`
- ─[References]─> [[ApiPropertyNode]] `Property: Properties`
- ─[References]─> [[ApiMethodNode]] `Property: Methods`

## In

- [[EngineAnalyzer]] ─[References]─> `Property: Registry`
- [[EngineAnalyzer]] ─[References]─> `Property: Value`
- [[EngineAnalyzer]] ─[References]─> `Property: Name`
- [[EngineAnalyzer]] ─[References]─> `Property: Namespace`
- [[EngineAnalyzer]] ─[References]─> `Property: BaseType`
- [[EngineAnalyzer]] ─[References]─> `Property: Properties`
- [[EngineAnalyzer]] ─[References]─> `Property: Fields`
- [[EngineAnalyzer]] ─[References]─> `Property: Methods`
- [[EngineAnalyzer]] ─[Calls]─> `Method: TryGetValue()`
- [[EngineAnalyzer]] ─[References]─> `Property: Values`
- [[EngineAnalyzer]] ─[References]─> `Property: IsValueType`
- [[EngineApiParser]] ─[Calls]─> `Method: TryGetValue()`
- [[EngineApiParser]] ─[References]─> `Property: Fields`
- [[EngineApiParser]] ─[References]─> `Property: Properties`
- [[EngineApiParser]] ─[References]─> `Property: Methods`
- [[GraphExporter]] ─[References]─> `Property: Values`
