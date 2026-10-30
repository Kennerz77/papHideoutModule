using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Enums.Hideout;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;

namespace PaPHideoutServer;

/// <summary>
/// The Pack-a-Punch hideout area: level 0 (the machine, covered and dead) and level 1 (built). Like the Gym, it only
/// shows once the side room's wall is down; building it takes the five fragments from "115 Riddles". The client
/// plugin adds the matching template and scene object.
/// </summary>
public static class PaPAreaInfo
{
    /// <summary>Area type id; the client uses the same number (115, of course).</summary>
    public const int Type = 115;
    public static readonly MongoId Id = new("6650a115a115a115a115a115");
    public const string Mechanic = "5a7c2eca46aef81a7ca2145d";
    /// <summary>The side room's wall (EmergencyWall) is fully down at this level; the Gym waits for the same.</summary>
    public const int WallOpenLevel = 6;
}

/// <summary>config.json next to the DLL. Missing keys get their defaults, and the file is rewritten with them.</summary>
public record PaPAreaConfig
{
    public int ConstructionSeconds { get; init; } = 10;
    /// <summary>Roubles to build it, on top of the five fragments. 0 for none.</summary>
    public int BuildRoubles { get; init; } = 115_000;
    public string Name { get; init; } = "Pack-a-Punch";
    public string DescriptionUnbuilt { get; init; } =
        "Mechanic found a strange machine in this blocked off room... what does it do?";
    public string DescriptionBuilt { get; init; } =
        "Mechanic got the strange machine running. Feed it a gun and 500,000 roubles and it gives the gun back... different.";
    public string MechanicMessage { get; init; } = "This was an interesting find, mercenary... you owe me!";
    public PaPQuestConfig Quest { get; init; } = new();
}

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class PaPAreaLoader(
    ISptLogger<PaPAreaLoader> logger,
    HideoutTable hideoutTable,
    LocaleTable localeTable,
    SaveServer saveServer) : IOnLoad
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keeps the quest text readable to edit (apostrophes as ', not ')
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static PaPAreaConfig? _config;

    /// <summary>Read on first use: the quest loader needs it before this one runs.</summary>
    public static PaPAreaConfig Config => _config ??= LoadConfig(Console.WriteLine);

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        _config ??= LoadConfig(message => logger.Error(message));
        AddArea();
        AddLocales();
        int added = 0;
        foreach (var profile in saveServer.GetProfiles().Values)
        {
            if (EnsureProfileArea(profile.CharacterData?.PmcData))
                added++;
        }
        logger.Info($"[PaP] Pack-a-Punch hideout area ready ({added} profile(s) given the area)");
        return Task.CompletedTask;
    }

    /// <summary>Gives a profile the (unbuilt) area if it doesn't have it yet. Returns true if it was added.</summary>
    public static bool EnsureProfileArea(PmcData? pmc)
    {
        List<BotHideoutArea>? areas = pmc?.Hideout?.Areas;
        if (areas == null || areas.Any(a => (int)a.Type == PaPAreaInfo.Type))
            return false;
        areas.Add(new BotHideoutArea
        {
            Type = (HideoutAreas)PaPAreaInfo.Type,
            Level = 0,
            Active = true,
            PassiveBonusesEnabled = true,
            CompleteTime = 0,
            Constructing = false,
            Slots = [],
        });
        return true;
    }

    private static PaPAreaConfig LoadConfig(Action<string> logError)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(PaPAreaLoader).Assembly.Location)!, "config.json");
        var config = new PaPAreaConfig();
        try
        {
            if (File.Exists(path))
                config = JsonSerializer.Deserialize<PaPAreaConfig>(File.ReadAllText(path), JsonOptions) ?? config;
            config.Quest.FillSpawns();
            // Written back so settings added in a newer version show up (keys no longer used drop out)
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch (Exception e)
        {
            logError($"[PaP] Could not read {path}, using defaults: {e.Message}");
        }
        return config;
    }

    private void AddArea()
    {
        hideoutTable.Areas.RemoveAll(a => a.Id == PaPAreaInfo.Id);

        // The fragments Mechanic mails back after "115 Riddles" are the missing assembly, plus some money. They can't be
        // sold or discarded and exist nowhere else in the game (see PaPQuestLoader). Finishing the quest is also
        // enforced by a server patch (the game's area requirements have no quest type).
        List<StageRequirement> requirements = PaPQuestInfo.ReturnedFragments
            .Select(tpl => ItemRequirement(tpl, 1))
            .ToList();
        if (Config.BuildRoubles > 0)
            requirements.Add(ItemRequirement(Money.ROUBLES, Config.BuildRoubles));

        hideoutTable.Areas.Add(new HideoutArea
        {
            Id = PaPAreaInfo.Id,
            Type = (HideoutAreas)PaPAreaInfo.Type,
            IsEnabled = true,
            NeedsFuel = false, // it needs power, which the client checks; no fuel of its own
            // Same gate as the Gym, Hall of Fame and gear stand: the client hides the area until the wall is down
            Requirements = [new HideoutAreaRequirement { Type = "Area", AreaType = (int)HideoutAreas.EmergencyWall, RequiredLevel = PaPAreaInfo.WallOpenLevel }],
            IsTakeFromSlotLocked = false,
            CraftGivesExperience = false,
            DisplayLevel = true,
            EnableAreaRequirements = true,
            Stages = new Dictionary<string, Stage>
            {
                ["0"] = NewStage([], 0),
                ["1"] = NewStage(requirements, Config.ConstructionSeconds),
            },
        });
    }

    private static Stage NewStage(List<StageRequirement> requirements, int seconds) => new()
    {
        Requirements = requirements,
        Bonuses = [],
        Slots = 0,
        ConstructionTime = seconds,
        Description = "",
        AutoUpgrade = false,
        DisplayInterface = true,
        Improvements = [],
    };

    private static StageRequirement ItemRequirement(MongoId template, int count) => new()
    {
        Type = "Item",
        TemplateId = template,
        Count = count,
        IsFunctional = false,
        IsEncoded = false,
        IsSpawnedInSession = false,
    };

    private void AddLocales()
    {
        var texts = new Dictionary<string, string>
        {
            [$"hideout_area_{PaPAreaInfo.Type}_name"] = Config.Name,
            [$"hideout_area_{PaPAreaInfo.Type}_stage_0_description"] = Config.DescriptionUnbuilt,
            [$"hideout_area_{PaPAreaInfo.Type}_stage_1_description"] = Config.DescriptionBuilt,
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
