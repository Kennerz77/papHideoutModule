using System.Collections.Generic;
using EFT.InventoryLogic;

namespace PaPHideoutModule
{
    /// <summary>
    /// What 115 Infused does to a weapon's numbers. Every weapon gets the standard set unless a mod registered its own
    /// for that template (through <see cref="PaPApi.RegisterBonuses"/>), e.g. a wonder weapon whose damage isn't its
    /// bullets. Looks-only mode switches all of them off, whatever is registered.
    /// </summary>
    public sealed class PaPBonusSet
    {
        /// <summary>Damage multiplier per bullet (1 = unchanged).</summary>
        public float Damage { get; set; } = PaPHideoutModulePlugin.DamageMultiplier;

        /// <summary>Rate of fire multiplier (1 = unchanged).</summary>
        public float FireRate { get; set; } = PaPHideoutModulePlugin.FireRateMultiplier;

        /// <summary>Vertical/back recoil taken off, as a fraction (0 = none).</summary>
        public float Recoil { get; set; } = PaPHideoutModulePlugin.RecoilReduction;

        /// <summary>Sideways recoil taken off, as a fraction (0 = none).</summary>
        public float Sideways { get; set; } = PaPHideoutModulePlugin.HorizontalRecoilReduction;

        /// <summary>The standard 115 bonuses.</summary>
        public static PaPBonusSet Standard => new PaPBonusSet();

        /// <summary>No stat changes at all (the weapon's mod handles its upgraded power itself).</summary>
        public static PaPBonusSet None => new PaPBonusSet { Damage = 1f, FireRate = 1f, Recoil = 0f, Sideways = 0f };
    }

    internal static class PaPBonuses
    {
        private static readonly PaPBonusSet Standard = PaPBonusSet.Standard;
        private static readonly Dictionary<string, PaPBonusSet> ByTemplate = new Dictionary<string, PaPBonusSet>();

        public static bool Has(string templateId) => templateId != null && ByTemplate.ContainsKey(templateId);

        public static void Register(string templateId, PaPBonusSet bonuses)
        {
            if (!string.IsNullOrEmpty(templateId) && bonuses != null)
                ByTemplate[templateId] = bonuses;
        }

        /// <summary>The bonuses for this weapon's template (the standard set unless one was registered).</summary>
        public static PaPBonusSet For(Item weapon) =>
            weapon != null && ByTemplate.Count > 0 && ByTemplate.TryGetValue(weapon.TemplateId.ToString(), out PaPBonusSet set) ? set : Standard;
    }
}
