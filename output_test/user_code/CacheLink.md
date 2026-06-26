---
type: class
namespace: SboxAstGraph.Analysis
tags:
  - user/logic
---

# CacheLink

**Namespace:** `SboxAstGraph.Analysis`  
**Source:** `QueryEngine.cs`  

---

## Out

*None*

## In

- [[CacheGraph]] ─[References]─> `Property: links`
- [[QueryEngine]] ─[References]─> `Field: _adjacencyList`
- [[QueryEngine]] ─[Calls]─> `Method: ContainsKey()`
- [[QueryEngine]] ─[References]─> `Property: source`
- [[QueryEngine]] ─[Calls]─> `Method: Add()`
- [[QueryEngine]] ─[Calls]─> `Method: TryGetValue()`
- [[QueryEngine]] ─[References]─> `Property: target`
- [[QueryEngine]] ─[Calls]─> `Method: Reverse()`
- [[QueryEngine]] ─[References]─> `Property: Count`
- [[QueryEngine]] ─[References]─> `Property: type`
- [[QueryEngine]] ─[References]─> `Property: details`
