using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace PaPHideoutServer;

/// <summary>
/// Keeps the profile files vanilla, so uninstalling the mod can't break a profile. Everything the mod puts into a
/// profile that the game wouldn't know without it (the fragment items, in the inventory or in Mechanic's unclaimed
/// reward mail, the quest's entry and progress counters, and the Pack-a-Punch area entry) is taken out of the JSON as
/// SPT writes the file and kept in profiles/&lt;id&gt;.json next to the DLL. After SPT loads the profiles it's put back in
/// memory, so while the mod is installed nothing changes. Without the mod nobody puts it back: the profile is as if
/// the mod was never there, and a reinstall starts the quest and the machine fresh.
/// Infused guns need nothing: they're ordinary items, and the infused list lives in infused.json.
/// </summary>
internal static class PaPProfileStore
{
    private const string FragmentPrefix = "6650a115a115a115a115f";

    // A side file older than its profile by more than this was left by an earlier install: the profile has been
    // saved without the mod since, and its items could now overlap whatever took their place
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    private static readonly Regex ProfilePath = new(@"user[\\/]profiles[\\/]([0-9a-f]{24})\.json$", RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Dir => Path.Combine(Path.GetDirectoryName(typeof(PaPProfileStore).Assembly.Location)!, "profiles");

    private static string StorePath(string profileId) => Path.Combine(Dir, profileId + ".json");

    internal static Action<string>? LogError;

    // Profiles whose side file is back in memory (or that never had one). Only these may have it rewritten: if a
    // restore failed, saving would otherwise find nothing to keep and delete the stored fragments for good
    private static readonly HashSet<string> Restored = [];

    /// <summary>The profile JSON SPT is about to write, minus the mod's data (kept in the side file). Fails open.</summary>
    public static string Strip(string filePath, string json)
    {
        Match match = ProfilePath.Match(filePath);
        if (!match.Success)
            return json;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                return json;
            var store = new JsonObject();

            if (root["characters"]?["pmc"] is JsonObject pmc)
            {
                if (pmc["Inventory"]?["items"] is JsonArray items && TakeFragments(items) is { Count: > 0 } taken)
                    store["items"] = taken;
                // "Examined" marks for the fragment templates (they'd point at items that don't exist without the mod)
                if (pmc["Encyclopedia"] is JsonObject encyclopedia)
                {
                    var seen = new JsonObject();
                    foreach (string tpl in encyclopedia.Select(p => p.Key).Where(k => k.StartsWith(FragmentPrefix)).ToList())
                    {
                        JsonNode? value = encyclopedia[tpl];
                        encyclopedia.Remove(tpl);
                        seen[tpl] = value;
                    }
                    if (seen.Count > 0)
                        store["encyclopedia"] = seen;
                }
                // The "115 Riddles" entry itself: left behind, a reinstall (without the server starting in between to
                // drop it as orphaned) found the quest done but the fragments and area gone, and the machine unbuildable
                if (pmc["Quests"] is JsonArray quests)
                {
                    for (int i = quests.Count - 1; i >= 0; i--)
                    {
                        if (quests[i]?["qid"]?.GetValue<string>() == PaPQuestInfo.QuestId.ToString())
                        {
                            JsonNode quest = quests[i]!;
                            quests.RemoveAt(i);
                            store["quest"] = quest;
                        }
                    }
                }
                // "115 Riddles" progress (fragments found and handed over): left in the profile, a reinstall would
                // find the quest already done the moment it's accepted
                if (pmc["TaskConditionCounters"] is JsonObject counters)
                {
                    var progress = new JsonObject();
                    foreach (string key in counters.Where(p => p.Value?["sourceId"]?.GetValue<string>() == PaPQuestInfo.QuestId.ToString()).Select(p => p.Key).ToList())
                    {
                        JsonNode? value = counters[key];
                        counters.Remove(key);
                        progress[key] = value;
                    }
                    if (progress.Count > 0)
                        store["counters"] = progress;
                }
                if (pmc["Hideout"]?["Areas"] is JsonArray areas)
                {
                    for (int i = areas.Count - 1; i >= 0; i--)
                    {
                        if (areas[i]?["type"]?.GetValueKind() == JsonValueKind.Number && (int)areas[i]!["type"]! == PaPAreaInfo.Type)
                        {
                            JsonNode area = areas[i]!;
                            areas.RemoveAt(i);
                            store["area"] = area;
                        }
                    }
                }
            }

            if (root["dialogues"] is JsonObject dialogues)
            {
                var mail = new JsonArray();
                foreach (var (dialogueId, dialogue) in dialogues)
                {
                    if (dialogue?["messages"] is not JsonArray messages)
                        continue;
                    foreach (JsonNode? message in messages)
                    {
                        if (message?["items"]?["data"] is JsonArray data && TakeFragments(data) is { Count: > 0 } taken)
                            mail.Add(new JsonObject { ["dialogue"] = dialogueId, ["message"] = message["_id"]?.GetValue<string>(), ["items"] = taken });
                    }
                }
                if (mail.Count > 0)
                    store["mail"] = mail;
            }

            string profileId = match.Groups[1].Value.ToLowerInvariant(), storePath = StorePath(profileId);
            lock (Restored)
            {
                if (!Restored.Contains(profileId) && File.Exists(storePath))
                    return json; // not restored this session: leave both files as they are
                Restored.Add(profileId);
            }
            if (store.Count == 0)
            {
                if (File.Exists(storePath))
                    File.Delete(storePath);
                return json;
            }
            Directory.CreateDirectory(Dir);
            string temp = storePath + ".tmp";
            File.WriteAllText(temp, store.ToJsonString(Indented));
            File.Move(temp, storePath, true); // written whole before the profile, never half
            return root.ToJsonString(json.Contains('\n') ? Indented : Compact);
        }
        catch (Exception e)
        {
            // The profile then keeps the data in its own file, exactly as before; nothing is lost
            LogError?.Invoke($"[PaP] Keeping mod data out of {filePath} failed, saved it as is: {e.Message}");
            return json;
        }
    }

