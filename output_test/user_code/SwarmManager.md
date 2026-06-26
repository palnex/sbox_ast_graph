---
type: class
namespace: global
tags:
  - user/logic
---

# SwarmManager

**Namespace:** `global`  
**Source:** `SwarmManager-test.cs`  

---

## Out

- ─[References]─> [[SwarmRenderObject]] `Field: _renderObject`
- ─[CallsSingleton]─> [[GameManager]] `Method: AddMoney()`
- ─[ReferencesSingleton]─> [[GameManager]] `Property: CurrentState`

## In

- [[GameManager]] ─[CallsSingleton]─> `Method: StartRadialDisintegration()`
- [[SwarmRenderObject]] ─[References]─> `Field: _manager`
- [[SwarmRenderObject]] ─[ReferencesSingleton]─> `Property: UnitModel`
- [[SwarmRenderObject]] ─[ReferencesSingleton]─> `Field: _renderCount`
- [[SwarmRenderObject]] ─[ReferencesSingleton]─> `Field: _renderTransforms`
- [[SwarmRenderObject]] ─[ReferencesSingleton]─> `Field: _renderAttributes`
- [[TowerComponent]] ─[ReferencesSingleton]─> `Property: Target`
- [[TowerComponent]] ─[CallsSingleton]─> `Method: CheckBulletCollision()`
- [[TowerComponent]] ─[CallsSingleton]─> `Method: GetUnitHealth()`
- [[TowerComponent]] ─[CallsSingleton]─> `Method: DamageUnit()`
- [[TowerComponent]] ─[CallsSingleton]─> `Method: GetUnitPosition()`
- [[TowerComponent]] ─[CallsSingleton]─> `Method: IsUnitAlive()`
