using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Enums.Hideout;
using SPTarkov.Server.Core.Services.InRaid;

namespace PaPHideoutServer;

/// <summary>
/// Server hooks for "115 Riddles". None of these methods are virtual, so they're Harmony patches (SPT's own
/// AbstractPatch wrapper). Each one only touches our quest, and does nothing if the service isn't up.
/// </summary>
internal static class PaPQuestPatches
{
    public static void Enable(Action<string> logError)
    {
        foreach (AbstractPatch patch in new AbstractPatch[]
                 {
                     new ClientQuestsPatch(), new WallOpenedPatch(), new HandoverPatch(),
                     new RaidSidePatch(), new RaidLootPatch(), new StartUpgradePatch(),
                     new DiscardPatch(), new SellPatch(),
                 })
        {
            try
            {
                patch.Enable();
            }
            catch (Exception e)
            {
                // One that no longer fits after an SPT update is logged and skipped, the rest still work
                logError($"[PaP] Patch {patch.GetType().Name} failed and is disabled: {e.GetBaseException().Message}");
            }
        }
    }

    /// <summary>Hides the quest from the list until the profile has been given its status.</summary>
    private sealed class ClientQuestsPatch : AbstractPatch
    {
        protected override MethodBase? GetTargetMethod() => typeof(QuestHelper).GetMethod(nameof(QuestHelper.GetClientQuests));

        [PatchPostfix]
        private static void Postfix(MongoId sessionId, List<Quest> __result) =>
            PaPQuestService.Instance?.FilterClientQuests(sessionId, __result);
    }

    /// <summary>The side room's wall just came down: offer the quest in the same response.</summary>
    private sealed class WallOpenedPatch : AbstractPatch
    {
        protected override MethodBase? GetTargetMethod() => typeof(HideoutController).GetMethod(nameof(HideoutController.UpgradeComplete));

        [PatchPostfix]
        private static void Postfix(PmcData pmcData, HideoutUpgradeCompleteRequestData request, MongoId sessionID, ItemEventRouterResponse output)
        {
            if (request.AreaType == HideoutAreas.EmergencyWall)
                PaPQuestService.Instance?.TryUnlock(pmcData, sessionID, output);
        }
    }

    /// <summary>The 115 rocks can't be thrown away: the item's own flags aren't enough on this client.</summary>
    private sealed class DiscardPatch : AbstractPatch
    {
        protected override MethodBase? GetTargetMethod() => typeof(InventoryController).GetMethod(nameof(InventoryController.DiscardItem));

        [PatchPrefix]
        private static bool Prefix(PmcData pmcData, InventoryRemoveRequestData request, ItemEventRouterResponse output) =>
            PaPQuestService.Instance?.AllowRemove(pmcData, [request.Item], output) ?? true;
    }

    /// <summary>...or sold to a trader.</summary>
    private sealed class SellPatch : AbstractPatch
    {
        protected override MethodBase? GetTargetMethod() => typeof(TradeController).GetMethod(nameof(TradeController.ConfirmTrading));

        [PatchPrefix]
        private static bool Prefix(PmcData pmcData, ProcessBaseTradeRequestData request, ref ItemEventRouterResponse __result)
        {
            if (request is not ProcessSellTradeRequestData sell || sell.Items == null)
                return true;
            var output = new ItemEventRouterResponse { ProfileChanges = [], Warnings = [] };
            if (PaPQuestService.Instance?.AllowRemove(pmcData, sell.Items.Select(i => i.Id).ToList(), output) ?? true)
                return true;
            __result = output;
            return false;
        }
    }

    /// <summary>The machine can only be built once Mechanic's quest is done.</summary>
    private sealed class StartUpgradePatch : AbstractPatch
    {
        protected override MethodBase? GetTargetMethod() => typeof(HideoutController).GetMethod(nameof(HideoutController.StartUpgrade));

        [PatchPrefix]
        private static bool Prefix(PmcData pmcData, HideoutUpgradeRequestData request, ItemEventRouterResponse output) =>
            PaPQuestService.Instance?.AllowUpgrade(pmcData, request, output) ?? true;
    }

    /// <summary>Remembers which fragments went to Mechanic, so a partial hand-in doesn't make them respawn.</summary>
    private sealed class HandoverPatch : AbstractPatch
    {
        protected override MethodBase? GetTargetMethod() => typeof(QuestController).GetMethod(nameof(QuestController.HandoverQuest));

        [PatchPrefix]
        private static void Prefix(PmcData pmcData, HandoverQuestRequestData request, out List<string> __state) =>
            __state = PaPQuestService.Instance?.FragmentsIn(pmcData, request) ?? [];

        [PatchPostfix]
        private static void Postfix(MongoId sessionID, ItemEventRouterResponse __result, List<string> __state)
        {
            if (__result?.Warnings?.Count > 0)
                return;
            PaPQuestService.Instance?.RecordHandedIn(sessionID, __state);
        }
    }

    // Raid loot is made inside StartLocalRaidAsync, which knows PMC or scav; the loot method itself doesn't
    private static readonly ConcurrentDictionary<MongoId, string> RaidSides = new();

    private sealed class RaidSidePatch : AbstractPatch
    {
        protected override MethodBase? GetTargetMethod() => typeof(LocationLifecycleService).GetMethod(nameof(LocationLifecycleService.StartLocalRaidAsync));

        [PatchPrefix]
        private static void Prefix(MongoId sessionId, StartLocalRaidRequestData request) =>
            RaidSides[sessionId] = request.PlayerSide ?? "";
    }

    /// <summary>Adds the fragments the player still needs to their raid's loot (PMC raids only).</summary>
    private sealed class RaidLootPatch : AbstractPatch
    {
        protected override MethodBase? GetTargetMethod() => typeof(LocationLifecycleService).GetMethod(nameof(LocationLifecycleService.GenerateLocationAndLoot));

        [PatchPostfix]
        private static void Postfix(MongoId sessionId, string name, bool generateLoot, LocationBase __result)
        {
            if (!generateLoot || !RaidSides.TryRemove(sessionId, out string? side) || !string.Equals(side, "pmc", StringComparison.OrdinalIgnoreCase))
                return;
            try
            {
                PaPQuestService.Instance?.AddRaidFragments(sessionId, name, __result);
            }
            catch (Exception e)
            {
                // never let the quest stop a raid from starting
                Console.WriteLine($"[PaP] Could not place 115 fragments: {e}");
            }
        }
    }
}
