using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace PaPHideoutModule
{
    /// <summary>
    /// The client's synced copy of the server's Pack-a-Punch state: the 115 Infused weapons (powers, and the camo on
    /// everything that sits on them). Read by the patches on every name/stat/shot/paint lookup.
    /// </summary>
    internal static class PaPRegistry
    {
        private const string StateRoute = "/pap/state";

        private static volatile HashSet<string> _infused = new HashSet<string>();

        /// <summary>Bumped whenever anything changes; read on the main thread to refresh visuals.</summary>
        public static int Version { get; private set; }

        // In the hideout's weapons mode the player holds a cloned copy of their gear with new ids;
        // copy id -> original id, so the copy gets the same name, stats, damage and camo
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>();

        public static IReadOnlyCollection<string> All => _infused;

        /// <summary>True once the state has been fetched from the server at least once.</summary>
        public static bool Loaded { get; private set; }

        public static bool IsInfused(string itemId)
        {
            if (itemId == null)
                return false;
            string id = OriginalId(itemId);
            return _infused.Contains(id) || (EnemyGuns.Count > 0 && EnemyGuns.Contains(id));
        }

        // Guns rolled 115 on enemies this raid (main thread only): infused for the raid, and reported after it so the
        // ones the player brought home stay infused
        private static readonly HashSet<string> EnemyGuns = new HashSet<string>();

        public static void MarkEnemyGun(string weaponId)
        {
            if (EnemyGuns.Add(weaponId))
                Version++;
        }

        /// <summary>After the raid: the server keeps the enemy guns that made it into the player's stash infused.</summary>
        public static void FlushEnemyGuns()
        {
            if (EnemyGuns.Count == 0 || Singleton<AbstractGame>.Instance?.InRaid == true)
                return;
            List<string> ids = EnemyGuns.ToList();
            EnemyGuns.Clear();
            PaPServer.Send(PaPServer.BotLoot, ids, result =>
            {
                if (result == null || result.Failed)
                    PaPHideoutModulePlugin.Log.LogWarning($"Reporting {ids.Count} enemy 115 gun(s) failed: {result?.Error ?? "no response"}");
                Refresh();
            });
        }

        /// <summary>True if any weapon is infused (else clones need no mapping).</summary>
        public static bool AnyInfused => _infused.Count > 0;

        /// <summary>The real (profile) id for an item, following a weapons-mode copy back to its original.</summary>
        // Clones (and so new aliases) can be made off the main thread while the main thread reads them; a Dictionary
        // written and read at once can corrupt itself and hang or crash the game, so every access takes this lock
        public static string OriginalId(string itemId)
        {
            if (itemId == null)
                return null;
            lock (Aliases)
                return Aliases.TryGetValue(itemId, out string original) ? original : itemId;
        }

        /// <summary>Merges new copy -> original pairs (old ones are kept: models of a copy can outlive weapons mode).</summary>
        public static void AddAliases(IEnumerable<KeyValuePair<string, string>> copyToOriginal)
        {
            bool changed = false;
            lock (Aliases)
            {
                foreach (var pair in copyToOriginal)
                {
                    if (!Aliases.TryGetValue(pair.Key, out string existing) || existing != pair.Value)
                    {
                        Aliases[pair.Key] = pair.Value;
                        changed = true;
                    }
                }
            }
            if (changed)
                Version++;
        }

        /// <summary>
        /// Copy -> original for clones the game makes to draw an item (icons, the inspect preview, the models on the
        /// body and the character on the loading screen all get new ids). No version bump: the clone's model is built
        /// right after, and a refresh here would re-render icons, which clone again, forever.
        /// </summary>
        public static void AddCloneAlias(string copyId, string originalId)
        {
            string original = OriginalId(originalId);
            lock (Aliases)
            {
                if (Aliases.Count > 50000)
                    Aliases.Clear(); // clones are short-lived; don't grow without end
                Aliases[copyId] = original;
            }
        }

        public static void ClearAliases()
        {
            lock (Aliases)
            {
                if (Aliases.Count == 0)
                    return;
                Aliases.Clear();
            }
            Version++;
        }

        /// <summary>Marks a weapon infused locally right after the server confirmed it.</summary>
        public static void MarkInfused(string itemId)
        {
            _infused = new HashSet<string>(_infused) { itemId };
            Version++;
        }

        public static void RefreshAsync() => Task.Run(() => Refresh());

        /// <summary>Blocking fetch from the server; returns false if the server could not be reached.</summary>
        public static bool Refresh()
        {
            try
            {
                var state = JsonConvert.DeserializeObject<State>(RequestHandler.GetJson(StateRoute)) ?? new State();
                var infused = new HashSet<string>(state.infused ?? new List<string>());
                if (!infused.SetEquals(_infused))
                {
                    _infused = infused;
                    Version++;
                }
                // Infused guns that came back by mail with new ids (insurance): their machine camo follows
                foreach (string pair in state.moved ?? new List<string>())
                {
                    int split = pair.IndexOf('>');
                    if (split > 0)
                        PaPOrigins.Move(pair.Substring(0, split), pair.Substring(split + 1));
                }
                FragmentScales = state.fragmentScales ?? new Dictionary<string, float>();
                Loaded = true;
                return true;
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogWarning($"Could not fetch the Pack-a-Punch state from the server: {e.Message}");
                return false;
            }
        }

        /// <summary>"115 Riddles" fragment template -> the scale its Factory spawn was placed at (server config).</summary>
        public static volatile Dictionary<string, float> FragmentScales = new Dictionary<string, float>();

#pragma warning disable CS0649 // filled in by the JSON reader
        private class State
        {
            public List<string> infused;
            public Dictionary<string, float> fragmentScales;
            public List<string> moved;
        }
#pragma warning restore CS0649
    }
}
