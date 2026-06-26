---
type: class
namespace: SboxAstGraph.Analysis
tags:
  - user/logic
---

# EngineApiParser

**Namespace:** `SboxAstGraph.Analysis`  
**Source:** `EngineApiParser.cs`  

---

## Out

- ─[Calls]─> [[ApiTypeNode]] `Method: TryGetValue()`
- ─[Calls]─> [[ApiFieldNode]] `Method: ContainsKey()`
- ─[References]─> [[ApiTypeNode]] `Property: Fields`
- ─[Calls]─> [[ApiPropertyNode]] `Method: ContainsKey()`
- ─[References]─> [[ApiTypeNode]] `Property: Properties`
- ─[Calls]─> [[ApiMethodNode]] `Method: ContainsKey()`
- ─[References]─> [[ApiTypeNode]] `Property: Methods`
- ─[Calls]─> [[ApiParameterNode]] `Method: Add()`
- ─[References]─> [[ApiMethodNode]] `Property: Parameters`

## In

- [[EngineAnalyzer]] ─[Calls]─> `Method: Parse()`
