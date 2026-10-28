using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Enums.Hideout;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

namespace PaPHideoutServer;

/// <summary>
/// Ids for Mechanic's "115 Riddles". Every fragment is its own template, so the server can tell which ones a player
/// still needs. The quest-item versions are what the player finds on Factory; the "returned" versions are ordinary
/// items Mechanic mails back as the reward (quest items live in the quest stash and can't pay for a hideout area).
/// </summary>
public static class PaPQuestInfo
{
    public const int FragmentCount = 5;
    public static readonly MongoId QuestId = new("6650a115a115a115a115b115");
    public static readonly MongoId FindCondition = new("6650a115a115a115a115c101");
    public static readonly MongoId HandoverCondition = new("6650a115a115a115a115c201");
    public static readonly MongoId HandoverVisibility = new("6650a115a115a115a115c301");

    public static readonly List<MongoId> QuestFragments = Ids("f1");
    public static readonly List<MongoId> ReturnedFragments = Ids("f2");

    /// <summary>The bronze pocket watch: a 1x1 quest item whose prefab stands in until the client swaps in the rock.</summary>
    public static readonly MongoId CloneFrom = new("5937fd0086f7742bf33fc198");
    public static readonly MongoId JewelryParent = new("57864a3d24597754843f8721");
    public const string HandbookQuestItems = "5b619f1a86f77450a702a6f3";
    public const string HandbookValuables = "5b47574386f77428ca22b2f1";
    public const string FactoryLocationId = "55f2d3fd4bdc2d5f408b4567";
    /// <summary>The quest picture (quest_115_riddles.png next to the DLL), served at this URL.</summary>
    public static readonly string ImageUrl = $"/files/quest/icon/{QuestId}";
    public const string ImageFile = "quest_115_riddles.png";

    /// <summary>6650a115a115a115a115{prefix}01 .. {prefix}05</summary>
    private static List<MongoId> Ids(string prefix) =>
        Enumerable.Range(1, FragmentCount).Select(i => new MongoId($"6650a115a115a115a115{prefix}{i:D2}")).ToList();

    /// <summary>Fixed id for one of the quest's own objects (rewards and their items), 6650a115a115a115a115{tag}{n}.</summary>
    internal static MongoId Fixed(string tag, int n) => new($"6650a115a115a115a115{tag}{n:D2}");
}

public record PaPVector(float X, float Y, float Z);

/// <summary>
/// Where one fragment lies on Factory, as the client's marker keys write it. Rotation is euler degrees. Scale is
/// for the client, which sizes the rock model (the game's loot data has no scale).
/// </summary>
public record PaPSpawn(PaPVector Position, PaPVector Rotation, float Scale = 1f);

public record PaPQuestConfig
{
    public string Name { get; init; } = "115 Riddles";
    public string Description { get; init; } =
        "Hi Mercenary,\n\n" +
        "I've been looking into some material I recovered from Factory. Found a few fragments scattered around the place. Doesn't look like anything I've seen before. Unusual density, strange surface structure, and the equipment I have isn't giving me any useful readings.\n\n" +
        "I've also come across something else in the hideout. An old machine casing, stripped of its internals. Can't identify its original purpose, but the construction is unusual. The fragments I've recovered seem to match parts of it.\n\n" +
        "I need more samples before I can figure out what we're dealing with. Check Factory. Look around carefully, especially in places where something might have been left behind. Bring me every fragment you find.\n\n" +
        "Don't try to process them yourself. I want the material intact.";
    public string FindObjective { get; init; } = "Find 115-infused rock fragments on Factory";
    public string HandoverObjective { get; init; } = "Hand over the collected fragments to Mechanic";
    public string SuccessMessage { get; init; } =
        "Good. These are exactly what I was looking for.\n\n" +
        "I've compared the fragments with the casing I found in the hideout. The material matches. More importantly, the fragments appear to fit into the machine's existing structure. I still can't explain what the machine was designed to do, but I'm reasonably confident these pieces are part of the missing assembly.\n\n" +
        "I've returned the fragments to you. Keep them safe. You'll need to install them into the machine yourself; I want to see what happens when the thing is finally operational.\n\n" +
        "One thing at a time. Get the machine assembled first. Then we'll find out what we've got.";

