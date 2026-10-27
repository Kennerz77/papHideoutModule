using System;
using System.Collections.Generic;
using EFT.InventoryLogic;
using UnityEngine;

namespace PaPHideoutModule
{
    /// <summary>
    /// Public API for other client mods. Reference PaPHideoutModule.dll, add
    /// <c>[BepInDependency("com.kennerz.paphideoutmodule")]</c> (soft or hard), and call these from the main thread.
    /// The server mod is the source of truth for which weapons are infused; this reads the client's synced copy.
    /// </summary>
    public static class PaPApi
    {
        public const string PluginGuid = PaPHideoutModulePlugin.PluginGuid;

        /// <summary>Raised on the main thread whenever the set of infused weapons (or a weapons-mode copy of one) changes.</summary>
        public static event Action InfusedChanged;

        /// <summary>
        /// Raised on the main thread the moment a weapon comes back out of the machine, upgraded (the profile's weapon,
        /// not the weapons-mode copy). For a mod weapon with its own Pack-a-Punched form, this is the time to switch to it.
        /// </summary>
        public static event Action<Item> WeaponUpgraded;

        /// <summary>False when the player chose "Looks and sound only": infused guns then get no damage, fire rate or recoil bonuses.</summary>
        public static bool BonusesActive => PaPHideoutModulePlugin.Bonuses;

        /// <summary>
        /// Gives a weapon template its own Pack-a-Punch shot sound (and optionally its own tail), in place of the layer
        /// picked by weapon class. For mod weapons with a BO3 PaP sound of their own (the Ray Gun's, the Thundergun's).
        /// </summary>
        public static void RegisterShotSound(string templateId, AudioClip shot, AudioClip tail = null) =>
            PaPShotAudio.Register(templateId, shot, tail);

        /// <summary>
        /// The weapon template keeps its own look when infused: no BO1 camo on it or on what sits on it. For mod weapons
        /// whose upgraded look the mod handles itself (listen to <see cref="WeaponUpgraded"/> or check <see cref="IsInfused(Item)"/>).
        /// </summary>
        public static void ExcludeFromCamo(string templateId) => PaPCamo.Exclude(templateId);

        /// <summary>
        /// The weapon template's own name when infused (e.g. "Zeus Cannon"), in place of the "(115 Infused)" suffix.
        /// <paramref name="shortName"/> is used where the game shows short names; the name itself if left out.
        /// </summary>
        public static void RegisterName(string templateId, string name, string shortName = null)
        {
            if (!string.IsNullOrEmpty(templateId) && !string.IsNullOrEmpty(name))
                PaPPatches.UpgradedNames[templateId] = (name, shortName ?? name);
        }

        /// <summary>
        /// The weapon template's own 115 stat bonuses in place of the standard ones: say <see cref="PaPBonusSet.None"/>
        /// for a wonder weapon whose upgraded power its mod handles, or a set of your own. Looks-only mode still
        /// switches them all off.
        /// </summary>
        public static void RegisterBonuses(string templateId, PaPBonusSet bonuses) => PaPBonuses.Register(templateId, bonuses);

        /// <summary>The bonuses an infused weapon gets (the standard set unless its template registered its own).</summary>
        public static PaPBonusSet BonusesFor(Item weapon) => PaPBonuses.For(weapon);

        /// <summary>True if the item is a 115 Infused weapon, including the hideout's weapons-mode copy of one.</summary>
        public static bool IsInfused(Item item) => item is Weapon && PaPRegistry.IsInfused(item.Id);

        /// <summary>True if the item id is infused (or is the weapons-mode copy of an infused weapon).</summary>
        public static bool IsInfused(string itemId) => PaPRegistry.IsInfused(itemId);

        /// <summary>The profile id behind a hideout weapons-mode copy; any other id comes back unchanged.</summary>
        public static string OriginalId(string itemId) => PaPRegistry.OriginalId(itemId);

        /// <summary>Ids of every infused weapon in the current profile.</summary>
        public static IReadOnlyCollection<string> InfusedIds => PaPRegistry.All;

        /// <summary>The 115 colour (inventory glow, stat bonuses).</summary>
        public static Color InfusedColor => PaPHideoutModulePlugin.GlowColor;

        /// <summary>The standard damage multiplier for every bullet fired from an infused weapon (see <see cref="BonusesFor"/>).</summary>
        public static float DamageMultiplier => PaPHideoutModulePlugin.DamageMultiplier;

        /// <summary>Rate of fire multiplier of infused weapons.</summary>
        public static float FireRateMultiplier => PaPHideoutModulePlugin.FireRateMultiplier;

        /// <summary>Recoil taken off infused weapons, as a fraction (0.06 = 6% less).</summary>
        public static float RecoilReduction => PaPHideoutModulePlugin.RecoilReduction;

        /// <summary>Sideways recoil taken off infused weapons, as a fraction (0.5 = half).</summary>
        public static float HorizontalRecoilReduction => PaPHideoutModulePlugin.HorizontalRecoilReduction;

        /// <summary>Paints the Pack-a-Punch camo on a weapon model if <paramref name="item"/> is infused, or removes it if not.</summary>
        public static void ApplyCamo(GameObject model, Item item) => PaPCamo.Apply(model, item);

        /// <summary>True for the "115 Riddles" rock fragments (the quest items and the ones Mechanic sends back).</summary>
        public static bool IsFragment(Item item) => PaPFragments.Is(item);

        /// <summary>
        /// Turns a model of a fragment (built from the bronze watch it was cloned from) into its glowing 115 rock;
        /// <paramref name="live"/> adds the pulse, light and hum. Does nothing for other items.
        /// </summary>
        public static void DressFragment(GameObject model, Item item, bool live = true) => PaPFragments.Dress(model, item, live);

        /// <summary>Asks the server to re-send the infused list (e.g. after your server mod infused something).</summary>
        public static void Refresh() => PaPRegistry.RefreshAsync();

        /// <summary>
        /// Removes the 115 powers from a weapon in the player's profile (free). The callback gets true on success,
        /// on the main thread.
        /// </summary>
        public static void Strip(string itemId, Action<bool> done = null)
        {
            string id = OriginalId(itemId);
            int before = PaPCopies.Signature(id);
            PaPServer.Send(PaPServer.Strip, id, result =>
            {
                PaPRegistry.Refresh();
                PaPCopies.RefreshIfChanged(id, before);
                done?.Invoke(!PaPRegistry.IsInfused(id));
            });
        }

        internal static void RaiseChanged() => Raise(InfusedChanged, handler => ((Action)handler)());

        internal static void RaiseUpgraded(Item weapon) => Raise(WeaponUpgraded, handler => ((Action<Item>)handler)(weapon));

        // One listener failing mustn't stop the others (or the machine)
        private static void Raise(Delegate handlers, Action<Delegate> call)
        {
            if (handlers == null)
                return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    call(handler);
                }
                catch (Exception e)
                {
                    PaPHideoutModulePlugin.Log.LogError($"A PaP API listener failed: {e}");
                }
            }
        }
    }
}
