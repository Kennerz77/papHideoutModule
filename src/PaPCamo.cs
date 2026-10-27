using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using UnityEngine;

namespace PaPHideoutModule
{
    /// <summary>
    /// Paints the BO1 Pack-a-Punch camo on item models, part by part. The camo belongs to the 115 Infused gun: every
    /// part wears it while it sits on one, and none anywhere else. Each part is matched to its item through the
    /// model's slot views, so an attachment is judged by where it sits now. Painted materials are cached per original
    /// material, so repainting is just a material swap and the same gun never builds new materials twice.
    /// </summary>
    internal static class PaPCamo
    {
        // _MainTex: Tarkov/Built-in shaders. _BaseMap / _BaseColorMap: URP/HDRP-style shaders some modded guns ship with
        private static readonly int[] BaseColorTextures =
            { Shader.PropertyToID("_MainTex"), Shader.PropertyToID("_BaseMap"), Shader.PropertyToID("_BaseColorMap") };
        private static readonly int[] BaseColors =
            { Shader.PropertyToID("_Color"), Shader.PropertyToID("_BaseColor") };

        private static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private const float CamoEmission = 0.06f;

        // Lenses, reticles, glass, lasers and effects keep their own look
        private static readonly string[] SkipShaderWords = { "lens", "reticle", "collimator", "glass", "optic", "laser", "particle", "flash", "sprite", "transparent", "additive" };

        // original -> painted, and back
        private static readonly Dictionary<Material, Material> Painted = new Dictionary<Material, Material>();
        private static readonly Dictionary<Material, Material> Originals = new Dictionary<Material, Material>();

        // Item models the hooks have seen and the item each shows, so refreshes don't scan the scene
        private static readonly Dictionary<GameObject, Item> Models = new Dictionary<GameObject, Item>();

        /// <summary>
        /// Paints (or un-paints) every part of a model of <paramref name="item"/>. <paramref name="force"/> overrides
        /// each part's own state: true paints all but the bullets, false leaves it plain (icon photos, which must match
        /// the icon hash taken when they were asked for).
        /// </summary>
        public static void Apply(GameObject go, Item item, bool? force = null)
        {
            if (go == null)
                return;
            if (force.HasValue)
            {
                Models.Remove(go); // a photo model goes back to the pool; a refresh mustn't repaint it meanwhile
                if (force.Value || IsPainted(go))
                    foreach (var (renderer, owner) in Parts(go, item))
                        SetPainted(renderer, force.Value && PaPAssets.CamoAlbedo != null && !(owner is Ammo) && !IsAmmoModel(renderer.transform, go.transform), PaPAssets.CamoAlbedo);
                return;
            }
            // Most models (loose loot, ordinary parts) have nothing to paint or un-paint: skip them quickly. Weapons are
            // always tracked, so a gun held in the machine gets its camo on release
            bool weapon = item is Weapon;
            if (!weapon && !OnInfusedGun(item) && !IsPainted(go))
            {
                Models.Remove(go);
                return;
            }
            if (item != null)
                Models[go] = item;
            try
            {
                PaPHideoutModulePlugin.SyncAliasesNow();
            }
            catch (System.Exception e)
            {
                // The weapons-mode copy can be mid-move; the next frame's sync catches up
                PaPHideoutModulePlugin.Log.LogWarning($"PaP alias sync skipped: {e.Message}");
            }

            // A null item means plain all over (the machine's display gun on its way in, or going back to the pool)
            Texture2D camo = item != null ? PaPAssets.CamoAlbedo : null;

            foreach (var (renderer, owner) in Parts(go, item))
            {
                // Bullets never wear it: not as rounds in a mag or the chamber, not as loose ammo
                bool ammo = owner is Ammo || IsAmmoModel(renderer.transform, go.transform);
                SetPainted(renderer, camo != null && !ammo && OnInfusedGun(owner), camo);
            }
        }

