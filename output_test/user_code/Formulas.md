---
type: class
namespace: global
tags:
  - user/logic
---

# Formulas

**Namespace:** `global`  
**Source:** `Formulas-test.cs`  

---

## Out

- ─[References]─> [[StatDefinition]] `Property: CardProgression`
- ─[Calls]─> [[StatRegistry]] `Method: Get()`
- ─[References]─> [[StatDefinition]] `Property: CardParams`
- ─[References]─> [[StatDefinition]] `Property: UnlockCost`
- ─[References]─> [[StatDefinition]] `Property: PriceThresholds`
- ─[References]─> [[StatDefinition]] `Property: ShopParams`
- ─[References]─> [[StatDefinition]] `Property: ShopProgression`
- ─[Calls]─> [[ProgressionMath]] `Method: GetLinearBulkCost()`
- ─[Calls]─> [[ProgressionMath]] `Method: GetQuadraticBulkCost()`
- ─[Calls]─> [[ProgressionMath]] `Method: GetGeometricBulkCost()`
- ─[Calls]─> [[ProgressionMath]] `Method: GetLinearAffordableLevels()`
- ─[Calls]─> [[ProgressionMath]] `Method: GetQuadraticAffordableLevels()`
- ─[Calls]─> [[ProgressionMath]] `Method: GetGeometricAffordableLevels()`

## In

- [[UpgradeNode]] ─[Calls]─> `Method: GetRarityUpgradeCost()`
- [[UpgradeNode]] ─[Calls]─> `Method: GetCardStatBulkCost()`
- [[UpgradeNode]] ─[Calls]─> `Method: GetCardStatAffordableLevels()`
- [[UpgradeNode]] ─[Calls]─> `Method: GetShopStatBulkCost()`
- [[UpgradeNode]] ─[Calls]─> `Method: GetShopStatAffordableLevels()`
- [[UpgradeNode]] ─[Calls]─> `Method: GetBulkCost()`
- [[UpgradeNode]] ─[Calls]─> `Method: GetAffordableLevels()`
