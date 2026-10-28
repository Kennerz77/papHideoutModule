using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace PaPHideoutServer;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.kennerz.paphideoutmodule"; // same GUID as the client plugin (Forge rule)
    public string Name { get; init; } = "PaPHideoutModule"; // letters and numbers only (Forge rule)
    public string Author { get; init; } = "Kennerz";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.0.2");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public List<string>? Incompatibilities { get; init; }
    /// <summary>Hideout Uncensored provides weapons in the hideout, which the machine needs (the client depends on it too).</summary>
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
    {
        ["com.trappuss.hideoutuncensored"] = new SemanticVersioning.Range(">=1.3.0"),
    };
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; } = false;
}

/// <summary>Body of the client's PaP item events: one item id, or several (enemy guns brought home).</summary>
public record PaPItemRequest : BaseInteractionRequestData
{
    [JsonPropertyName("item")]
    public string? Item { get; set; }

    [JsonPropertyName("items")]
    public List<string>? Items { get; set; }

    /// <summary>Upgrade only: the player chose looks-and-sound only (no weapon bonuses), which costs far less.</summary>
    [JsonPropertyName("cosmetic")]
    public bool Cosmetic { get; set; }
}

/// <summary>
/// What the client syncs: the 115 Infused weapons, and the scale each "115 Riddles" fragment was placed at (loot data
/// has no scale, so the client sizes the rock).
/// </summary>
public record PaPState(
    [property: JsonPropertyName("infused")] List<string> Infused,
    [property: JsonPropertyName("fragmentScales")] Dictionary<string, float> FragmentScales);

/// <summary>
/// PaPUpgrade: charge the player and infuse the weapon. PaPStrip: remove its powers. PaPDestroy: the gun was left in
/// the machine. PaPBotLoot: guns taken from 115 enemies in raid stay infused. PaPBuilt: the hideout area was built.
/// Inventory changes flow back to the client. (The camo is the client's business: it's on whatever sits on an
/// infused gun.)
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public class PaPItemEventRouter(PaPService service) : ItemEventRouter([
    Route(PaPService.UpgradeAction, service.Upgrade),
    Route(PaPService.StripAction, service.Strip),
    Route(PaPService.DestroyAction, service.Destroy),
    Route(PaPService.BotLootAction, service.BotLoot),
    Route(PaPService.BuiltAction, service.Built),
])
{
    private static ItemRouteAction<PaPItemRequest> Route(string action,
        Func<PmcData, PaPItemRequest, MongoId, ItemEventRouterResponse, ItemEventRouterResponse> handler) =>
        new(action, (url, pmcData, body, sessionID, output, cancellationToken) =>
            ValueTask.FromResult(handler(pmcData, body, sessionID, output)));
}

/// <summary>GET /pap/state (and the older /pap/infused) for the calling profile.</summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public class PaPStaticRouter(JsonUtil jsonUtil, PaPService service, PaPQuestService questService, HttpResponseUtil httpResponseUtil, SaveServer saveServer) : StaticRouter(jsonUtil, [
    new RouteAction<EmptyRequestData>(
        "/pap/state",
        (url, info, sessionId, output, cancellationToken) =>
        {
            // Profiles made since the server started get the area here (the client asks at startup), and the
            // quest if they've opened the side room in a way the live hooks didn't see (it shows next login)
            PaPService.EnsureArea(saveServer, sessionId);
            PaPService.EnsureQuest(saveServer, questService, sessionId);
            return ValueTask.FromResult(httpResponseUtil.NoBody(new PaPState(service.GetInfused(sessionId), PaPQuestService.FragmentScales())));
        }
    ),
    new RouteAction<EmptyRequestData>(
        "/pap/infused",
        (url, info, sessionId, output, cancellationToken) =>
            ValueTask.FromResult(httpResponseUtil.NoBody(service.GetInfused(sessionId)))
    ),
]) { }

/// <summary>Item ids per profile, kept in a JSON file next to the DLL.</summary>
internal class IdStore(string fileName, Action<string> logError)
{
    private readonly string _path = Path.Combine(Path.GetDirectoryName(typeof(IdStore).Assembly.Location)!, fileName);
    private Dictionary<string, HashSet<string>>? _data;

    public HashSet<string> For(MongoId sessionId)
    {
        _data ??= Load();
        string key = sessionId.ToString();
        if (!_data.TryGetValue(key, out var ids))
            _data[key] = ids = [];
        return ids;
    }

    public void Save() => File.WriteAllText(_path, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));

    private Dictionary<string, HashSet<string>> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception e)
        {
            logError($"[PaP] Could not read {_path}: {e.Message}");
        }
        return [];
    }
}

