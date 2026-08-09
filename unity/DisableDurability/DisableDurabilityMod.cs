using ModSettingsMenu.Settings;
using PlayerEquipment;
using PugMod;
using Unity.Entities;
using UnityEngine;

namespace DisableDurability
{
    /// <summary>
    /// Mod bootstrap. The Pugstorm mod loader instantiates this class on
    /// game start and calls the IMod lifecycle methods.
    ///
    /// The <see cref="BurstDisabler"/> call in <see cref="Init"/> is required
    /// because Harmony cannot patch Burst-compiled job entry points. By
    /// disabling Burst for the <see cref="ChangeDurabilitySystem"/> group,
    /// the system's managed <c>OnUpdate</c> method becomes patchable.
    /// </summary>
    public sealed class DisableDurabilityMod : IMod
    {
        public void EarlyInit() { }

        public void Init()
        {
            BurstDisabler.DisableBurstForSystem<ChangeDurabilitySystem>();

            // Registering the system is only half the job: the Burst bypass is
            // armed per world by BurstDisabler.AddWorld, whose sole caller is
            // ECSManager.StartEcs, and which snapshots the systems registered
            // up to that moment. A dedicated server runs IMod.Init() *after*
            // StartEcs, so that snapshot was taken while our registration was
            // still missing, ChangeDurabilitySystem.OnUpdate kept running
            // through the Burst path and the prefix was never reached. Re-run
            // it for the worlds that exist by now. The registry is a set, so
            // this is a no-op wherever Init() ran first (singleplayer, client).
            //
            // EarlyInit is not an option: TypeManager is not initialised yet
            // there, and DisableBurstForSystem throws NullReferenceException.
            foreach (var world in World.All)
                BurstDisabler.AddWorld(world);

            ModSettings.Section(this).Toggle(out var en, "enabled", true).Build();
            ModConfig.Instance.Bind(en);

            Debug.Log($"[DisableDurability] Mod initialized. Enabled={ModConfig.Instance.enabled}");
        }

        public void ModObjectLoaded(Object obj) { }

        public void Shutdown() { }

        public void Update() { }
    }
}
