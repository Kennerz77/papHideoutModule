using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PaP.Editor
{
    /// <summary>
    /// Dev: an example picture of a game weapon wearing the Pack-a-Punch default camo, by the module's own paint rules
    /// (PaPCamo: the gun and its structural parts painted, attachments, magazines, optics, soft and see-through
    /// materials left plain; the look's colour, spec and glow maps and its shine settings). The weapon is put together
    /// from the game's own bundles, as the game does: each part's model hung on its parent's slot. The build (parts,
    /// slots, bundles) comes from 99_Tools\eft_preset_build.py.
    /// Run: Unity -batchmode -executeMethod PaP.Editor.PaPCamoPreview.Run -previewBuild build.json -previewOut out.png
    ///      -previewLook chronicles|classic
    /// </summary>
    public static class PaPCamoPreview
    {
        [Serializable] private class Part { public string id, parent, slot, tpl, name, bundle, asset; public bool magazine, iron; }
        [Serializable] private class Build { public string preset; public string[] bundles; public Part[] parts; }

        private static readonly HashSet<string> StructuralSlots = new HashSet<string>
        {
            "mod_barrel", "mod_handguard", "mod_stock", "mod_stock_000", "mod_stock_001", "mod_stock_002", "mod_stock_akms", "mod_pistol_grip", "mod_pistol_grip_akms",
            "mod_gas_block", "mod_reciever", "mod_receiver", "mod_charge", "mod_bolt", "mod_trigger", "mod_hammer", "mod_catch", "mod_mount_000", "mod_equipment",
        };
        private static readonly string[] SkipShaderWords = { "lens", "reticle", "collimator", "glass", "optic", "laser", "particle", "flash", "sprite", "transparent", "additive" };
        private static readonly string[] NonMetalWords = { "plastic", "polymer", "rubber", "nylon", "foam", "furniture", "fabric", "leather", "pad", "grip" };
        private static readonly string[] SoftWords = { "rubber", "foam", "fabric", "leather", "pad", "cloth", "strap" };
        private static readonly string[] AmmoWords = { "patron", "bullet", "cartridge" };

        private static string Arg(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        public static void Run()
        {
            var log = new List<string>();
            int exit = 0;
            try
            {
                Render(Arg("-previewBuild", ""), Arg("-previewOut", "preview.png"), Arg("-previewLook", "chronicles"), log);
            }
            catch (Exception e)
            {
                log.Add("FAILED: " + e);
                exit = 1;
            }
            foreach (string line in log)
                Debug.Log("[PaPPreview] " + line);
            AssetBundle.UnloadAllAssetBundles(true);
            EditorApplication.Exit(exit);
        }

        private static void Render(string buildPath, string outPath, string look, List<string> log)
        {
            Build build = JsonUtility.FromJson<Build>(File.ReadAllText(buildPath));
            AssetBundle.UnloadAllAssetBundles(true);
            var bundles = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in build.bundles)
            {
                AssetBundle b = AssetBundle.LoadFromFile(path);
                if (b != null)
                    bundles[Path.GetFullPath(path)] = b;
                else
                    log.Add("bundle not loaded: " + path);
            }

            // The parts, each on its parent's slot
            var made = new Dictionary<string, GameObject>();
            var partOf = new Dictionary<GameObject, Part>();
            GameObject root = null;
            foreach (Part p in build.parts)
            {
                if (!bundles.TryGetValue(Path.GetFullPath(p.bundle), out AssetBundle b))
                {
                    log.Add($"no bundle for {p.name}");
                    continue;
                }
                GameObject prefab = b.LoadAsset<GameObject>(p.asset) ?? b.LoadAllAssets<GameObject>().FirstOrDefault();
                if (prefab == null)
                {
                    log.Add($"no model in {p.bundle}");
                    continue;
                }
                if (Arg("-previewDump", "") == "1")
                    Dump(prefab.transform, 0, log);
                GameObject go = UnityEngine.Object.Instantiate(prefab);
                go.name = p.name;
                made[p.id] = go;
                partOf[go] = p;
                if (string.IsNullOrEmpty(p.parent) || !made.TryGetValue(p.parent, out GameObject parent))
                {
                    root = root ?? go;
                    continue;
                }
                // The slot nearest the part's root: a part's LOD meshes carry a second, turned copy of its slots
                Transform slot = parent.GetComponentsInChildren<Transform>(true).Where(t => t.name == p.slot)
                    .OrderBy(t => { int d = 0; for (Transform u = t; u != null && u != parent.transform; u = u.parent) d++; return d; })
                    .FirstOrDefault();
                if (slot == null)
                {
                    log.Add($"no slot {p.slot} on {parent.name} for {p.name}");
                    slot = parent.transform;
                }
                // On the slot, turned back by its own mesh's turn: every part's meshes sit a quarter turn (the import's)
                // off its root, and the slots are in that mesh frame, so a part's root on a slot takes the opposite
                // turn and its meshes line up with its parent's
                go.transform.SetParent(slot, false);
                go.transform.localPosition = Vector3.zero;
                Transform mesh = prefab.transform.Cast<Transform>().FirstOrDefault(c => c.GetComponent<MeshRenderer>() != null || c.GetComponent<SkinnedMeshRenderer>() != null);
                go.transform.localRotation = mesh != null ? Quaternion.Inverse(mesh.localRotation) : Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }
            if (root == null)
                throw new Exception("nothing was made");
            // The look's maps (the module's bundle textures)
            bool chronicles = look.StartsWith("chron", StringComparison.OrdinalIgnoreCase);
            Texture2D albedo = Tex(chronicles ? "pap_camo_chronicles_c" : "pap_camo_albedo");
            Texture2D spec = Tex(chronicles ? "pap_camo_chronicles_s" : "pap_camo_spec");
            Texture2D glowMap = chronicles ? spec : albedo;
            float tiling = 0.5f * (chronicles ? 2f : 1f), glow = chronicles ? 0.15f : 0.25f;

            int painted = 0, plain = 0;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Part owner = OwnerOf(r.transform, partOf);
                bool mounted = owner != null && !string.IsNullOrEmpty(owner.parent);
                bool ammo = Enumerable.Range(0, 1).Any(_ => { for (Transform t = r.transform; t != null; t = t.parent) if (AmmoWords.Any(t.name.ToLowerInvariant().Contains)) return true; return false; });
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string why = ammo ? "ammo" : Why(mats[i], owner, mounted);
                    if (why != null)
                    {
                        plain++;
                        log.Add($"plain: {mats[i]?.name} on {owner?.name} ({why})");
                        continue;
                    }
                    mats[i] = Painted(mats[i], albedo, spec, glowMap, tiling, glow, chronicles);
                    painted++;
                }
                r.sharedMaterials = mats;
            }
            log.Add($"{build.preset}: {painted} material slots painted, {plain} left plain ({look})");

            Shoot(root, outPath, $"{build.preset} - PaP default camo: {(chronicles ? "Chronicles copper circuit" : "Classic")} (the module's rules)", log);
        }

        private static void Dump(Transform t, int depth, List<string> log)
        {
            if (depth > 6 || t.name.StartsWith("Base Human") || t.name.Contains("Digit") || t.name.StartsWith("Bip"))
                return;
            var comps = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));
            log.Add($"dump {new string(' ', depth * 2)}{t.name}  pos {t.localPosition:F3} rot {t.localRotation.eulerAngles:F0} [{comps}]");
            foreach (Transform c in t)
                Dump(c, depth + 1, log);
        }

        private static Texture2D Tex(string name) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/PaP/Camo/{name}.png");

        private static Part OwnerOf(Transform t, Dictionary<GameObject, Part> partOf)
        {
            for (; t != null; t = t.parent)
                if (partOf.TryGetValue(t.gameObject, out Part p))
                    return p;
            return null;
        }

        /// <summary>Why a material stays plain under the default look (PaPCamo.PaintKindRaw's rules), or null to paint it.</summary>
        private static string Why(Material mat, Part owner, bool mounted)
        {
            if (mat == null || mat.shader == null)
                return "no shader";
            if (mat.renderQueue >= 2450)
                return "transparent queue " + mat.renderQueue;
            int main = new[] { "_MainTex", "_BaseMap", "_BaseColorMap" }.Select(Shader.PropertyToID).FirstOrDefault(mat.HasProperty);
            if (main == 0)
                return "no colour texture";
            string shader = mat.shader.name.ToLowerInvariant();
            if (SkipShaderWords.Any(shader.Contains))
                return "shader " + mat.shader.name;
            string name = mat.name.ToLowerInvariant();
            Texture tex = mat.GetTexture(main);
            if (tex != null)
                name += " " + tex.name.ToLowerInvariant();
            bool structural = !mounted || StructuralSlots.Contains(owner.slot);
            if (mounted && !owner.iron && !structural)
                return "attachment (" + owner.slot + ")";
            if (owner != null && owner.magazine)
                return "magazine";
            bool nonMetal = NonMetalWords.Any(name.Contains);
            if (nonMetal && !(structural && !SoftWords.Any(name.Contains)))
                return "not metal";
            return null;
        }

        /// <summary>The painted copy, as PaPCamo.PaintedFor makes it for the default look.</summary>
        private static Material Painted(Material original, Texture2D albedo, Texture2D spec, Texture2D glowMap, float tiling, float glow, bool shiny)
        {
            var m = new Material(original) { name = original.name + " (115)" };
            int main = new[] { "_MainTex", "_BaseMap", "_BaseColorMap" }.Select(Shader.PropertyToID).First(m.HasProperty);
            m.SetTexture(main, albedo);
            m.SetTextureScale(main, new Vector2(tiling, tiling));
            m.SetTextureOffset(main, Vector2.zero);
            foreach (string c in new[] { "_Color", "_BaseColor" })
                if (m.HasProperty(c))
                    m.SetColor(c, Color.white);
            if (m.HasProperty("_HasTint"))
                m.SetFloat("_HasTint", 0f);
            if (m.HasProperty("_BaseTintColor"))
                m.SetColor("_BaseTintColor", Color.white);
            if (spec != null && m.HasProperty("_SpecMap"))
            {
                m.SetTexture("_SpecMap", spec);
                m.SetTextureScale("_SpecMap", new Vector2(tiling, tiling));
            }
            if (m.HasProperty("_Specularness"))
                m.SetFloat("_Specularness", shiny ? 1.7f : 1.1f);
            if (m.HasProperty("_Glossness"))
                m.SetFloat("_Glossness", shiny ? 2.2f : 1.6f);
            if (m.HasProperty("_ReflectColor"))
                m.SetColor("_ReflectColor", shiny ? new Color(0.3f, 0.32f, 0.38f, 0.8f) : new Color(0.12f, 0.13f, 0.17f, 0.5f));
            foreach (string g in new[] { "_Glossiness", "_Smoothness" })
                if (m.HasProperty(g))
                    m.SetFloat(g, shiny ? 0.93f : 0.8f);
            if (m.HasProperty("_Metallic"))
                m.SetFloat("_Metallic", 0.1f);
            if (m.HasProperty("_SpecColor"))
                m.SetColor("_SpecColor", shiny ? new Color(0.8f, 0.8f, 0.85f, 1f) : new Color(0.55f, 0.55f, 0.6f, 1f));
            if (m.HasProperty("_EmissionMap") && m.HasProperty("_EmissionColor"))
            {
                m.SetTexture("_EmissionMap", glowMap);
                m.SetTextureScale("_EmissionMap", new Vector2(tiling, tiling));
                m.SetColor("_EmissionColor", new Color(glow, glow, glow, 1f));
                m.EnableKeyword("_EMISSION");
            }
            return m;
        }

        /// <summary>From the left side, muzzle to the left as the game pictures its guns, framed tight on a grey backdrop.</summary>
        private static void Shoot(GameObject root, string outPath, string title, List<string> log)
        {
            Renderer[] rs = root.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)).ToArray();
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs.Skip(1))
                b.Encapsulate(r.bounds);
            Transform fireport = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "fireport");
            // The gun's length along its longest axis, the muzzle end to the left
            Vector3 size = b.size;
            Vector3 along = size.x >= size.y && size.x >= size.z ? Vector3.right : size.z >= size.y ? Vector3.forward : Vector3.up;
            if (fireport != null && Vector3.Dot(fireport.position - b.center, along) > 0f)
                along = -along; // screen right points away from the muzzle
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(up, along)) > 0.9f)
                up = Vector3.forward;
            Vector3 forward = Vector3.Cross(along, up).normalized; // looking at the gun's side
            up = Vector3.Cross(forward, along).normalized;

            var lights = new List<GameObject>();
            foreach (var (dir, intensity) in new[] { (Quaternion.LookRotation(forward + Vector3.down * 0.6f + along * 0.3f), 1.1f), (Quaternion.LookRotation(-forward + Vector3.down * 0.3f), 0.4f) })
            {
                var lg = new GameObject("light");
                Light l = lg.AddComponent<Light>();
                l.type = LightType.Directional;
                l.intensity = intensity;
                lg.transform.rotation = dir;
                lights.Add(lg);
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.46f, 0.5f);

            int width = 1600;
            float length = Vector3.Dot(size, new Vector3(Mathf.Abs(along.x), Mathf.Abs(along.y), Mathf.Abs(along.z)));
            float height = Vector3.Dot(size, new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z)));
            int h = Mathf.Max(200, Mathf.RoundToInt(width * height / length * 1.25f));
            var camGo = new GameObject("cam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = height * 0.62f;
            cam.aspect = (float)width / h;
            if (length * 0.54f > cam.orthographicSize * cam.aspect)
                cam.orthographicSize = length * 0.54f / cam.aspect;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.21f, 0.23f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 50f;
            cam.transform.rotation = Quaternion.LookRotation(forward, up);
            cam.transform.position = b.center - forward * 5f;
            var rt = new RenderTexture(width, h, 24) { antiAliasing = 8 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            log.Add($"wrote {outPath} ({width}x{h}): {title}");
            UnityEngine.Object.DestroyImmediate(camGo);
            foreach (GameObject lg in lights)
                UnityEngine.Object.DestroyImmediate(lg);
        }
    }
}
