using System;
using System.Reflection;
using EFT;
using EFT.InputSystem;
using EFT.InventoryLogic;
using HarmonyLib;

namespace PaPHideoutModule
{
    /// <summary>
    /// Weapons in the hideout come from Hideout Uncensored (a hard dependency): it keeps the player armed anywhere,
    /// lifts the aiming restriction and resets stuck hands. Its session decides whether weapons should be out
    /// ("Want") and moves the game there every frame, so the machine asks it instead of switching modes itself;
    /// holstering behind its back would just be undone on the next frame.
    /// </summary>
    internal static class PaPUncensored
    {
        public const string Guid = "com.trappuss.hideoutuncensored";

        private static PropertyInfo _want;
        private static MethodInfo _onWeaponKey;
        private static bool _looked;

        private static bool Ready()
        {
            if (!_looked)
            {
                _looked = true;
                Type session = AccessTools.TypeByName("HideoutUncensored.HideoutSession");
                _want = session == null ? null : AccessTools.Property(session, "Want");
                _onWeaponKey = session == null ? null : AccessTools.Method(session, "OnWeaponKey");
                if (_want == null || _want.GetSetMethod(true) == null)
                    PaPHideoutModulePlugin.Log.LogError("Hideout Uncensored's session was not found (renamed in an update?); the machine can't get your weapons out.");
            }
            return _want != null && _want.GetSetMethod(true) != null;
        }

        /// <summary>Hideout Uncensored will draw (true) or holster (false) as soon as the game allows.</summary>
        public static void SetArmed(bool armed)
        {
            if (Ready())
                _want.GetSetMethod(true).Invoke(null, new object[] { armed });
        }

        /// <summary>Draws with <paramref name="weapon"/> in hand, as if its slot key was pressed.</summary>
        public static void DrawWith(HideoutPlayerOwner owner, Weapon weapon)
        {
            ECommand command = SlotCommand(weapon);
            if (command != ECommand.None && _onWeaponKey != null && owner != null)
            {
                try
                {
                    _onWeaponKey.Invoke(null, new object[] { owner, command });
                }
                catch (Exception e)
                {
                    PaPHideoutModulePlugin.Log.LogWarning($"Drawing by slot failed, drawing normally: {e.GetBaseException().Message}");
                }
            }
            SetArmed(true);
        }

        private static ECommand SlotCommand(Weapon weapon)
        {
            string slot = (weapon?.CurrentAddress?.Container as Slot)?.ID;
            switch (slot)
            {
                case "FirstPrimaryWeapon": return ECommand.SelectFirstPrimaryWeapon;
                case "SecondPrimaryWeapon": return ECommand.SelectSecondPrimaryWeapon;
                case "Holster": return ECommand.SelectSecondaryWeapon;
                default: return ECommand.None;
            }
        }
    }
}