        /// <summary>
        /// The camo belongs to the infused gun: a part wears it exactly while it sits on one (itself or anything up
        /// its parent chain is an infused weapon, not still held in the machine). Taken off, it's plain again; put on,
        /// it's painted. Never ammo.
        /// </summary>
        public static bool OnInfusedGun(Item item)
        {
            if (item == null || item is Ammo)
                return false;
            var seen = new HashSet<Item>();
            for (; item != null && seen.Add(item) && seen.Count < 32; item = item.CurrentAddress?.Container?.ParentItem)
            {
                if (item is Weapon && PaPRegistry.IsInfused(item.Id))
                    return !IsHeld(item) && !Excluded.Contains(item.TemplateId.ToString());
            }
            return false;
        }

        // Weapon templates that keep their own look when infused (other mods, through PaPApi)
        private static readonly HashSet<string> Excluded = new HashSet<string>();

        public static void Exclude(string templateId)
        {
            if (!string.IsNullOrEmpty(templateId))
                Excluded.Add(templateId);
        }

        // Weapons infused on the server but still on their way through the machine: they stay plain until they
        // come back out, instead of turning camo in the player's hands the moment the server says yes
        private static readonly HashSet<string> Held = new HashSet<string>();

        /// <summary>True for a held gun and for every part on it.</summary>
        public static bool IsHeld(Item item)
        {
            if (Held.Count == 0)
                return false;
            var seen = new HashSet<Item>();
            for (; item != null && seen.Add(item) && seen.Count < 32; item = item.CurrentAddress?.Container?.ParentItem)
            {
                if (Held.Contains(PaPRegistry.OriginalId(item.Id)))
                    return true;
            }
            return false;
        }

        public static void Hold(Weapon weapon) => Held.Add(PaPRegistry.OriginalId(weapon.Id));

        /// <summary>The gun is out of the machine: its camo goes on everywhere.</summary>
        public static void Release(string weaponId)
        {
            if (weaponId != null && Held.Remove(PaPRegistry.OriginalId(weaponId)))
                RefreshAll();
        }

        /// <summary>Leaving the hideout mid-sequence: nothing stays held.</summary>
        public static void ReleaseAll()
        {
            if (Held.Count == 0)
                return;
            Held.Clear();
            RefreshAll();
        }

        /// <summary>Stop tracking a model that's going back to the pool for some other item.</summary>
        public static void Forget(GameObject go) => Models.Remove(go);

        /// <summary>Re-checks every live model, e.g. after a weapon was infused or an attachment's camo cleared.</summary>
        public static void RefreshAll()
        {
            foreach (GameObject dead in Models.Keys.Where(go => go == null).ToList())
                Models.Remove(dead);
            foreach (Renderer dead in SlotOriginals.Keys.Where(r => r == null).ToList())
                SlotOriginals.Remove(dead);
            // Weapon bundles the game unloaded take their materials with them; drop our copies of those. Not
            // destroyed here: a pooled model we don't track may still draw one, and Unity frees it once unused
            foreach (Material gone in Painted.Keys.Where(m => m == null).ToList())
            {
                Material painted = Painted[gone];
                Painted.Remove(gone);
                if (painted != null)
                    Originals.Remove(painted);
            }
            foreach (var pair in Models.ToList())
            {
                // One model broken by a part move in progress mustn't stop the rest (or throw out of Update)
                try
                {
                    if (pair.Key != null)
                        Apply(pair.Key, pair.Value);
                }
                catch (System.Exception e)
                {
                    Models.Remove(pair.Key);
                    PaPHideoutModulePlugin.Log.LogWarning($"PaP camo refresh skipped a model: {e.Message}");
                }
            }
        }

        /// <summary>Applies a new camo tiling to every painted material.</summary>
        public static void UpdateTiling(float tiling)
        {
            foreach (Material painted in Painted.Values)
            {
                int texture = BaseColorTexture(painted);
                if (texture != 0)
                    painted.SetTextureScale(texture, new Vector2(tiling, tiling));
                if (painted.HasProperty(EmissionMap))
                    painted.SetTextureScale(EmissionMap, new Vector2(tiling, tiling));
            }
        }

