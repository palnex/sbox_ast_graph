---
type: class
namespace: global
tags:
  - user/logic
---

# StatDefinition

**Namespace:** `global`  
**Source:** `StatRegistry-test.cs`  

---

## Out

- ─[References]─> [[StatType]] `Property: Type`
- ─[References]─> [[ShopTab]] `Property: Tab`
- ─[References]─> [[ProgressionType]] `Property: CardProgression`
- ─[References]─> [[ProgressionType]] `Property: ShopProgression`

## In

- [[Formulas]] ─[References]─> `Property: CardProgression`
- [[Formulas]] ─[References]─> `Property: CardParams`
- [[Formulas]] ─[References]─> `Property: UnlockCost`
- [[Formulas]] ─[References]─> `Property: PriceThresholds`
- [[Formulas]] ─[References]─> `Property: ShopParams`
- [[Formulas]] ─[References]─> `Property: ShopProgression`
- [[GameMetadata]] ─[References]─> `Property: FriendlyName`
- [[GameMetadata]] ─[References]─> `Property: FlatUnit`
- [[StatRegistry]] ─[References]─> `Field: Stats`
- [[StatRegistry]] ─[References]─> `Property: Count`
- [[StatRegistry]] ─[Calls]─> `Method: Clear()`
- [[StatRegistry]] ─[References]─> `Property: Values`
- [[StatRegistry]] ─[References]─> `Property: PriceThresholds`
- [[StatRegistry]] ─[References]─> `Property: Type`
- [[StatRegistry]] ─[Calls]─> `Method: TryGetValue()`
- [[StatRegistry]] ─[References]─> `Property: CardParams`
- [[StatRegistry]] ─[References]─> `Property: ShopParams`
