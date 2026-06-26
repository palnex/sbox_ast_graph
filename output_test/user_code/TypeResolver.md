---
type: class
namespace: SboxAstGraph.Analysis
tags:
  - user/logic
---

# TypeResolver

**Namespace:** `SboxAstGraph.Analysis`  
**Source:** `TypeResolver.cs`  

---

## Out

- ─[References]─> [[Regex]] `Field: MetadataCleanupRegex`
- ─[References]─> [[TypeSignature]] `Property: IsByRef`
- ─[References]─> [[TypeSignature]] `Property: RawName`
- ─[References]─> [[TypeSignature]] `Property: IsArray`
- ─[References]─> [[TypeSignature]] `Property: IsPointer`
- ─[References]─> [[TypeSignature]] `Property: FullName`
- ─[Calls]─> [[TypeSignature]] `Method: Add()`
- ─[References]─> [[TypeSignature]] `Property: GenericArguments`
- ─[References]─> [[TypeSignature]] `Property: CleanName`

## In

- [[EngineAnalyzer]] ─[Calls]─> `Method: Parse()`
- [[StubGenerator]] ─[Calls]─> `Method: Parse()`
