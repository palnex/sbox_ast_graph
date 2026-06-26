---
type: class
namespace: global
tags:
  - user/logic
---

# GameManager

**Namespace:** `global`  
**Source:** `GameManager-test.cs`  

---

## Out

- ─[References]─> [[GameState]] `Property: CurrentState`
- ─[References]─> [[CombatStats]] `Property: PermanentCombat`
- ─[References]─> [[EconomyStats]] `Property: PermanentEconomy`
- ─[References]─> [[WorldStats]] `Property: PermanentWorld`
- ─[CallsSingleton]─> [[SwarmManager]] `Method: StartRadialDisintegration()`

## In

- [[SwarmManager]] ─[CallsSingleton]─> `Method: AddMoney()`
- [[SwarmManager]] ─[ReferencesSingleton]─> `Property: CurrentState`
- [[TowerComponent]] ─[ReferencesSingleton]─> `Property: GlobalMaxHealth`
- [[TowerComponent]] ─[ReferencesSingleton]─> `Property: GlobalFireRate`
- [[TowerComponent]] ─[ReferencesSingleton]─> `Property: GlobalExtraBullets`
- [[TowerComponent]] ─[ReferencesSingleton]─> `Property: GlobalBulletSpeed`
- [[TowerComponent]] ─[ReferencesSingleton]─> `Property: GlobalBulletRange`
- [[TowerComponent]] ─[ReferencesSingleton]─> `Property: GlobalDamage`
- [[TowerComponent]] ─[ReferencesSingleton]─> `Property: GlobalRadis`
- [[ShopMaster]] ─[ReferencesSingleton]─> `Property: CurrentState`
- [[ShopMaster]] ─[CallsSingleton]─> `Method: StartRun()`
