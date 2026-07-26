using HarmonyLib;
using PlayerEquipment;
using UnityEngine;

namespace DisableDurability
{
    /// <summary>
    /// Harmony Prefix that suppresses the ECS system responsible for changing
    /// item durability. Target identified by inspecting Pug.Other.dll exports:
    /// <c>PlayerEquipment.ChangeDurabilitySystem</c> is the SystemBase that
    /// schedules <c>ChangeDurabilityOfHeldEquipmentJob</c> and
    /// <c>ReduceDurabilityOfAllEquipmentJob</c> each tick. Skipping its
    /// <c>OnUpdate</c> prevents both jobs from being scheduled.
    ///
    /// Note: the original research note pointed at
    /// <c>PlayerController.ReduceDurabilityOfEquipment</c> (matching PermaBreak's
    /// source), but that method no longer exists in the current Core Keeper
    /// build. The system-level patch is the actual mechanism.
    ///
    /// Burst-compiled jobs cannot themselves be patched by Harmony, but the
    /// system's <c>OnUpdate</c> is regular managed code. See
    /// <c>DisableDurabilityMod.Init</c> for the BurstDisabler workaround.
    /// </summary>
    [HarmonyPatch(typeof(ChangeDurabilitySystem), "OnUpdate")]
    [HarmonyPriority(Priority.Last)]
    public static class NoDurabilityLossPatch
    {
        static NoDurabilityLossPatch()
        {
            Debug.Log($"[DisableDurability] Patch loaded. " + $"Enabled={ModConfig.Instance.enabled}");
        }

        [HarmonyPrefix]
        private static bool Prefix()
        {
            if (!ModConfig.Instance.enabled)
                return true; // run original
            return false; // skip original
        }
    }
}