    public int RewardRoubles { get; init; } = 50_000;
    public int RewardExperience { get; init; } = 5_000;

    public List<string> Maps { get; init; } = ["factory4_day", "factory4_night"];
    /// <summary>One per fragment, in order. A null entry uses that fragment's built-in spot.</summary>
    public List<PaPSpawn?> Spawns { get; init; } = [.. DefaultSpawns];

    /// <summary>The five Factory spots the mod ships with.</summary>
    public static readonly IReadOnlyList<PaPSpawn> DefaultSpawns =
    [
        new(new(55.7466f, 0.891f, 25.5856f), new(2.7291f, 269.9221f, 0f), 1f),
        new(new(39.6437f, 0.9864f, -15.2716f), new(336.4462f, 2.873f, 282.3616f), 1.4964f),
        new(new(30.3693f, 4.6111f, -32.6716f), new(275.4715f, 181.233f, 269.9337f), 3f),
        new(new(26.2519f, 4.5164f, -19.8355f), new(359.5985f, 83.3959f, 0.2551f), 1.2f),
        new(new(-18.5756f, 0.099f, 21.8916f), new(0f, 320.2285f, 0f), 0.7f),
    ];

    /// <summary>Fills missing or null spots with the built-in ones (older configs were written with nulls).</summary>
    public void FillSpawns()
    {
        for (int i = 0; i < DefaultSpawns.Count; i++)
        {
            if (i >= Spawns.Count)
                Spawns.Add(DefaultSpawns[i]);
            else
                Spawns[i] ??= DefaultSpawns[i];
        }
    }

    public string FragmentName { get; init; } = "115-infused rock fragment";
    public string FragmentShortName { get; init; } = "115 Rock";
    public string FragmentDescription { get; init; } =
        "A small, unusually dense rock fragment. A faint blue light pulses deep inside it. Mechanic wants it intact.";
    public string ReturnedFragmentDescription { get; init; } =
        "A 115-infused rock fragment, returned by Mechanic. It seems to fit the strange machine in the side room.";
}