    /// <summary>Removes fragment items (and anything inside them) from an item list; returns them.</summary>
    private static JsonArray TakeFragments(JsonArray items)
    {
        var ids = new HashSet<string>();
        foreach (JsonNode? item in items)
        {
            if (item?["_tpl"]?.GetValue<string>() is string tpl && tpl.StartsWith(FragmentPrefix) && item["_id"]?.GetValue<string>() is string id)
                ids.Add(id);
        }
        // Children of a taken item go with it
        for (bool grew = ids.Count > 0; grew;)
        {
            grew = false;
            foreach (JsonNode? item in items)
            {
                if (item?["parentId"]?.GetValue<string>() is string parent && ids.Contains(parent)
                    && item["_id"]?.GetValue<string>() is string id && ids.Add(id))
                    grew = true;
            }
        }
        var taken = new JsonArray();
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i]?["_id"]?.GetValue<string>() is string id && ids.Contains(id))
            {
                JsonNode item = items[i]!;
                items.RemoveAt(i);
                taken.Insert(0, item);
            }
        }
        return taken;
    }

    /// <summary>Puts a profile's stored data back after SPT loaded it. Returns what it restored, for the log.</summary>
    public static string? Restore(MongoId profileId, SptProfile profile, JsonUtil jsonUtil)
    {
        string id = profileId.ToString().ToLowerInvariant(), storePath = StorePath(id);
        if (!File.Exists(storePath))
            return null;
        string profilePath = Path.Combine("user/profiles/", $"{profileId}.json");
        if (File.Exists(profilePath) && File.GetLastWriteTimeUtc(profilePath) - File.GetLastWriteTimeUtc(storePath) > StaleAfter)
        {
            File.Move(storePath, storePath + ".stale", true);
            return "nothing (side file older than the profile, set aside as .stale)";
        }

        if (JsonNode.Parse(File.ReadAllText(storePath)) is not JsonObject store)
            return null;
        PmcData? pmc = profile.CharacterData?.PmcData;
        int items = 0, mailItems = 0;
        bool area = false;

        if (store["items"] is JsonArray stored && pmc?.Inventory?.Items is { } inventory)
            items = AddMissing(inventory, stored, jsonUtil);

        if (store["encyclopedia"] is JsonObject seen && pmc != null)
        {
            pmc.Encyclopedia ??= new Dictionary<MongoId, bool>();
            foreach (var (tpl, value) in seen)
            {
                if (value?.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
                    pmc.Encyclopedia.TryAdd(new MongoId(tpl), value.GetValue<bool>());
            }
        }

        bool quest = false;
        if (store["quest"] is JsonNode questNode && pmc?.Quests is { } quests
            && jsonUtil.Deserialize<QuestStatus>(questNode.ToJsonString()) is { } status)
        {
            quests.RemoveAll(q => q.QId == PaPQuestInfo.QuestId);
            quests.Add(status);
            quest = true;
        }

        if (store["counters"] is JsonObject progress && pmc != null)
        {
            pmc.TaskConditionCounters ??= new Dictionary<MongoId, TaskConditionCounter>();
            foreach (var (key, value) in progress)
            {
                if (value != null && jsonUtil.Deserialize<TaskConditionCounter>(value.ToJsonString()) is { } counter)
                    pmc.TaskConditionCounters.TryAdd(new MongoId(key), counter);
            }
        }

        if (store["area"] is JsonNode areaNode && pmc?.Hideout?.Areas is { } areas
            && jsonUtil.Deserialize<BotHideoutArea>(areaNode.ToJsonString()) is { } saved)
        {
            areas.RemoveAll(a => (int)a.Type == PaPAreaInfo.Type);
            areas.Add(saved);
            area = true;
        }

        if (store["mail"] is JsonArray mail && profile.DialogueRecords is { } dialogues)
        {
            foreach (JsonNode? entry in mail)
            {
                string? dialogueId = entry?["dialogue"]?.GetValue<string>(), messageId = entry?["message"]?.GetValue<string>();
                if (dialogueId == null || messageId == null || entry!["items"] is not JsonArray mailStored)
                    continue;
                Dialogue? dialogue = dialogues.FirstOrDefault(d => d.Key.ToString() == dialogueId).Value;
                Message? message = dialogue?.Messages?.FirstOrDefault(m => m.Id.ToString() == messageId);
                if (message == null)
                    continue; // the mail is gone (collected or expired)
                message.Items ??= new MessageItems();
                message.Items.Data ??= [];
                mailItems += AddMissing(message.Items.Data, mailStored, jsonUtil);
            }
        }
        lock (Restored)
            Restored.Add(id);
        return $"{items} fragment(s), {mailItems} in mail{(quest ? ", quest" : "")}{(area ? ", Pack-a-Punch area" : "")}";
    }

    private static int AddMissing(List<Item> list, JsonArray stored, JsonUtil jsonUtil)
    {
        int added = 0;
        foreach (JsonNode? node in stored)
        {
            if (node == null || jsonUtil.Deserialize<Item>(node.ToJsonString()) is not { } item || list.Any(i => i.Id == item.Id))
                continue;
            list.Add(item);
            added++;
        }
        return added;
    }

    /// <summary>Every profile file SPT writes goes through FileUtil.WriteFileAsync (SaveServer.SaveProfileAsync).</summary>
    internal sealed class ProfileWritePatch : AbstractPatch
    {
        protected override MethodBase? GetTargetMethod() =>
            typeof(FileUtil).GetMethod(nameof(FileUtil.WriteFileAsync), [typeof(string), typeof(string), typeof(CancellationToken)]);

        [PatchPrefix]
        private static void Prefix(string filePath, ref string fileContent) => fileContent = Strip(filePath, fileContent);
    }
}

