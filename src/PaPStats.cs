using System;
using System.Linq;
using System.Runtime.CompilerServices;
using EFT.InventoryLogic;
using UnityEngine;

namespace PaPHideoutModule
{
    /// <summary>
    /// Shows the 115 bonuses in the weapon stats panel in the 115 colour, like attachment modifiers:
    /// fire rate "800 RPM (+120)", recoil "(-6)", and extra "115 DAMAGE +17%" / "115 SIDEWAYS RECOIL -50%" rows.
    /// The value texts are wrapped once per attribute and check the infused state every time they're drawn,
    /// so stripping a weapon puts its plain numbers straight back.
    /// </summary>
    internal static class PaPStats
    {
        private const string DamageRowName = "115 DAMAGE";
        private const string SidewaysRowName = "115 SIDEWAYS RECOIL";

        // Attributes already wrapped (the value is just a marker)
        private static readonly ConditionalWeakTable<ItemAttribute, object> Wrapped = new ConditionalWeakTable<ItemAttribute, object>();

        /// <summary>Called right before the stats panel shows an item.</summary>
        public static void Sync(Item item)
        {
            if (!(item is Weapon weapon) || weapon.Attributes == null)
                return;

            foreach (ItemAttribute attribute in weapon.Attributes)
                Wrap(weapon, attribute);

            // Looks-only mode has no bonuses to show
            bool infused = PaPHideoutModulePlugin.Bonuses && PaPRegistry.IsInfused(weapon.Id);
            // A weapon registered with its own bonuses shows only the ones it has
            PaPBonusSet bonuses = PaPBonuses.For(weapon);
            SyncRow(weapon, infused && bonuses.Damage != 1f, DamageRowName, () => (PaPBonuses.For(weapon).Damage - 1f) * 100f, "+{0}% per bullet");
            SyncRow(weapon, infused && bonuses.Sideways > 0f, SidewaysRowName, () => PaPBonuses.For(weapon).Sideways * 100f, "-{0}%");
        }

        private static void SyncRow(Weapon weapon, bool infused, string name, Func<float> percent, string format)
        {
            ItemAttribute row = weapon.Attributes.FirstOrDefault(a => a.Name == name);
            if (infused && row == null)
                weapon.Attributes.Add(Row(name, percent, format));
            else if (!infused && row != null)
                weapon.Attributes.Remove(row);
        }

        private static void Wrap(Weapon weapon, ItemAttribute attribute)
        {
            if (!(attribute.Id is EItemAttributeId id) || attribute.StringValue == null)
                return;
            if (id != EItemAttributeId.FireRate && id != EItemAttributeId.RecoilUp && id != EItemAttributeId.RecoilBack)
                return;
            if (Wrapped.TryGetValue(attribute, out _))
                return;

            Wrapped.Add(attribute, null);
            Func<string> original = attribute.StringValue;
            attribute.StringValue = () => Decorate(weapon, id, original());
        }

        private static string Decorate(Weapon weapon, EItemAttributeId id, string text)
        {
            if (!PaPHideoutModulePlugin.Bonuses || !PaPRegistry.IsInfused(weapon.Id))
                return text;
            PaPBonusSet bonuses = PaPBonuses.For(weapon);

            switch (id)
            {
                case EItemAttributeId.FireRate when bonuses.FireRate != 1f:
                    // The row reads the template's rate; show the boosted one and what the 115 added
                    int rate = weapon.Template.bFirerate;
                    int boosted = Mathf.RoundToInt(rate * bonuses.FireRate);
                    return ReplaceFirst(text, rate.ToString(), boosted.ToString()) + Bonus($"+{boosted - rate}");

                // These rows already include the 115 reduction (they read Weapon.RecoilDelta); just mark its share
                case EItemAttributeId.RecoilUp when bonuses.Recoil > 0f:
                    return text + Bonus($"-{Mathf.RoundToInt(weapon.Template.RecoilForceUp * bonuses.Recoil)}");
                case EItemAttributeId.RecoilBack when bonuses.Recoil > 0f:
                    return text + Bonus($"-{Mathf.RoundToInt(weapon.Template.RecoilForceBack * bonuses.Recoil)}");
                default:
                    return text;
            }
        }

        private static ItemAttribute Row(string name, Func<float> percent, string format)
        {
            return new ItemAttribute(EItemAttributeId.Undefined)
            {
                Name = name,
                DisplayNameFunc = () => name,
                Base = percent,
                StringValue = () => Colored(string.Format(format, Mathf.RoundToInt(percent()))),
                DisplayType = () => EItemAttributeDisplayType.Compact,
            };
        }

        private static string Bonus(string value) => " " + Colored($"({value})");

        private static string Colored(string text) =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(PaPHideoutModulePlugin.GlowColor)}>{text}</color>";

        private static string ReplaceFirst(string text, string find, string replace)
        {
            int at = text.IndexOf(find, StringComparison.Ordinal);
            return at < 0 ? text : text.Substring(0, at) + replace + text.Substring(at + find.Length);
        }
    }
}
