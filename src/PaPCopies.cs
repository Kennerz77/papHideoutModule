using System.Linq;
using EFT;
using EFT.InventoryLogic;

namespace PaPHideoutModule
{
    /// <summary>
    /// The hideout's weapons mode hands the player a copy of their gear, cloned when the mode starts. When a server
    /// mod changes the real gun during an upgrade or strip (PaPServerApi), the game applies that to the profile's
    /// gun only; this swaps the copy for a fresh clone of it, so the gun in the hands matches.
    /// </summary>
    internal static class PaPCopies
    {
        private static HideoutPlayer Player => PaPPatches.CurrentOwner?.HideoutPlayer;

        /// <summary>The profile's own weapon in the equipment (weapons mode only), or null.</summary>
        public static Weapon Real(string weaponId) =>
            Player?.OriginalInventory?.Inventory?.Equipment?.GetAllItems().FirstOrDefault(i => i.Id == weaponId) as Weapon;

        /// <summary>A fingerprint of the weapon and everything on it: ids, templates and stack counts. 0 if not found.</summary>
        public static int Signature(string weaponId)
        {
            Weapon weapon = Real(weaponId);
            if (weapon == null)
                return 0;
            int signature = 17;
            foreach (Item item in weapon.GetAllItems())
                signature = (signature * 31 + item.Id.GetHashCode()) * 31 + item.TemplateId.GetHashCode() * 7 + item.StackObjectsCount;
            return signature;
        }

        /// <summary>
        /// The server's reply changed the gun (another mod's handler): the copy in the same equipment slot is replaced.
        /// Not while the copy is in the hands; it then catches up the next time weapons mode starts. True if replaced.
        /// </summary>
        public static bool RefreshIfChanged(string weaponId, int before)
        {
            if (before == 0 || Signature(weaponId) == before)
                return false;
            HideoutPlayer player = Player;
            Weapon real = Real(weaponId);
            InventoryController copies = player?.ShootingRangeInventory;
            string slotId = (real?.CurrentAddress?.Container as Slot)?.ID;
            Slot copySlot = slotId == null ? null : copies?.Inventory?.Equipment?.GetAllSlots().FirstOrDefault(s => s.ID == slotId);
            Item copy = copySlot?.ContainedItem;
            if (copy == null || copy.TemplateId != real.TemplateId)
                return false;
            if (player.HandsController?.Item == copy)
            {
                PaPHideoutModulePlugin.Log.LogInfo($"{real.LocalizedName()} changed on the server while in the hands; the copy updates when weapons mode is next entered");
                return false;
            }

            var gone = ItemManipulator.DiscardWithoutRestrictions(copy, copies);
            if (gone.Failed)
            {
                PaPHideoutModulePlugin.Log.LogWarning($"Couldn't take the old weapons-mode copy of {weaponId} out: {gone.Error}");
                return false;
            }
            var added = ItemManipulator.AddWithoutRestrictions(real.CloneItem(), copySlot.CreateItemAddress(), copies);
            if (added.Failed)
                PaPHideoutModulePlugin.Log.LogError($"Couldn't put a fresh weapons-mode copy of {weaponId} back: {added.Error}");
            else
                PaPHideoutModulePlugin.Log.LogInfo($"Weapons-mode copy of {real.LocalizedName()} rebuilt after the server changed it");
            return added.Succeeded;
        }
    }
}