[Injectable(InjectionType.Singleton)]
public class PaPService
{
    public const string UpgradeAction = "PaPUpgrade";
    public const string StripAction = "PaPStrip";
    public const string DestroyAction = "PaPDestroy";
    public const string BotLootAction = "PaPBotLoot";
    public const string BuiltAction = "PaPBuilt";
    public const int Cost = 500_000;
    public const int CosmeticCost = 5_000; // looks and sound only: no weapon bonuses

    private readonly ISptLogger<PaPService> _logger;
    private readonly PaymentService _paymentService;
    private readonly InventoryHelper _inventoryHelper;
    private readonly MailSendService _mailSendService;
    private readonly HttpResponseUtil _httpResponseUtil;

    private readonly object _lock = new();
    private readonly IdStore _infused, _greeted;

    public PaPService(ISptLogger<PaPService> logger, PaymentService paymentService, InventoryHelper inventoryHelper,
        MailSendService mailSendService, HttpResponseUtil httpResponseUtil)
    {
        _logger = logger;
        _paymentService = paymentService;
        _inventoryHelper = inventoryHelper;
        _mailSendService = mailSendService;
        _httpResponseUtil = httpResponseUtil;
        _infused = new IdStore("infused.json", message => logger.Error(message));
        _greeted = new IdStore("greeted.json", message => logger.Error(message));
    }

    public ItemEventRouterResponse Upgrade(PmcData pmcData, PaPItemRequest body, MongoId sessionId, ItemEventRouterResponse output)
    {
        string? weaponId = body.Item;
        if (string.IsNullOrEmpty(weaponId) || !Owns(pmcData, weaponId))
            return _httpResponseUtil.AppendErrorToOutput(output, "The machine can't find that weapon.");

        Item weapon = pmcData.Inventory!.Items!.First(i => i.Id.ToString() == weaponId);
        var context = new PaPWeaponContext(sessionId, pmcData, weapon, body.Cosmetic);
        lock (_lock)
        {
            HashSet<string> ids = _infused.For(sessionId);
            if (ids.Contains(weaponId))
                return _httpResponseUtil.AppendErrorToOutput(output, "This weapon is already 115 Infused.");
            // Another mod's weapon may have conditions of its own (before any money changes hands)
            if (PaPServerApi.CanUpgrade(context, message => _logger.Error(message)) is { } refusal)
                return _httpResponseUtil.AppendErrorToOutput(output, refusal);

            int cost = body.Cosmetic ? CosmeticCost : Cost;
            _paymentService.AddPaymentToOutput(pmcData, Money.ROUBLES, cost, sessionId, output);
            if (output.Warnings?.Count > 0)
                return output;

            ids.Add(weaponId);
            _infused.Save();
        }

        _logger.Success($"[PaP] {pmcData.Info?.Nickname} Pack-a-Punched weapon {weaponId} for {(body.Cosmetic ? CosmeticCost : Cost)} RUB{(body.Cosmetic ? " (looks only)" : "")}");
        PaPServerApi.Run(context, h => h.Upgraded, "upgrade", message => _logger.Error(message));
        if (context.Report(output))
            _logger.Info($"[PaP] Weapon {weaponId}: other mods changed it on upgrade; the client gets the new item tree");
        return output;
    }

    public ItemEventRouterResponse Strip(PmcData pmcData, PaPItemRequest body, MongoId sessionId, ItemEventRouterResponse output)
    {
        string? weaponId = body.Item;
        lock (_lock)
        {
            if (string.IsNullOrEmpty(weaponId) || !_infused.For(sessionId).Remove(weaponId))
                return _httpResponseUtil.AppendErrorToOutput(output, "That weapon isn't 115 Infused.");
            _infused.Save();
        }

        _logger.Info($"[PaP] {pmcData.Info?.Nickname} stripped 115 powers from weapon {weaponId}");
        if (pmcData.Inventory?.Items?.FirstOrDefault(i => i.Id.ToString() == weaponId) is { } weapon)
        {
            var context = new PaPWeaponContext(sessionId, pmcData, weapon, false);
            PaPServerApi.Run(context, h => h.Stripped, "strip", message => _logger.Error(message));
            if (context.Report(output))
                _logger.Info($"[PaP] Weapon {weaponId}: other mods changed it on strip; the client gets the new item tree");
        }
        return output;
    }