/// <summary>
/// Adds the fragment items and the quest. Items must exist before profiles load, hence Preload. It also turns on
/// the server patches the quest needs.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public class PaPQuestLoader(
    ISptLogger<PaPQuestLoader> logger,
    CustomItemService customItemService,
    TemplateTable templateTable,
    LocaleTable localeTable,
    ImageRouter imageRouter,
    ItemConfig itemConfig,
    ItemFilterService itemFilterService) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        PaPQuestConfig config = PaPAreaLoader.Config.Quest;
        // The router matches the URL without its extension
        string image = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(PaPQuestLoader).Assembly.Location)!, PaPQuestInfo.ImageFile);
        if (File.Exists(image))
            imageRouter.AddRoute(PaPQuestInfo.ImageUrl, image);
        else
            logger.Warning($"[PaP] {image} is missing; the quest shows no picture");
        for (int i = 0; i < PaPQuestInfo.FragmentCount; i++)
        {
            AddFragment(PaPQuestInfo.QuestFragments[i], i, true, config);
            AddFragment(PaPQuestInfo.ReturnedFragments[i], i, false, config);
        }
        BlacklistFragments();
        templateTable.Quests[PaPQuestInfo.QuestId] = BuildQuest(config);
        AddQuestLocales(config);
        PaPQuestPatches.Enable(message => logger.Error(message));
        logger.Info($"[PaP] Quest \"{config.Name}\" added ({config.Spawns.Count(s => s != null)}/{PaPQuestInfo.FragmentCount} fragment spawn points set)");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Keeps every fragment out of random loot, daily/repeatable quest rewards, flea, traders and bot gear. They only
    /// exist as the raid spawns the quest places and as Mechanic's mailed reward.
    /// </summary>
    private void BlacklistFragments()
    {
        var all = PaPQuestInfo.QuestFragments.Concat(PaPQuestInfo.ReturnedFragments).ToList();
        ItemConfig items = itemConfig;
        foreach (MongoId id in all)
        {
            items.Blacklist.Add(id);
            items.LootableItemBlacklist.Add(id);
            items.RewardItemBlacklist.Add(id);
        }
        itemFilterService.AddItemToBlacklistCache(all);
        itemFilterService.AddItemToLootableBlacklistCache(all);
    }

    private void AddFragment(MongoId id, int index, bool questItem, PaPQuestConfig config)
    {
        string name = $"{config.FragmentName} #{index + 1}";
        CreateItemResult result = customItemService.CreateItemFromClone(new NewItemFromCloneDetails
        {
            ItemTplToClone = PaPQuestInfo.CloneFrom,
            ParentId = PaPQuestInfo.JewelryParent,
            NewId = id,
            NewItemName = questItem ? $"item_quest_pap_115_fragment_{index + 1}" : $"item_barter_pap_115_fragment_{index + 1}",
            AddToHandbook = true,
            HandbookParentId = questItem ? PaPQuestInfo.HandbookQuestItems : PaPQuestInfo.HandbookValuables,
            HandbookPriceRoubles = questItem ? 0 : 115_000,
            AddToFleaPriceDb = false, // never on the flea
            AddToWeaponShelf = false,
            Locales = new Dictionary<string, LocaleDetails>
            {
                ["en"] = new()
                {
                    Name = name,
                    ShortName = $"{config.FragmentShortName} {index + 1}",
                    Description = questItem ? config.FragmentDescription : config.ReturnedFragmentDescription,
                },
            },
        });
        if (result.Success != true || !templateTable.Items.TryGetValue(id, out TemplateItem? item) || item.Properties == null)
        {
            logger.Error($"[PaP] Could not add fragment {id}: {string.Join("; ", result.Errors ?? [])}");
            return;
        }

        // Set on the clone directly: an override object would also copy its own defaults over the watch's values
        TemplateItemProperties props = item.Properties;
        props.QuestItem = questItem;
        props.BackgroundColor = "violet";
        props.ExaminedByDefault = false; // unknown until inspected, like vanilla quest items
        props.CanSellOnRagfair = false;
        // Neither version can be thrown away or sold off, or a player could lock themselves out of the machine
        props.IsUnsaleable = true;
        props.DiscardingBlock = true;
        props.DiscardLimit = 0;
    }

    private static Quest BuildQuest(PaPQuestConfig config)
    {
        string q = PaPQuestInfo.QuestId.ToString();
        var targets = PaPQuestInfo.QuestFragments.Select(id => id.ToString()).ToList();
        return new Quest
        {
            Id = PaPQuestInfo.QuestId,
            QuestName = config.Name,
            TraderId = new MongoId(PaPAreaInfo.Mechanic),
            Location = PaPQuestInfo.FactoryLocationId,
            Image = PaPQuestInfo.ImageUrl + ".png",
            Type = QuestTypeEnum.PickUp,
            Side = "Pmc",
            IsKey = false,
            Restartable = false,
            InstantComplete = false,
            SecretQuest = false,
            CanShowNotificationsInGame = true,
            AcceptanceAndFinishingSource = "eft",
            ProgressSource = "eft",
            GameModes = [],
            RankingModes = [],
            ArenaLocations = [],
            Status = 0,
            Name = $"{q} name",
            Description = $"{q} description",
            Note = $"{q} note",
            StartedMessageText = $"{q} startedMessageText",
            SuccessMessageText = $"{q} successMessageText",
            FailMessageText = $"{q} failMessageText",
            AcceptPlayerMessage = $"{q} acceptPlayerMessage",
            DeclinePlayerMessage = $"{q} declinePlayerMessage",
            CompletePlayerMessage = $"{q} completePlayerMessage",
            ChangeQuestMessageText = $"{q} changeQuestMessageText",
            Conditions = new QuestConditionTypes
            {
                // Nothing here: the server hides the quest until the profile is given its status (see PaPQuestService)
                AvailableForStart = [],
                AvailableForFinish =
                [
                    ItemCondition("FindItem", PaPQuestInfo.FindCondition, 0, targets, []),
                    ItemCondition("HandoverItem", PaPQuestInfo.HandoverCondition, 1, targets,
                    [
                        // Like vanilla quests: the hand-in shows once everything has been found
                        new VisibilityCondition { Id = PaPQuestInfo.HandoverVisibility.ToString(), ConditionType = "CompleteCondition", Target = PaPQuestInfo.FindCondition.ToString() },
                    ]),
                ],
                Fail = [],
            },
            Rewards = new Dictionary<string, List<Reward>>
            {
                ["Started"] = [],
                ["Success"] = BuildRewards(config),
                ["Fail"] = [],
            },
        };
    }

    private static QuestCondition ItemCondition(string type, MongoId id, int index, List<string> targets, List<VisibilityCondition> visibility) => new()
    {
        Id = id,
        Index = index,
        ConditionType = type,
        Target = new ListOrT<string>(targets, null),
        Value = PaPQuestInfo.FragmentCount, // any mix of the five templates, so five different rocks
        OnlyFoundInRaid = false,
        CountInRaid = false,
        IsEncoded = false,
        DogtagLevel = 0,
        MinDurability = 0,
        MaxDurability = 100,
        DynamicLocale = false,
        GlobalQuestCounterId = "",
        ParentId = "",
        VisibilityConditions = visibility,
    };

    private static List<Reward> BuildRewards(PaPQuestConfig config)
    {
        var rewards = new List<Reward>
        {
            new()
            {
                Id = PaPQuestInfo.Fixed("e0", 1),
                Index = 0,
                Type = RewardType.Experience,
                Value = config.RewardExperience,
                GameMode = ["regular", "pve"],
                AvailableInGameEditions = [],
                IsHidden = false,
                Unknown = false,
            },
            ItemReward(PaPQuestInfo.Fixed("e0", 2), PaPQuestInfo.Fixed("d0", 1), 1, Money.ROUBLES, config.RewardRoubles),
        };
        // Mechanic mails the fragments back with his message, as ordinary items
        for (int i = 0; i < PaPQuestInfo.FragmentCount; i++)
            rewards.Add(ItemReward(PaPQuestInfo.Fixed("e1", i + 1), PaPQuestInfo.Fixed("d1", i + 1), i + 2, PaPQuestInfo.ReturnedFragments[i], 1, unknown: true));
        return rewards;
    }

    // unknown: shown as the game's "? Unknown Reward" in the quest screen, so the fragments keep their mystery
    private static Reward ItemReward(MongoId id, MongoId itemId, int index, MongoId template, int count, bool unknown = false) => new()
    {
        Id = id,
        Index = index,
        Type = RewardType.Item,
        Target = itemId.ToString(),
        Value = count,
        FindInRaid = false,
        IsEncoded = false,
        GameMode = ["regular", "pve"],
        AvailableInGameEditions = [],
        IsHidden = false,
        Unknown = unknown,
        Items = [new Item { Id = itemId, Template = template, Upd = new Upd { StackObjectsCount = count } }],
    };

    private void AddQuestLocales(PaPQuestConfig config)
    {
        string q = PaPQuestInfo.QuestId.ToString();
        var texts = new Dictionary<string, string>
        {
            [$"{q} name"] = config.Name,
            [$"{q} description"] = config.Description,
            [$"{q} note"] = "",
            [$"{q} startedMessageText"] = config.Description,
            [$"{q} successMessageText"] = config.SuccessMessage,
            [$"{q} failMessageText"] = "",
            [$"{q} acceptPlayerMessage"] = "",
            [$"{q} declinePlayerMessage"] = "",
            [$"{q} completePlayerMessage"] = "",
            [$"{q} changeQuestMessageText"] = "",
            [PaPQuestInfo.FindCondition.ToString()] = config.FindObjective,
            [PaPQuestInfo.HandoverCondition.ToString()] = config.HandoverObjective,
        };
        foreach (var locale in localeTable.Global.Values)
        {
            locale.AddTransformer(dictionary =>
            {
                if (dictionary != null)
                {
                    foreach (var (key, text) in texts)
                        dictionary[key] = text;
                }
                return dictionary;
            });
        }
    }
}

