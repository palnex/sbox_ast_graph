---
type: class
namespace: global
tags:
  - user/logic
---

# StatRegistry

**Namespace:** `global`  
**Source:** `StatRegistry-test.cs`  

---

## Out

- ─[References]─> [[StatType]] `Field: Stats`
- ─[References]─> [[StatDefinition]] `Field: Stats`
- ─[References]─> [[StatType]] `Property: Count`
- ─[References]─> [[StatDefinition]] `Property: Count`
- ─[Calls]─> [[StatType]] `Method: Clear()`
- ─[Calls]─> [[StatDefinition]] `Method: Clear()`
- ─[References]─> [[StatType]] `Property: Values`
- ─[References]─> [[StatDefinition]] `Property: Values`
- ─[References]─> [[StatDefinition]] `Property: PriceThresholds`
- ─[References]─> [[StatDefinition]] `Property: Type`
- ─[Calls]─> [[StatType]] `Method: TryGetValue()`
- ─[Calls]─> [[StatDefinition]] `Method: TryGetValue()`
- ─[References]─> [[StatDefinition]] `Property: CardParams`
- ─[References]─> [[StatDefinition]] `Property: ShopParams`

## In

- [[Formulas]] ─[Calls]─> `Method: Get()`
- [[GameMetadata]] ─[Calls]─> `Method: Get()`
