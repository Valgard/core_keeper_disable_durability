using ModSettingsMenu.Settings;
using PlayerEquipment;
using PugMod;
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

            ModSettings.Section(this).Toggle(out var en, "enabled", true).Build();
            ModConfig.Instance.Bind(en);

            Debug.Log($"[DisableDurability] Mod initialized. Enabled={ModConfig.Instance.enabled}");
        }

        public void ModObjectLoaded(Object obj) { }

        public void Shutdown() { }

        public void Update() { }
    }
}