        /// <summary>
        /// Every renderer of the model with the item it belongs to. Item models carry a slot view per slot (which item
        /// sits in it, and its model); deeper slots override, so a scope's lens belongs to the scope, not the gun.
        /// </summary>
        private static List<(Renderer renderer, Item owner)> Parts(GameObject go, Item item)
        {
            var owners = new Dictionary<Renderer, Item>();
            Assign(go.transform, item, owners, 0);
            return owners.Select(p => (p.Key, p.Value)).ToList();
        }

        private static void Assign(Transform node, Item item, Dictionary<Renderer, Item> owners, int depth)
        {
            foreach (Renderer renderer in node.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    owners[renderer] = item;
            }
            if (depth > 8)
                return;

            ContainerCollectionView view = node.GetComponent<AssetPoolObject>()?.ContainerCollectionView;
            if (view == null)
                return;
            // Snapshot: a part going on or off rewrites the slot views while we walk them
            foreach (var pair in view.ContainerBones.ToList())
            {
                if (pair.Key == null)
                    continue;
                ContainerCollectionView.SlotView slot = pair.Value;
                if (slot?.ItemView == null || slot.Item == null || slot.ItemView == node)
                    continue;
                // Icons and the machine's display gun are built from a clone with new ids; follow the same slot on
                // the real item instead, so each part is judged by its own (real) camo state
                Item original = (item as ContainerCollection)?.Containers.FirstOrDefault(c => c.ID == pair.Key.ID) is Slot realSlot
                                && realSlot.ContainedItem?.TemplateId == slot.Item.TemplateId
                    ? realSlot.ContainedItem
                    : slot.Item;
                Assign(slot.ItemView, original, owners, depth + 1);
            }
        }

        private static void SetPainted(Renderer renderer, bool paint, Texture2D camo)
        {
            if (renderer == null) // a part destroyed or pooled between the walk and now
                return;
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                    continue;
                bool isPainted = Originals.TryGetValue(material, out Material original) || PaintedCopy(renderer, i, material, out original);
                if (paint && !isPainted && ShouldPaint(material))
                {
                    materials[i] = PaintedFor(material, camo);
                    Remember(renderer, i, material, materials.Length);
                    changed = true;
                }
                else if (!paint && isPainted)
                {
                    materials[i] = original;
                    changed = true;
                }
            }
            if (changed)
                renderer.sharedMaterials = materials;
        }

        private const string PaintedSuffix = " (115)";

        // What each painted renderer slot held before: parts like hot barrels (HotObject) swap the painted material for
        // their own instance of it ("... (115) (Instance)"), which isn't one of ours, and would otherwise keep the camo
        // after a strip. Per renderer and slot, so same-named materials of different guns can't be mixed up
        private static readonly Dictionary<Renderer, Material[]> SlotOriginals = new Dictionary<Renderer, Material[]>();

        private static void Remember(Renderer renderer, int slot, Material original, int slots)
        {
            if (!SlotOriginals.TryGetValue(renderer, out Material[] originals) || originals.Length < slots)
            {
                var grown = new Material[slots];
                originals?.CopyTo(grown, 0);
                SlotOriginals[renderer] = originals = grown;
            }
            originals[slot] = original;
        }

        /// <summary>A copy some other script made of one of our painted materials, and the material it replaced.</summary>
        private static bool PaintedCopy(Renderer renderer, int slot, Material material, out Material original)
        {
            original = null;
            if (!material.name.Contains(PaintedSuffix))
                return false;
            if (SlotOriginals.TryGetValue(renderer, out Material[] originals) && slot < originals.Length && originals[slot] != null)
                original = originals[slot];
            else
            {
                // Painted before this renderer was remembered (a pooled model): match our painted material by name
                string name = material.name.Replace(" (Instance)", "");
                original = Originals.FirstOrDefault(p => p.Key != null && p.Key.name == name).Value;
            }
            return original != null;
        }

