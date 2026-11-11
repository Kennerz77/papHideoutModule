using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace PaPHideoutServer;

/// <summary>
/// Each player's choice: Pack-a-Punch in their normal gameplay (the hideout machine, Mechanic's "115 Riddles"), or only
/// in the Kennerz Zombies mode. Kept per profile in normalgame.json next to the DLL; a player who hasn't picked yet
/// plays as before (on), and every client asks them (the Kennerz mods' shared first-launch question, or F12). A choice
/// counts from the next server start. Switched off, that player's quest, progress, fragments, built machine and gun
/// upgrades are removed at every server start (into removed/, overwritten each time); switched on again, it starts fresh.
/// </summary>
internal static class PaPNormalGame
{
    private static string ModDir => Path.GetDirectoryName(typeof(PaPNormalGame).Assembly.Location)!;
    private static string FilePath => Path.Combine(ModDir, "normalgame.json");

    private static readonly object Lock = new();
    private static Dictionary<string, bool>? _saved, _atStart;

    private static Dictionary<string, bool> Saved
    {
        get
        {
            if (_saved != null)
                return _saved;
            try
            {
                _saved = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(FilePath)) ?? new() : new();
            }
            catch
            {
                _saved = new();
            }
            return _saved;
        }
    }

    private static Dictionary<string, bool> AtStart
    {
        get
        {
            lock (Lock)
                return _atStart ??= new Dictionary<string, bool>(Saved);
        }
    }

    /// <summary>On for this player since the server started (not picked yet: on, as before).</summary>
    public static bool Now(string? profileId) => profileId == null || !AtStart.TryGetValue(profileId, out bool on) || on;

    /// <summary>The player's saved pick (from the next start), or null if they haven't picked.</summary>
    public static bool? Next(string profileId)
    {
        lock (Lock)
            return Saved.TryGetValue(profileId, out bool on) ? on : null;
    }

    public static void Set(string profileId, bool on)
    {
        lock (Lock)
        {
            _ = AtStart;
            Saved[profileId] = on;
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Saved, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static readonly string[] Files = ["infused.json", "greeted.json", "handed_in.json", "moved.json"];

    /// <summary>Server start: every player who chose off loses what the mod kept for them (a copy goes to removed/).</summary>
    public static void RemoveOff(Action<string> log)
    {
        List<string> off = AtStart.Where(p => !p.Value).Select(p => p.Key).ToList();
        if (off.Count == 0)
            return;
        string removed = Path.Combine(ModDir, "removed");
        foreach (string id in off)
        {
            try
            {
                bool any = false;
                Directory.CreateDirectory(removed);
                string side = Path.Combine(ModDir, "profiles", id + ".json");
                if (File.Exists(side))
                {
                    File.Move(side, Path.Combine(removed, id + ".json"), true);
                    any = true;
                }
                foreach (string f in Files)
                {
                    string path = Path.Combine(ModDir, f);
                    if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject all || !all.ContainsKey(id))
                        continue;
                    var copy = new JsonObject { [id] = all[id]?.DeepClone() };
                    File.WriteAllText(Path.Combine(removed, id + "_" + f), copy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    all.Remove(id);
                    File.WriteAllText(path, all.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    any = true;
                }
                if (any)
                    log($"[PaP] Profile {id} chose Pack-a-Punch for zombies only: its quest, machine and upgrades removed (copy in removed/)");
            }
            catch (Exception e)
            {
                log($"[PaP] Removing Pack-a-Punch from profile {id} failed: {e.Message}");
            }
        }
    }
}

public record PaPNormalGameRequest : IRequestData
{
    /// <summary>null: just ask; true/false: this player's pick (from the next server start).</summary>
    [JsonPropertyName("set")] public bool? Set { get; set; }
}

/// <summary>POST /pap/normalgame {set?}: the caller's pick. Replies on now, the saved pick, and whether they've picked.</summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public class PaPNormalGameRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil) : StaticRouter(jsonUtil, [
    new RouteAction<PaPNormalGameRequest>(
        "/pap/normalgame",
        (url, info, sessionId, output, cancellationToken) =>
        {
            string id = sessionId.ToString();
            if (info?.Set is bool on)
                PaPNormalGame.Set(id, on);
            bool? next = PaPNormalGame.Next(id);
            return ValueTask.FromResult(httpResponseUtil.NoBody(new Dictionary<string, object>
            {
                ["now"] = PaPNormalGame.Now(id),
                ["next"] = next ?? PaPNormalGame.Now(id),
                ["chosen"] = next != null,
            }));
        }
    ),
]) { }