/// <summary>Profiles that already have the side room open get the quest at startup.</summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 2)]
public class PaPQuestProfileLoader(ISptLogger<PaPQuestProfileLoader> logger, SaveServer saveServer, PaPQuestService service) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        int unlocked = saveServer.GetProfiles().Values.Count(p => service.TryUnlock(p.CharacterData?.PmcData) != null);
        if (unlocked > 0)
            logger.Info($"[PaP] \"{PaPAreaLoader.Config.Quest.Name}\" offered to {unlocked} profile(s)");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Unlocking "115 Riddles", hiding it until then, and putting the missing fragments into a player's Factory raid.
/// The server patches (PaPQuestPatches) call in here.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PaPQuestService
{
    /// <summary>For the Harmony patches, which are static.</summary>
    internal static PaPQuestService? Instance { get; private set; }

    private readonly ISptLogger<PaPQuestService> _logger;
    private readonly ProfileHelper _profileHelper;
    private readonly TemplateTable _templateTable;
    private readonly TimeUtil _timeUtil;
    private readonly object _lock = new();
    // Fragments already handed to Mechanic: gone from the inventory, but they mustn't spawn again
    private readonly IdStore _handedIn;

    public PaPQuestService(ISptLogger<PaPQuestService> logger, ProfileHelper profileHelper, TemplateTable templateTable, TimeUtil timeUtil)
    {
        _logger = logger;
        _profileHelper = profileHelper;
        _templateTable = templateTable;
        _timeUtil = timeUtil;
        _handedIn = new IdStore("handed_in.json", message => logger.Error(message));
        Instance = this;
    }

    private static PaPQuestConfig Config => PaPAreaLoader.Config.Quest;

    /// <summary>Quest fragment template -> the scale its spawn was placed at, for the client.</summary>
    public static Dictionary<string, float> FragmentScales()
    {
        var scales = new Dictionary<string, float>();
        for (int i = 0; i < PaPQuestInfo.FragmentCount && i < Config.Spawns.Count; i++)
        {
            if (Config.Spawns[i] is { } spawn)
                scales[PaPQuestInfo.QuestFragments[i].ToString()] = spawn.Scale;
        }
        return scales;
    }

    private static QuestStatus? StatusOf(PmcData? pmc) => pmc?.Quests?.FirstOrDefault(q => q.QId == PaPQuestInfo.QuestId);

    /// <summary>
    /// Gives the profile the quest (available to accept) once the side room is open. Returns the new status, or
    /// null if nothing changed.
    /// </summary>
    public QuestStatus? TryUnlock(PmcData? pmc)
    {
        if (pmc?.Quests == null || StatusOf(pmc) != null || !RoomOpen(pmc))
            return null;

        double now = _timeUtil.GetTimeStamp();
        var status = new QuestStatus
        {
            QId = PaPQuestInfo.QuestId,
            StartTime = 0,
            Status = QuestStatusEnum.AvailableForStart,
            StatusTimers = new Dictionary<QuestStatusEnum, double> { [QuestStatusEnum.AvailableForStart] = now },
            CompletedConditions = [],
            AvailableAfter = 0,
        };
        pmc.Quests.Add(status);
        _logger.Info($"[PaP] {pmc.Info?.Nickname}: Mechanic now offers \"{Config.Name}\"");
        return status;
    }

    /// <summary>TryUnlock during an item event, sending the new quest with the response so it appears straight away.</summary>
    public void TryUnlock(PmcData? pmc, MongoId sessionId, ItemEventRouterResponse? output)
    {
        QuestStatus? status = TryUnlock(pmc);
        if (status == null || output?.ProfileChanges == null || !output.ProfileChanges.TryGetValue(sessionId, out ProfileChange? change))
            return;
        change.QuestsStatus ??= [];
        change.QuestsStatus.Add(status);
        if (_templateTable.Quests.TryGetValue(PaPQuestInfo.QuestId, out Quest? quest))
        {
            change.Quests ??= [];
            change.Quests.Add(quest);
        }
    }

    private static bool RoomOpen(PmcData pmc) =>
        pmc.Hideout?.Areas?.Any(a => a.Type == HideoutAreas.EmergencyWall && a.Level >= PaPAreaInfo.WallOpenLevel) == true;

    /// <summary>The quest list a profile sees: without our status entry, "115 Riddles" isn't there.</summary>
    public void FilterClientQuests(MongoId sessionId, List<Quest> quests)
    {
        if (StatusOf(_profileHelper.GetPmcProfile(sessionId)) == null)
            quests.RemoveAll(q => q.Id == PaPQuestInfo.QuestId);
    }

    /// <summary>Templates of the fragments in a hand-in (read before the server removes them).</summary>
    public List<string> FragmentsIn(PmcData pmc, HandoverQuestRequestData request)
    {
        if (request.QuestId != PaPQuestInfo.QuestId || request.Items == null)
            return [];
        var ids = request.Items.Select(i => i.Id).ToHashSet();
        return (pmc.Inventory?.Items ?? [])
            .Where(i => ids.Contains(i.Id) && PaPQuestInfo.QuestFragments.Contains(i.Template))
            .Select(i => i.Template.ToString())
            .ToList();
    }

    public void RecordHandedIn(MongoId sessionId, List<string> templates)
    {
        if (templates.Count == 0)
            return;
        lock (_lock)
        {
            _handedIn.For(sessionId).UnionWith(templates);
            _handedIn.Save();
        }
    }

    /// <summary>
    /// Adds the fragments this player still needs to their own raid's loot, like the map's forced quest-item spawns.
    /// Only while the quest is running, and only fragments they don't have (inventory or quest stash) and haven't
    /// handed in, so a fragment lost on death comes back next raid.
    /// </summary>
    public void AddRaidFragments(MongoId sessionId, string mapName, LocationBase location)
    {
        PaPQuestConfig config = Config;
        if (!config.Maps.Contains(mapName, StringComparer.OrdinalIgnoreCase))
            return;
        PmcData? pmc = _profileHelper.GetPmcProfile(sessionId);
        if (StatusOf(pmc)?.Status != QuestStatusEnum.Started)
            return;

        HashSet<MongoId> owned = (pmc!.Inventory?.Items ?? []).Select(i => i.Template).ToHashSet();
        HashSet<string> handedIn;
        lock (_lock)
            handedIn = [.. _handedIn.For(sessionId)];

        var added = new List<SpawnpointTemplate>();
        for (int i = 0; i < PaPQuestInfo.FragmentCount; i++)
        {
            MongoId tpl = PaPQuestInfo.QuestFragments[i];
            PaPSpawn? spawn = i < config.Spawns.Count ? config.Spawns[i] : null;
            if (spawn == null || owned.Contains(tpl) || handedIn.Contains(tpl.ToString()))
                continue;
            added.Add(SpawnPoint(i, tpl, spawn));
        }
        if (added.Count == 0)
            return;

        location.Loot = (location.Loot ?? []).Concat(added).ToList();
        _logger.Info($"[PaP] {pmc.Info?.Nickname}: {added.Count} 115 fragment(s) placed on {mapName}");
    }

    private static SpawnpointTemplate SpawnPoint(int index, MongoId template, PaPSpawn spawn)
    {
        var itemId = new MongoId();
        return new SpawnpointTemplate
        {
            Id = $"pap_115_fragment_{index + 1} [{itemId}]",
            IsContainer = false,
            UseGravity = false,
            RandomRotation = false,
            Position = new Vector3(spawn.Position.X, spawn.Position.Y, spawn.Position.Z),
            Rotation = new Vector3(spawn.Rotation.X, spawn.Rotation.Y, spawn.Rotation.Z),
            IsAlwaysSpawn = true,
            IsGroupPosition = false,
            GroupPositions = [],
            Root = itemId.ToString(),
            Items = [new SptLootItem { Id = itemId, Template = template, Upd = new Upd { StackObjectsCount = 1 } }],
        };
    }
}
