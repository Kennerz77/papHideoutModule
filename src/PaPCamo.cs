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
        // The other two looks (Origins crystal, Origins metal plating) keep their own painted copies
        private static bool _iconPass;
        private static readonly Dictionary<Material, Material>[] IconKinds =
            { new Dictionary<Material, Material>(), new Dictionary<Material, Material>(), new Dictionary<Material, Material>(), new Dictionary<Material, Material>() };
        // 0 BO1 camo, 1 Origins crystal, 2 Origins metal plating, 3 Origins split: crystal and plating on one material
        private static readonly Dictionary<Material, Material>[] PaintedKinds =
            { Painted, new Dictionary<Material, Material>(), new Dictionary<Material, Material>(), new Dictionary<Material, Material>() };

        private static bool IsCrystal(int kind) => kind == 1 || kind == 3;
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
                _iconPass = true; // pictures get their own, darker copies: the studio's lights are far brighter than the hideout's
                try
                {
                if (force.Value || IsPainted(go))
                    foreach (var (renderer, owner) in Parts(go, item))
                        SetPainted(renderer, force.Value && PaPAssets.CamoAlbedo != null && !(owner is Ammo) && !IsAmmoModel(renderer.transform, go.transform), PaPAssets.CamoAlbedo, owner);
                }
                finally
                {
                    _iconPass = false;
                }
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
                SetPainted(renderer, camo != null && !ammo && OnInfusedGun(owner), camo, owner);
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

        private static readonly List<string> PlainWords = new List<string>();
        private static readonly List<string> AlwaysPaintWords = new List<string>();

        public static void KeepPlain(string alwaysPaintWord, string[] plainWords)
        {
            if (!string.IsNullOrEmpty(alwaysPaintWord))
                AlwaysPaintWords.Add(alwaysPaintWord.ToLowerInvariant());
            PlainWords.AddRange((plainWords ?? new string[0]).Select(w => w.ToLowerInvariant()));
        }

        private static readonly List<(string word, bool crystal)> OriginsRoles = new List<(string, bool)>();
        private static readonly List<string> OriginsPlainWords = new List<string>();
        private static readonly List<string> OriginsCutouts = new List<string>();

        public static void PaintCutoutOnOrigins(string materialWord)
        {
            if (!string.IsNullOrEmpty(materialWord))
                OriginsCutouts.Add(materialWord.ToLowerInvariant());
        }

        public static void KeepOriginsPlain(string materialWord)
        {
            if (!string.IsNullOrEmpty(materialWord))
                OriginsPlainWords.Add(materialWord.ToLowerInvariant());
        }

        private static readonly List<(string word, bool crystal)> OriginsPartRoles = new List<(string, bool)>();

        public static void SetOriginsPartLook(string rendererWord, bool crystal)
        {
            if (!string.IsNullOrEmpty(rendererWord))
                OriginsPartRoles.Add((rendererWord.ToLowerInvariant(), crystal));
        }

        public static void SetOriginsLook(string word, bool crystal)
        {
            if (!string.IsNullOrEmpty(word))
                OriginsRoles.Add((word.ToLowerInvariant(), crystal));
        }

        /// <summary>The blue crystal flows: its texture and glow drift, the way the Ray Gun's tube does.</summary>
        // Renderer slots wearing the crystal. Animated through property blocks: the game copies materials for the gun in
        // the hands, so changing ours alone never reached it
        private static readonly Dictionary<Renderer, HashSet<int>> CrystalSlots = new Dictionary<Renderer, HashSet<int>>();
        private static readonly MaterialPropertyBlock CrystalBlock = new MaterialPropertyBlock();
        private static readonly int MainTexST = Shader.PropertyToID("_MainTex_ST");
        private const string DetailST_Name = "_DetailAlbedoMap";

        private static int _animatedFrame = -1;

        /// <summary>
        /// The crystal is set on each camera's pre-cull, once a frame: after everything else has run, so the hot barrel's
        /// own property block (written as the gun fires, and which replaces ours) can't leave it dark for a frame.
        /// </summary>
        /// <summary>Also every LateUpdate, in case the game's cameras never raise onPreCull.</summary>
        public static void AnimateLate() => AnimateCrystalSlots(Time.time);

        public static void HookRendering() => Camera.onPreCull += _ =>
        {
            if (_animatedFrame == Time.frameCount)
                return;
            _animatedFrame = Time.frameCount;
            AnimateCrystalSlots(Time.time);
        };

        private static void Track(Renderer renderer, int slot, int kind)
        {
            if (CrystalSlots.TryGetValue(renderer, out HashSet<int> slots) && slots.Remove(slot))
                ClearBlock(renderer, slot);
            if (!IsCrystal(kind))
                return;
            if (slots == null)
                CrystalSlots[renderer] = slots = new HashSet<int>();
            slots.Add(slot);
        }

        /// <summary>Our values off a slot (a pooled model shown next as a plain gun kept the crystal's tiling and colour).</summary>
        private static void ClearBlock(Renderer renderer, int slot)
        {
            if (renderer == null)
                return;
            CrystalBlock.Clear();
            renderer.SetPropertyBlock(CrystalBlock, slot);
        }

        private static void AnimateCrystalSlots(float t)
        {
            if (CrystalSlots.Count == 0)
                return;
            float tiling = PaPHideoutModulePlugin.CamoTiling;
            float shade = 0.92f + 0.08f * Mathf.Sin(t * 1.3f + 0.6f * Mathf.Sin(t * 0.7f));
            float pulse = 0.45f + 0.2f * Mathf.Sin(t * 1.7f) + 0.1f * Mathf.Sin(t * 4.3f);
            foreach (Renderer dead in CrystalSlots.Keys.Where(r => r == null).ToList())
                CrystalSlots.Remove(dead);
            foreach (var pair in CrystalSlots)
            {
                if (!pair.Key.isVisible)
                    continue;
                Material[] mats = pair.Key.sharedMaterials;
                foreach (int slot in pair.Value.ToList())
                {
                    // Only the Origins crystal moves (the default camo never does); a slot no longer wearing it is cleared
                    if (slot >= mats.Length || mats[slot] == null || !mats[slot].name.Contains(CrystalTag))
                    {
                        pair.Value.Remove(slot);
                        ClearBlock(pair.Key, slot);
                        continue;
                    }
                    // Set on the material the renderer really draws (the game's own copy of ours, if it made one):
                    // property blocks never showed on guns in the hands
                    Material m = mats[slot];
                    var flow = new Vector2(t * 0.06f, t * 0.04f);
                    if (m.IsKeywordEnabled("_DETAIL_MULX2"))
                    {
                        m.SetTextureOffset(DetailST_Name, flow);
                    }
                    else
                    {
                        m.SetTextureOffset("_MainTex", flow);
                        m.SetColor("_Color", new Color(shade, shade, shade, 1f) * 1.15f);
                    }
                    if (!m.name.Contains(IconTag))
                        m.SetColor(EmissionColor, new Color(0.35f, 0.85f, 1f) * (1.0f + 1.2f * pulse));
                }
            }
        }

        public static void AnimateCrystal()
        {
            float t = Time.time;
            foreach (Material m in PaintedKinds[1].Values)
            {
                if (m == null)
                    continue;
                var offset = new Vector2(t * 0.045f, t * 0.03f);
                int texture = BaseColorTexture(m);
                if (texture != 0)
                    m.SetTextureOffset(texture, offset);
                if (m.HasProperty(EmissionMap))
                    m.SetTextureOffset(EmissionMap, new Vector2(-t * 0.06f, t * 0.04f));
                float pulse = 0.45f + 0.2f * Mathf.Sin(t * 1.7f) + 0.1f * Mathf.Sin(t * 4.3f);
                if (m.HasProperty(EmissionColor))
                    m.SetColor(EmissionColor, new Color(pulse, pulse, pulse, 1f));
                // The colour map itself breathes too (not every gun shader takes an emission map)
                float shade = 0.92f + 0.08f * Mathf.Sin(t * 1.3f + 0.6f * Mathf.Sin(t * 0.7f));
                foreach (int color in BaseColors.Where(m.HasProperty))
                    m.SetColor(color, new Color(shade, shade, shade, 1f));
            }
        }

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
            foreach (Dictionary<Material, Material> kind in PaintedKinds)
            {
                foreach (Material gone in kind.Keys.Where(m => m == null).ToList())
                {
                    Material painted = kind[gone];
                    kind.Remove(gone);
                    if (painted != null)
                        Originals.Remove(painted);
                }
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
            foreach (Material painted in PaintedKinds[0].Values)
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

        private static Texture2D LookTexture(int kind, Texture2D camo) => kind == 0 ? camo : IsCrystal(kind) ? PaPAssets.OriginsCrystal : PaPAssets.OriginsMetal;

        private static void SetPainted(Renderer renderer, bool paint, Texture2D camo, Item owner)
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
                int kind = paint && !isPainted ? PaintKind(material, owner, renderer) : -1;
                if (kind >= 0)
                {
                    materials[i] = PaintedFor(material, LookTexture(kind, camo), kind);
                    Track(renderer, i, kind);
                    Remember(renderer, i, material, materials.Length);
                    changed = true;
                }
                else if (!paint && isPainted)
                {
                    materials[i] = original;
                    Track(renderer, i, -1);
                    changed = true;
                }
                else if (paint && isPainted && original != null)
                {
                    // A pooled model (the inspect window, the hands) can still wear the look it had for another gun or for
                    // a photo: it's re-picked every time, not only when first painted
                    int want = PaintKind(original, owner, renderer);
                    Material target = want < 0 ? original : PaintedFor(original, LookTexture(want, camo), want);
                    if (Bare(material.name) != target.name)
                    {
                        materials[i] = target;
                        Track(renderer, i, want);
                        changed = true;
                    }
                }
                else if (!paint && !isPainted)
                {
                    Track(renderer, i, -1);
                }
            }
            if (changed)
                renderer.sharedMaterials = materials;
        }

        private const string PaintedSuffix = " (115)";
        private const string CrystalTag = " (115) (ori)", PlatingTag = " (115) (orm)", IconTag = " (icon)";

        private static string PaintedName(Material original, int kind) =>
            original.name + (kind == 1 ? CrystalTag : kind == 3 ? CrystalTag + " (x)" : kind == 2 ? PlatingTag : PaintedSuffix) + (_iconPass ? IconTag : "");

        private static string Bare(string name) => name.Replace(" (Instance)", "");

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

        private static Material PaintedFor(Material original, Texture2D camo, int kind)
        {
            Dictionary<Material, Material> cache = (_iconPass ? IconKinds : PaintedKinds)[kind];
            if (cache.TryGetValue(original, out Material painted) && painted != null)
                return painted;
            if (camo == null)
                camo = PaPAssets.CamoAlbedo; // an Origins texture that isn't in the bundle: the BO1 look

            Shader glowing = IsCrystal(kind) ? Shader.Find("Standard (Specular setup)") : null;
            painted = glowing != null ? new Material(glowing) { name = PaintedName(original, kind) } : new Material(original) { name = PaintedName(original, kind) };
            int texture = glowing != null ? Shader.PropertyToID("_MainTex") : BaseColorTexture(painted);
            float tiling = PaPHideoutModulePlugin.CamoTiling;
            painted.SetTexture(texture, camo);
            painted.SetTextureScale(texture, new Vector2(tiling, tiling));
            painted.SetTextureOffset(texture, Vector2.zero);
            foreach (int color in BaseColors.Where(painted.HasProperty))
                painted.SetColor(color, Color.white);
            // Shiny black: glossy, but not mirror-metal (a high metallic reflects the room and reads silver)
            foreach (string gloss in new[] { "_Glossiness", "_Smoothness" })
            {
                if (painted.HasProperty(gloss))
                    painted.SetFloat(gloss, kind == 0 ? 0.45f : IsCrystal(kind) ? 0.75f : 0.55f);
            }
            if (painted.HasProperty("_Metallic"))
                painted.SetFloat("_Metallic", kind == 2 ? 0.4f : 0.1f);
            if (painted.HasProperty("_SpecColor"))
                painted.SetColor("_SpecColor", kind == 0 ? new Color(0.12f, 0.1f, 0.1f, 1f) : new Color(0.25f, 0.25f, 0.28f, 1f));
            // A faint self-glow of the pattern, so the traces still read in dark rooms. Only where the shader takes an
            // emission map: a colour alone would wash the whole gun in one flat tint
            if (kind != 1 && painted.HasProperty(EmissionMap) && painted.HasProperty(EmissionColor))
            {
                painted.SetTexture(EmissionMap, camo);
                painted.SetTextureScale(EmissionMap, new Vector2(tiling, tiling));
                painted.SetColor(EmissionColor, new Color(CamoEmission, CamoEmission, CamoEmission, 1f));
                painted.EnableKeyword("_EMISSION");
            }
            if (IsCrystal(kind) && painted.HasProperty(EmissionMap) && painted.HasProperty(EmissionColor) && PaPAssets.OriginsCrystalGlow != null)
            {
                painted.SetTexture(EmissionMap, PaPAssets.OriginsCrystalGlow);
                painted.SetTextureScale(EmissionMap, new Vector2(tiling, tiling));
                painted.SetColor(EmissionColor, new Color(0.55f, 0.55f, 0.55f, 1f));
                painted.EnableKeyword("_EMISSION");
            }
            if (IsCrystal(kind) && glowing != null)
            {
                // Glowing, translucent-looking ice: bright cyan colour, a strong emission, glossy
                painted.SetColor("_SpecColor", new Color(0.55f, 0.7f, 0.9f, 1f));
                painted.SetFloat("_Glossiness", 0.85f);
                painted.SetTexture("_EmissionMap", PaPAssets.OriginsCrystalGlow != null ? PaPAssets.OriginsCrystalGlow : camo);
                painted.SetTextureScale("_EmissionMap", new Vector2(tiling, tiling));
                painted.SetColor("_EmissionColor", new Color(0.35f, 0.85f, 1f) * 1.4f);
                painted.EnableKeyword("_EMISSION");
                painted.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            if (IsCrystal(kind))
            {
                // Cut-crystal facets
                Texture2D facets = PaPAssets.OriginsCrystalNormal;
                if (facets != null && painted.HasProperty("_BumpMap"))
                {
                    painted.SetTexture("_BumpMap", facets);
                    painted.SetTextureScale("_BumpMap", new Vector2(tiling, tiling));
                    if (painted.HasProperty("_BumpScale"))
                        painted.SetFloat("_BumpScale", 1.2f);
                    painted.EnableKeyword("_NORMALMAP");
                }
            }
            if (kind == 3 && SplitFor(original) is Split split)
            {
                // The crystal as a detail layer over a baked map of plating, masked to the crystal's texels: the
                // detail scrolls (the flow), the plating and the mask stay put
                painted.SetTexture("_MainTex", split.Main);
                painted.SetTextureScale("_MainTex", Vector2.one);
                painted.SetTextureOffset("_MainTex", Vector2.zero);
                painted.SetTexture("_DetailMask", split.Mask);
                painted.SetTexture("_DetailAlbedoMap", PaPAssets.OriginsCrystal);
                painted.SetTextureScale("_DetailAlbedoMap", new Vector2(tiling, tiling));
                painted.SetTexture("_DetailNormalMap", PaPAssets.OriginsCrystalNormal);
                painted.SetFloat("_DetailNormalMapScale", 1.2f);
                painted.SetTexture("_BumpMap", null);
                painted.EnableKeyword("_DETAIL_MULX2");
                painted.EnableKeyword("_NORMALMAP");
                painted.SetTexture("_EmissionMap", split.Emission);
                painted.SetTextureScale("_EmissionMap", Vector2.one);
                // Not the crystal's mirror-like specular: the plating half would read as blank, shiny black
                painted.SetFloat("_Glossiness", 0.5f);
                painted.SetColor("_SpecColor", new Color(0.22f, 0.24f, 0.27f, 1f));
            }
            if (_iconPass)
            {
                // The studio overexposes: dimmer colour, no self-glow, so the picture matches the model in the hands
                foreach (int color in BaseColors.Where(painted.HasProperty))
                    painted.SetColor(color, new Color(0.42f, 0.42f, 0.42f, 1f));
                if (kind == 1 && painted.HasProperty(EmissionColor))
                    painted.SetColor(EmissionColor, new Color(0.15f, 0.35f, 0.45f, 1f));
                else if (painted.HasProperty(EmissionColor))
                    painted.SetColor(EmissionColor, Color.black);
            }
            cache[original] = painted;
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

        // The camo is black metal: sights, lights and lasers keep their own look, and so do wooden and plastic parts
        private static readonly string[] SkipOwnerWords = { "sight", "scope", "light", "laser", "optic" };
        private static readonly string[] NonMetalWords = { "plastic", "polymer", "rubber", "nylon", "foam", "furniture", "fabric", "leather", "pad", "grip" };

        private static readonly HashSet<string> StructuralSlots = new HashSet<string>
        {
            "mod_barrel", "mod_handguard", "mod_stock", "mod_stock_000", "mod_stock_001", "mod_stock_002", "mod_stock_akms", "mod_pistol_grip", "mod_pistol_grip_akms",
            "mod_gas_block", "mod_reciever", "mod_receiver", "mod_charge", "mod_bolt", "mod_trigger", "mod_hammer", "mod_catch", "mod_mount_000", "mod_equipment",
        };

        private const string SightsCategory = "5448fe7a4bdc2d6f028b456b", ScopeCategory = "55818ae44bdc2dde698b456c";

        private static bool InCategory(Item item, string categoryId)
        {
            for (ItemTemplate t = item?.Template; t != null; t = t.Parent)
            {
                if (t.ParentId?.ToString() == categoryId)
                    return true;
            }
            return false;
        }

        private static string SlotOf(Item item) => (item?.CurrentAddress?.Container as Slot)?.ID ?? "";

        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static int Note(Material mat, int kind)
        {
            if (Logged.Add(mat.name + "#" + kind))
                PaPHideoutModulePlugin.Log.LogInfo($"[camo] painted: {mat.name} (look {kind})");
            return kind;
        }

        private static int Skip(Material mat, string why)
        {
            if (Logged.Add(mat.name + why))
                PaPHideoutModulePlugin.Log.LogInfo($"[camo] left plain: {mat.name} ({why})");
            return -1;
        }

        /// <summary>
        /// Which look a material gets: -1 none, 0 the BO1 camo (shiny black, metal only), 1 Origins crystal, 2 Origins
        /// metal plating (furniture and attachments of an Origins gun). The reasons for skipping are logged once.
        /// </summary>
        private static int PaintKind(Material mat, Item owner, Renderer renderer)
        {
            int kind = PaintKindRaw(mat, owner, renderer);
            // A crystal material that also holds wood (a shotgun's receiver and grip in one) or, for a mod gun that asks,
            // other colours, is split per texel instead of going all crystal
            if (kind < 1)
                return kind;
            string bare = Bare(mat.name).ToLowerInvariant();
            // A mod's explicit look for this material or part wins over any split
            if (OriginsRoles.Any(role => bare.Contains(role.word)))
                return kind;
            if (renderer != null && OriginsPartRoles.Any(role => renderer.name.ToLowerInvariant().Contains(role.word)))
                return kind;
            bool redWord = RedSplitWords.Any(bare.Contains);
            // Only a mod gun that asks for it is split (the Ray Gun's red); the wood split on other guns was taken out
            if (redWord)
                return SplitFor(mat) != null ? 3 : 1;
            return kind;
        }

        // ---------- Origins split ----------

        private sealed class Split
        {
            public Texture2D Main, Mask, Emission;
        }

        private static readonly Dictionary<Material, Split> Splits = new Dictionary<Material, Split>();
        private static readonly List<string> RedSplitWords = new List<string>();

        /// <summary>Materials with this word: their red texels take the crystal, everything else the metal plating.</summary>
        public static void SplitOnRed(string materialWord)
        {
            if (!string.IsNullOrEmpty(materialWord))
                RedSplitWords.Add(materialWord.ToLowerInvariant());
        }

        private static Split SplitFor(Material original)
        {
            if (original == null)
                return null;
            if (Splits.TryGetValue(original, out Split known))
                return known;
            Split split = null;
            try
            {
                split = BuildSplit(original);
            }
            catch (System.Exception e)
            {
                PaPHideoutModulePlugin.Log.LogWarning($"[camo] Origins split of {original.name} skipped: {e.Message}");
            }
            Splits[original] = split;
            return split;
        }

        private static Split BuildSplit(Material original)
        {
            int textureId = BaseColorTexture(original);
            Texture source = textureId != 0 ? original.GetTexture(textureId) : null;
            if (source == null || PaPAssets.OriginsMetal == null || PaPAssets.OriginsCrystalGlow == null)
                return null;
            bool red = RedSplitWords.Any(Bare(original.name).ToLowerInvariant().Contains);
            int w = Mathf.Min(1024, source.width), h = Mathf.Max(1, source.height * w / source.width);
            Color32[] pixels = Read(source, w, h);
            // The "other" texels (wood, or not red): found per texel, then closed up over a few texels so the scratches and
            // grain in them don't leak crystal, and softened at the edge
            var other = new float[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color.RGBToHSV(pixels[i], out float hue, out float sat, out float val);
                bool isRed = (hue < 0.05f || hue > 0.94f) && sat > 0.35f && val > 0.1f;
                bool isWood = hue >= 0.02f && hue <= 0.13f && sat > 0.28f && val > 0.12f;
                other[i] = (red ? !isRed : isWood) ? 1f : 0f;
            }
            float[] closed = Blur(other, w, h, Mathf.Max(2, w / 160));
            for (int i = 0; i < closed.Length; i++)
                closed[i] = closed[i] > 0.3f ? 1f : 0f;
            float[] soft = Blur(closed, w, h, 2);
            var crystal = new float[pixels.Length];
            int crystalCount = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                crystal[i] = 1f - soft[i];
                if (crystal[i] > 0.5f)
                    crystalCount++;
            }
            // Mostly one or the other: no split needed (a few brownish texels on metal aren't wood)
            if (!red && crystalCount > pixels.Length * 0.97f)
                return null;
            Color32[] plating = Sample(PaPAssets.OriginsMetal, w, h);
            Color32[] glow = Sample(PaPAssets.OriginsCrystalGlow, w, h);
            var main = new Color32[pixels.Length];
            var mask = new Color32[pixels.Length];
            var emission = new Color32[pixels.Length];
            var grey = new Color32(128, 128, 128, 255);
            var black = new Color32(0, 0, 0, 255);
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = plating[i];
                // The plating map is dark; lifted so it reads as worn metal, not a black mirror
                var metal = new Color32((byte)Mathf.Min(255, p.r * 2), (byte)Mathf.Min(255, p.g * 2), (byte)Mathf.Min(255, p.b * 2), 255);
                float c = crystal[i];
                main[i] = Color32.Lerp(metal, grey, c);
                mask[i] = new Color32(255, 255, 255, (byte)(c * 255f));
                emission[i] = Color32.Lerp(black, glow[i], c);
            }
            PaPHideoutModulePlugin.Log.LogInfo($"[camo] Origins split of {original.name}: {crystalCount * 100 / pixels.Length}% crystal ({(red ? "red" : "not wood")})");
            return new Split { Main = Make(main, w, h, "main"), Mask = Make(mask, w, h, "mask"), Emission = Make(emission, w, h, "glow") };
        }

        private static Texture2D Make(Color32[] pixels, int w, int h, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "pap_origins_split_" + name, wrapMode = TextureWrapMode.Repeat };
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Our own textures ship readable: sampled on the CPU (a GPU read of them came back black in game).</summary>
        private static Color32[] Sample(Texture2D tex, int w, int h)
        {
            if (!tex.isReadable)
                return Read(tex, w, h);
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = tex.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
            return pixels;
        }

        /// <summary>Box blur, separable, radius r texels, wrapping (UVs repeat).</summary>
        private static float[] Blur(float[] src, int w, int h, int r)
        {
            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            float n = 2 * r + 1;
            for (int y = 0; y < h; y++)
            {
                float sum = 0f;
                for (int k = -r; k <= r; k++)
                    sum += src[y * w + ((k % w) + w) % w];
                for (int x = 0; x < w; x++)
                {
                    tmp[y * w + x] = sum / n;
                    sum += src[y * w + (x + r + 1) % w] - src[y * w + ((x - r) % w + w) % w];
                }
            }
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                for (int k = -r; k <= r; k++)
                    sum += tmp[(((k % h) + h) % h) * w + x];
                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = sum / n;
                    sum += tmp[((y + r + 1) % h) * w + x] - tmp[(((y - r) % h + h) % h) * w + x];
                }
            }
            return dst;
        }

        /// <summary>A texture's pixels at the given size, through the GPU (game textures aren't readable).</summary>
        private static Color32[] Read(Texture source, int w, int h)
        {
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                read.Apply(false);
                Color32[] pixels = read.GetPixels32();
                Object.Destroy(read);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static int PaintKindRaw(Material mat, Item owner, Renderer renderer)
        {
            // Origins: named parts of a mod gun (a dial's needle, a knob...) get their own look, whatever their material
            if (renderer != null && OriginsPartRoles.Count > 0 && mat.shader != null && BaseColorTexture(mat) != 0 && mat.renderQueue <= 2450 && OnOriginsGun(owner))
            {
                string part = renderer.name.ToLowerInvariant();
                // A dial's face keeps its own colours
                if (mat.name.ToLowerInvariant().Contains("meter"))
                    return Skip(mat, "dial face");
                foreach (var (word, crystal) in OriginsPartRoles)
                {
                    if (part.Contains(word))
                        return Note(mat, crystal ? 1 : 2);
                }
            }
            bool cutoutOk = mat.renderQueue == 2450 && OriginsCutouts.Any(w => mat.name.ToLowerInvariant().Contains(w)) && OnOriginsGun(owner);
            if (mat.shader == null || (mat.renderQueue >= 2450 && !cutoutOk) || BaseColorTexture(mat) == 0)
                return Skip(mat, mat.shader == null ? "no shader" : mat.renderQueue >= 2450 ? "transparent queue " + mat.renderQueue : "no colour texture, shader " + mat.shader.name);
            string shader = mat.shader.name.ToLowerInvariant();
            if (SkipShaderWords.Any(shader.Contains))
                return Skip(mat, "shader " + mat.shader.name);
            bool mounted = owner != null && !(owner is Weapon);
            string ownerKind = mounted ? owner.GetType().Name.ToLowerInvariant() : "";
            bool optic = SkipOwnerWords.Any(ownerKind.Contains) || (mounted && InCategory(owner, SightsCategory));
            // Material and texture names are the only hint of what a part is made of
            string name = mat.name.ToLowerInvariant();
            Texture main = mat.GetTexture(BaseColorTexture(mat));
            if (main != null)
                name += " " + main.name.ToLowerInvariant();
            bool nonMetal = !AlwaysPaintWords.Any(name.Contains) && NonMetalWords.Any(name.Contains);
            bool origins = OnOriginsGun(owner);
            if (!origins && PlainWords.Any(name.Contains))
                return Skip(mat, "kept plain by its mod");

            if (!origins)
            {
                // Attachments (sights, scopes, lasers, lights, grips, muzzle devices, mounts, magazines) keep their look;
                // the gun itself and its structure (barrel, handguard, stock, pistol grip, gas block...) are painted
                if (mounted && !StructuralSlots.Contains(SlotOf(owner)) && !AlwaysPaintWords.Any(name.Contains))
                    return Skip(mat, "attachment (" + SlotOf(owner) + ")");
                // Magazines keep their own look on the default camo (a mod gun's own parts opt in through KeepMaterialsPlain)
                if (owner is Magazine && !AlwaysPaintWords.Any(name.Contains))
                    return Skip(mat, "magazine");
                if (optic)
                    return Skip(mat, "mounted " + ownerKind);
                if (nonMetal)
                    return Skip(mat, "not metal");
                return Note(mat, 0);
            }
            if (OriginsPlainWords.Any(name.Contains))
                return Skip(mat, "plain on Origins guns");
            foreach (var (word, crystal) in OriginsRoles)
            {
                if (name.Contains(word))
                    return Note(mat, crystal ? 1 : 2);
            }
            // Scopes and magazines are metal plating on an Origins gun; lights and lasers keep their look
            if (optic)
            {
                if (ownerKind.Contains("light") || ownerKind.Contains("laser"))
                    return Skip(mat, "mounted " + ownerKind);
                // The long sniper-type scopes take the crystal; every other sight is metal plating
                return Note(mat, InCategory(owner, ScopeCategory) ? 1 : 2);
            }
            string slotId = SlotOf(owner);
            if (mounted && (slotId == "mod_barrel" || slotId.StartsWith("mod_reciever") || slotId.StartsWith("mod_receiver")))
                return Note(mat, 1);
            return Note(mat, mounted || nonMetal || owner is Magazine || name.Contains("wood") ? 2 : 1);
        }

        internal static bool OnOriginsGun(Item item)
        {
            var seen = new HashSet<Item>();
            for (; item != null && seen.Add(item) && seen.Count < 32; item = item.CurrentAddress?.Container?.ParentItem)
            {
                if (item is Weapon && PaPRegistry.IsInfused(item.Id))
                    return PaPOrigins.Has(item.Id);
            }
            return false;
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