    public ItemEventRouterResponse Destroy(PmcData pmcData, PaPItemRequest body, MongoId sessionId, ItemEventRouterResponse output)
    {
        string? weaponId = body.Item;
        lock (_lock)
        {
            // Only a weapon that just went through the machine can be taken by it
            if (string.IsNullOrEmpty(weaponId) || !_infused.For(sessionId).Contains(weaponId))
                return _httpResponseUtil.AppendErrorToOutput(output, "The machine has no claim on that weapon.");
            if (!Owns(pmcData, weaponId))
                return _httpResponseUtil.AppendErrorToOutput(output, "The machine can't find that weapon.");

            // Removes the weapon and everything attached to it, and reports the deletions to the client
            _inventoryHelper.RemoveItem(pmcData, weaponId, sessionId, output);
            if (output.Warnings?.Count > 0)
                return output;

            _infused.For(sessionId).Remove(weaponId);
            _infused.Save();
        }

        _logger.Warning($"[PaP] {pmcData.Info?.Nickname} left weapon {weaponId} in the machine; it was destroyed");
        return output;
    }

    /// <summary>
    /// Guns the player took off 115 enemies in a raid (the client marked them while the raid ran): the ones that made it
    /// home in the player's inventory stay infused. Anything not in the profile is ignored.
    /// </summary>
    public ItemEventRouterResponse BotLoot(PmcData pmcData, PaPItemRequest body, MongoId sessionId, ItemEventRouterResponse output)
    {
        List<string> kept = (body.Items ?? []).Where(id => Owns(pmcData, id)).ToList();
        if (kept.Count == 0)
            return output;
        lock (_lock)
        {
            _infused.For(sessionId).UnionWith(kept);
            _infused.Save();
        }
        _logger.Success($"[PaP] {pmcData.Info?.Nickname} brought home {kept.Count} 115 Infused gun(s) taken from enemies");
        return output;
    }

    /// <summary>
    /// The client saw the Pack-a-Punch area built. The first time per profile, Mechanic (who got it running) sends
    /// his message; his portrait shows on the notification.
    /// </summary>
    public ItemEventRouterResponse Built(PmcData pmcData, PaPItemRequest body, MongoId sessionId, ItemEventRouterResponse output)
    {
        BotHideoutArea? area = pmcData.Hideout?.Areas?.FirstOrDefault(a => (int)a.Type == PaPAreaInfo.Type);
        if (area?.Level is not >= 1)
            return output;

        lock (_lock)
        {
            if (!_greeted.For(sessionId).Add("built"))
                return output;
            _greeted.Save();
        }

        _mailSendService.SendDirectNpcMessageToPlayer(sessionId, PaPAreaInfo.Mechanic, MessageType.NpcTraderMessage,
            PaPAreaLoader.Config.MechanicMessage, null, null, null, null);
        _logger.Info($"[PaP] {pmcData.Info?.Nickname} built the Pack-a-Punch; Mechanic sent his regards");
        return output;
    }

    private static bool Owns(PmcData pmcData, string itemId) =>
        pmcData.Inventory?.Items?.Any(i => i.Id.ToString() == itemId) == true;

    // ---------- API for other server mods: inject PaPService (it's a singleton) ----------

    /// <summary>Ids of the profile's 115 Infused weapons.</summary>
    public List<string> GetInfused(MongoId sessionId)
    {
        lock (_lock)
            return _infused.For(sessionId).ToList();
    }

    /// <summary>Gives new profiles the hideout area too.</summary>
    internal static void EnsureArea(SaveServer saveServer, MongoId sessionId)
    {
        try
        {
            PaPAreaLoader.EnsureProfileArea(saveServer.GetProfile(sessionId)?.CharacterData?.PmcData);
        }
        catch
        {
            // no profile yet (still being created)
        }
    }

    internal static void EnsureQuest(SaveServer saveServer, PaPQuestService questService, MongoId sessionId)
    {
        try
        {
            questService.TryUnlock(saveServer.GetProfile(sessionId)?.CharacterData?.PmcData);
        }
        catch
        {
            // no profile yet (still being created)
        }
    }

    public bool IsInfused(MongoId sessionId, string itemId)
    {
        lock (_lock)
            return _infused.For(sessionId).Contains(itemId);
    }

    /// <summary>
    /// Infuses a weapon for free (quest rewards, traders, events...). The client picks it up the next time it asks
    /// (entering the hideout) or when a client mod calls PaPApi.Refresh(). Returns false if it already was.
    /// </summary>
    public bool Infuse(MongoId sessionId, string itemId)
    {
        lock (_lock)
        {
            if (!_infused.For(sessionId).Add(itemId))
                return false;
            _infused.Save();
        }
        _logger.Info($"[PaP] weapon {itemId} infused by another mod");
        return true;
    }

    /// <summary>Removes the 115 powers from a weapon. Returns false if it wasn't infused.</summary>
    public bool Uninfuse(MongoId sessionId, string itemId)
    {
        lock (_lock)
        {
            if (!_infused.For(sessionId).Remove(itemId))
                return false;
            _infused.Save();
            return true;
        }
    }
}