/// <summary>Hooks profile writing before anything is saved.</summary>
[Injectable(TypePriority = OnLoadOrder.Preload + 2)]
public class PaPProfileStoreHook(ISptLogger<PaPProfileStoreHook> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        PaPProfileStore.LogError = message => logger.Error(message);
        try
        {
            new PaPProfileStore.ProfileWritePatch().Enable();
        }
        catch (Exception e)
        {
            logger.Error($"[PaP] Profile write hook failed and is disabled (profiles keep the mod's data): {e.GetBaseException().Message}");
        }
        return Task.CompletedTask;
    }
}

/// <summary>Right after SPT loads the profiles (SaveCallbacks): the mod's data back into them.</summary>
[Injectable(TypePriority = OnLoadOrder.SaveCallbacks + 1)]
public class PaPProfileStoreLoader(ISptLogger<PaPProfileStoreLoader> logger, SaveServer saveServer, JsonUtil jsonUtil) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        foreach (var (id, profile) in saveServer.GetProfiles())
        {
            try
            {
                string? restored = PaPProfileStore.Restore(id, profile, jsonUtil);
                if (restored != null)
                    logger.Info($"[PaP] Profile {id}: restored {restored} from the mod's side file");
            }
            catch (Exception e)
            {
                logger.Error($"[PaP] Restoring the mod's data into profile {id} failed: {e.Message}");
            }
        }
        return Task.CompletedTask;
    }
}