        private static Material PaintedFor(Material original, Texture2D camo)
        {
            if (Painted.TryGetValue(original, out Material painted) && painted != null)
                return painted;

            painted = new Material(original) { name = original.name + PaintedSuffix };
            int texture = BaseColorTexture(painted);
            float tiling = PaPHideoutModulePlugin.CamoTiling;
            painted.SetTexture(texture, camo);
            painted.SetTextureScale(texture, new Vector2(tiling, tiling));
            painted.SetTextureOffset(texture, Vector2.zero);
            foreach (int color in BaseColors.Where(painted.HasProperty))
                painted.SetColor(color, Color.white);
            // A faint self-glow of the pattern, so the traces still read in dark rooms. Only where the shader takes an
            // emission map: a colour alone would wash the whole gun in one flat tint
            if (painted.HasProperty(EmissionMap) && painted.HasProperty(EmissionColor))
            {
                painted.SetTexture(EmissionMap, camo);
                painted.SetTextureScale(EmissionMap, new Vector2(tiling, tiling));
                painted.SetColor(EmissionColor, new Color(CamoEmission, CamoEmission, CamoEmission, 1f));
                painted.EnableKeyword("_EMISSION");
            }
            Painted[original] = painted;
            Originals[painted] = original;
            return painted;
        }

        // Rounds a magazine model draws itself (no slot view of their own) are named after the cartridge
        // (EFT's round meshes are "patron_*"; not "shell", which shell deflectors are called)
        private static readonly string[] AmmoModelWords = { "patron", "bullet", "cartridge" };

        private static bool IsAmmoModel(Transform part, Transform root)
        {
            for (Transform t = part; t != null && t != root; t = t.parent)
            {
                string name = t.name.ToLowerInvariant();
                if (AmmoModelWords.Any(name.Contains))
                    return true;
            }
            return false;
        }

        private static int BaseColorTexture(Material mat) => BaseColorTextures.FirstOrDefault(mat.HasProperty);

        /// <summary>True if any renderer of the model still wears a painted material.</summary>
        public static bool IsPainted(GameObject go) =>
            go != null && go.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m != null && (Originals.ContainsKey(m) || m.name.Contains(PaintedSuffix))));

        private static bool ShouldPaint(Material mat)
        {
            if (mat.shader == null || mat.renderQueue >= 2450 || BaseColorTexture(mat) == 0)
                return false;
            string shader = mat.shader.name.ToLowerInvariant();
            return !SkipShaderWords.Any(shader.Contains);
        }
    }

    /// <summary>
    /// Repaints a model a couple of times shortly after it was built, for places that set the model up further
    /// after creating it (the loading-screen character); logs if the camo had been taken off in between.
    /// </summary>
    internal class PaPCamoKeeper : MonoBehaviour
    {
        private static readonly float[] Checks = { 0.5f, 2f, 6f, 15f }; // the loading screen can stay up a while
        private Item _item;
        private float _start;
        private int _next;

        public static void Watch(GameObject go, Item item)
        {
            PaPCamoKeeper keeper = go.GetComponent<PaPCamoKeeper>() ?? go.AddComponent<PaPCamoKeeper>();
            keeper._item = item;
            keeper._start = Time.unscaledTime;
            keeper._next = 0;
            keeper.enabled = true;
        }

        private void Update()
        {
            if (_next >= Checks.Length)
            {
                enabled = false;
                return;
            }
            if (Time.unscaledTime - _start < Checks[_next])
                return;
            _next++;
            try
            {
                bool infused = _item is Weapon && PaPRegistry.IsInfused(_item.Id);
                if (infused && !PaPCamo.IsPainted(gameObject))
                    PaPHideoutModulePlugin.Log.LogInfo($"PaP camo was taken off {_item.LocalizedName()} after it was built; painting it again.");
                PaPCamo.Apply(gameObject, _item);
            }
            catch (System.Exception e)
            {
                PaPHideoutModulePlugin.Log.LogWarning($"PaP camo keeper stopped: {e.Message}");
                enabled = false;
            }
        }
    }
}
