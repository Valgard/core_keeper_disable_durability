# Patch target — research findings (2026-05-13)

## Source

PermaBreak mod, installed via mod.io into: `~/Library/Application
Support/CrossOver/Bottles/Core
Keeper/drive_c/users/Public/mod.io/5289/mods/3407304_4384035/Scripts/`

Inspected raw C# source (no decompilation needed — PermaBreak ships source).

## Files inspected

- `PermaBreakMod.cs` — mod bootstrap (44 lines)
- `BreakForeverPatch.cs` — Harmony patch (~3.3k, two patch methods)

## Patch target

PermaBreak ships **two separate patches**. The relevant one for our use case is the second:

### Primary target (durability reduction — what we want to skip)

- **Class:** `PlayerController`
- **Method:** `ReduceDurabilityOfEquipment`
- **Method signature (as seen in PermaBreak's postfix):** `(InventoryHandler
  equipmentInventoryHandler, int amountToReduce)`
- **Patch type used by PermaBreak:** `Postfix` (runs after reduction to check if item
  hit 0 and should be salvaged)

### Secondary target (broken-item replacement — NOT needed for our mod)

- **Class:** `InventoryHandler`
- **Method:** `TryReplaceBrokenObject`
- **Patch type used by PermaBreak:** `Prefix`
- **Relevance:** PermaBreak intercepts item replacement (after durability is already 0)
  to force permanent destruction. Since our mod prevents durability from ever reaching
  0, `TryReplaceBrokenObject` will never fire — we do NOT need to patch this.

## HarmonyPatch annotation (verbatim from PermaBreak)

```csharp
// Equipment/armor durability reduction (our target):
[HarmonyPatch(typeof(PlayerController), "ReduceDurabilityOfEquipment")]
[HarmonyPostfix]

// Tool replacement after break (NOT needed for our mod):
[HarmonyPatch(typeof(InventoryHandler), "TryReplaceBrokenObject")]
[HarmonyPrefix]
```

Both live inside a class decorated with a bare `[HarmonyPatch]` at class level (see full
file header below).

## Patch method signature (PermaBreak's body)

```csharp
// The postfix PermaBreak uses — fires after ReduceDurabilityOfEquipment has already run
[HarmonyPatch(typeof(PlayerController), "ReduceDurabilityOfEquipment")]
[HarmonyPostfix]
static void BreakEquipmentOnNoDurability(InventoryHandler equipmentInventoryHandler, int amountToReduce)
{
    ObjectDataCD objectData = equipmentInventoryHandler.GetObjectData(0);
    if (objectData.objectID != ObjectID.None && PugDatabase.HasComponent<DurabilityCD>(objectData.objectID))
    {
        if (objectData.amount <= 0)
        {
            equipmentInventoryHandler.Salvage(0, equipmentInventoryHandler,
                API.Client.LocalPlayer.transform.position - API.Rendering.RenderOffset);
        }
    }
}
```

Parameters confirmed from inspection: `(InventoryHandler equipmentInventoryHandler, int
amountToReduce)`. The method returns `void` (postfix). Our prefix should match the same
parameter list.

## Mod bootstrap pattern (PermaBreakMod.cs)

```csharp
using PugMod;
using UnityEngine;

public class PermaBreakMod : IMod
{
    public string version = "1.0.0";
    public void EarlyInit()
    {
        Debug.Log($"Loading mod: PermaBreak [v{version}]");
        Debug.Log("Bringin' back alpha =)");
    }
    public void Init() { }
    public void ModObjectLoaded(Object obj) { }
    public void Shutdown() { }
    public void Update() { }
}
```

The mod implements `IMod` (from `PugMod`) with five lifecycle methods. The Harmony
patches themselves do **not** need to be manually registered in `EarlyInit`/`Init` — the
PugMod loader discovers `[HarmonyPatch]` classes automatically. The `PermaBreakMod.cs`
bootstrap is purely for logging and optional per-frame logic.

## Our adapted patch

We will write a `Prefix` returning `false` to skip the original
`PlayerController.ReduceDurabilityOfEquipment` entirely.

From inspection of PermaBreak: `ReduceDurabilityOfEquipment` is called by
`PlayerController` to reduce the durability of the currently-equipped item by
`amountToReduce`. The postfix PermaBreak adds checks `objectData.amount <= 0` (the
durability field after reduction), so the method's primary — and seemingly only —
responsibility is to decrement durability. The sole side-effect PermaBreak exploits is
that the item's `amount` field reflects the new durability value after the call; there
is no evidence of animation triggers, network sync calls, or audio cues inside this
method (PermaBreak's postfix has no such suppression). Skipping it with a Prefix
returning `false` is safe for our use case: items simply never lose durability, and
`TryReplaceBrokenObject` is never reached.

Note: PermaBreak also patches `InventoryHandler.TryReplaceBrokenObject` (Prefix, void)
to intercept the moment a tool breaks. We do not need this — since our prefix prevents
the decrement, the "broken" state is never triggered.

## Alternatives considered

- Strategy 2 (system OnUpdate): Ruled out because the patch target is a well-scoped
  method (`ReduceDurabilityOfEquipment`) that covers the exact decrement step. A
  system-level hook would be broader and less precise.
- Strategy 3 (multiple call sites): Ruled out because PermaBreak demonstrates that a
  single method (`ReduceDurabilityOfEquipment` for equipment, `TryReplaceBrokenObject`
  for tools) is the correct chokepoint. We only need the former.

## What our Task 5 must include

- `[HarmonyPatch(typeof(PlayerController), "ReduceDurabilityOfEquipment")]` annotation
- `[HarmonyPrefix]` attribute on the patch method
- `using` statement: `PlayerController` is in the root/global namespace (no explicit
  namespace seen in PermaBreak — consistent with Core Keeper's ECS codegen pattern where
  generated types are unnamespaced)
- Our prefix method signature:
  ```csharp
  static bool Prefix() => false;
  ```
  A bare `static bool Prefix() => false;` is sufficient — Harmony does not require the original method's parameters in a prefix unless you need to read/modify them. Since we're unconditionally skipping the original, no parameters are needed.
- If Harmony binding fails (unlikely), fall back to including the full parameter list:
  ```csharp
  static bool Prefix(InventoryHandler equipmentInventoryHandler, int amountToReduce) => false;
  ```
- The class must also implement `IMod` OR be a standalone `[HarmonyPatch]` class.
  PermaBreak separates the two — bootstrap in `PermaBreakMod.cs`, patch in
  `BreakForeverPatch.cs`. We should follow the same pattern.

## Confidence

**High.** The durability reduction method is unambiguously identified:
`PlayerController.ReduceDurabilityOfEquipment`. PermaBreak's postfix directly inspects
`objectData.amount` (the durability field) after this call, confirming it is the sole
durability-decrement site for equipment. The `HarmonyPatch` annotation uses
`typeof(PlayerController)` with a string method name — no ambiguity. The patch type
(Postfix in PermaBreak, Prefix for us) is clear and compatible with a
Prefix-returns-false strategy.
