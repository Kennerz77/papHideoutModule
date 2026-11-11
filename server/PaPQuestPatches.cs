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
                     new RaidLootPatch(), new StartUpgradePatch(),
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

    /// <summary>
    /// Adds the fragments the player still needs to their raid's loot (PMC raids only). Done on the raid start's answer,
    /// not where the loot is made: with Fika the loot is made earlier (when the raid is created, solo too) and the
    /// start only hands it out, so the old hook on the loot method never saw a PMC raid there. Every raid start comes
    /// through here, with or without Fika. In co-op the host's game spawns the loot, so the host's rocks are the ones
    /// that show. On a Fika headless the headless starts the raid, so the player who asked it for the raid counts as
    /// the host (PaPQuestService.LootOwner).
    /// </summary>
    private sealed class RaidLootPatch : AbstractPatch
    {
        protected override MethodBase? GetTargetMethod() => typeof(LocationLifecycleService).GetMethod(nameof(LocationLifecycleService.StartLocalRaidAsync));

        [PatchPostfix]
        private static void Postfix(MongoId sessionId, StartLocalRaidRequestData request, ref Task<StartLocalRaidResponseData> __result) =>
            __result = AddFragments(__result, sessionId, request);

        private static async Task<StartLocalRaidResponseData> AddFragments(Task<StartLocalRaidResponseData> start, MongoId sessionId, StartLocalRaidRequestData request)
        {
            StartLocalRaidResponseData response = await start;
            if (response?.LocationLoot == null || PaPQuestService.Instance is not { } quest)
                return response!;
            // A Fika headless starts the raid under its own profile: its host's rocks go in (whatever side it says)
            MongoId owner = quest.LootOwner(sessionId);
            if (owner == sessionId && !string.Equals(request?.PlayerSide, "pmc", StringComparison.OrdinalIgnoreCase))
                return response;
            try
            {
                quest.AddRaidFragments(owner, request?.Location ?? response.LocationLoot.Id ?? "", response.LocationLoot);
            }
            catch (Exception e)
            {
                // never let the quest stop a raid from starting
                Console.WriteLine($"[PaP] Could not place 115 fragments: {e}");
            }
            return response;
        }
    }
}
