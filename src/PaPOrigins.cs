using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PaPHideoutModule
{
    /// <summary>
    /// Guns upgraded in the Origins machine wear the Origins camo (blue crystal) instead of the BO1 one. The server
    /// doesn't know the difference, so the ids are kept here, in origins_guns.txt beside the DLL.
    /// </summary>
    internal static class PaPOrigins
    {
        private static HashSet<string> _ids;

        private static string FilePath => Path.Combine(PaPAssets.PluginDir, "origins_guns.txt");

        private static HashSet<string> Ids
        {
            get
            {
                if (_ids != null)
                    return _ids;
                _ids = new HashSet<string>();
                try
                {
                    if (File.Exists(FilePath))
                        _ids.UnionWith(File.ReadAllLines(FilePath).Where(l => l.Length > 0));
                }
                catch (System.Exception e)
                {
                    PaPHideoutModulePlugin.Log.LogWarning($"Could not read origins_guns.txt: {e.Message}");
                }
                return _ids;
            }
        }

        public static bool Has(string weaponId) => weaponId != null && Ids.Contains(PaPRegistry.OriginalId(weaponId));

        /// <summary>The gun came back with a new id (insurance mail): it keeps the machine's camo.</summary>
        public static void Move(string oldId, string newId)
        {
            if (!Ids.Remove(oldId))
                return;
            Ids.Add(newId);
            try
            {
                File.WriteAllLines(FilePath, Ids);
            }
            catch (System.Exception e)
            {
                PaPHideoutModulePlugin.Log.LogWarning($"Could not save origins_guns.txt: {e.Message}");
            }
        }

        /// <summary>The gun was just upgraded in the Origins machine (<paramref name="origins"/>) or in the default one.</summary>
        public static void Set(string weaponId, bool origins)
        {
            string id = PaPRegistry.OriginalId(weaponId);
            if (id == null || (origins ? !Ids.Add(id) : !Ids.Remove(id)))
                return;
            try
            {
                File.WriteAllLines(FilePath, Ids);
            }
            catch (System.Exception e)
            {
                PaPHideoutModulePlugin.Log.LogWarning($"Could not save origins_guns.txt: {e.Message}");
            }
        }
    }
}
