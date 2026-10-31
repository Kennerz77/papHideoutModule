using System.Collections;
using System.IO;
using UnityEngine;

namespace PaPHideoutModule
{
    /// <summary>Lazily loads pap_hideout.bundle once and hands out the machine prefab and camo texture.</summary>
    internal static class PaPAssets
    {
        public const string BundleFileName = "pap_hideout.bundle";
        private const string PrefabName = "pap_machine";
        private const string OriginsPrefabName = "pap_machine_origins";
        private const string CamoName = "pap_camo_albedo";

        private static AssetBundle _bundle;
        private static bool _bundleFailed;
        private static GameObject _prefab;
        private static Texture2D _camo;

        public static string BundlePath { get; set; }

        public static AssetBundle Bundle
        {
            get
            {
                if (_bundle != null || _bundleFailed)
                    return _bundle;
                // A second load while the startup preload runs fails, and used to switch the bundle off for the
                // whole session (icons asked for the camo early and came out plain for good); wait for it instead
                if (Preloading)
                    return null;

                if (!File.Exists(BundlePath))
                {
                    PaPHideoutModulePlugin.Log.LogError($"Bundle not found at {BundlePath}");
                    _bundleFailed = true;
                    return null;
                }

                _bundle = AssetBundle.LoadFromFile(BundlePath);
                if (_bundle == null)
                {
                    PaPHideoutModulePlugin.Log.LogError($"Failed to load {BundleFileName}. Was it built with Unity 2022.3.43f1 for StandaloneWindows64?");
                    _bundleFailed = true;
                }
                return _bundle;
            }
        }

        public static bool Preloading { get; private set; }

        /// <summary>Loads the bundle and machine prefab in the background at startup, so the Hideout spawn is instant.</summary>
        public static IEnumerator PreloadAsync()
        {
            if (_bundle != null || _bundleFailed || !File.Exists(BundlePath))
                yield break;

            Preloading = true;
            try
            {
                AssetBundleCreateRequest bundleRequest = AssetBundle.LoadFromFileAsync(BundlePath);
                yield return bundleRequest;
                _bundle = bundleRequest.assetBundle;
                if (_bundle == null)
                    yield break;

                AssetBundleRequest prefabRequest = _bundle.LoadAssetAsync<GameObject>(PrefabName);
                yield return prefabRequest;
                _prefab = prefabRequest.asset as GameObject;
            }
            finally
            {
                Preloading = false;
            }
        }

        public static GameObject MachinePrefab => _prefab != null ? _prefab : _prefab = Load<GameObject>(PrefabName);

        /// <summary>Origins camo: blue crystal for metal, its emissive glow, and the metal plating that furniture and attachments get.</summary>
        public static Texture2D OriginsCrystal => Preloading ? null : Load<Texture2D>("origins_crystal_c");
        public static Texture2D OriginsCrystalNormal => Preloading ? null : Load<Texture2D>("origins_crystal_n");
        public static Texture2D OriginsCrystalGlow => Preloading ? null : Load<Texture2D>("origins_camo_e");
        public static Texture2D OriginsMetal => Preloading ? null : Load<Texture2D>("origins_camo_c");

        private static GameObject _originsPrefab;

        /// <summary>The Origins stone machine (F12 > Machine model), or null if the bundle doesn't have it.</summary>
        public static GameObject OriginsPrefab => _originsPrefab != null ? _originsPrefab : _originsPrefab = Load<GameObject>(OriginsPrefabName);

        /// <summary>BO1's spec map (red-copper traces, gloss in alpha): the default camo's highlights, or null.</summary>
        public static Texture2D CamoSpec => Preloading ? null : _camoSpec != null ? _camoSpec : _camoSpec = Load<Texture2D>("pap_camo_spec");
        private static Texture2D _camoSpec;

        public static Texture2D CamoAlbedo => _camo != null ? _camo : Preloading ? null : _camo = Load<Texture2D>(CamoName);

        /// <summary>True once the camo can be painted; until then icons mustn't be cached as "camo" pictures.</summary>
        public static bool CamoReady => !Preloading && CamoAlbedo != null;

        private static readonly GameObject[] Rocks = new GameObject[5];

        /// <summary>The "115 Riddles" fragment model 0..4 (pap_rock_1..5 in the bundle), or null.</summary>
        public static GameObject Rock(int index)
        {
            if (index < 0 || index >= Rocks.Length)
                return null;
            return Rocks[index] != null ? Rocks[index] : Rocks[index] = Load<GameObject>($"pap_rock_{index + 1}");
        }

        private static readonly System.Collections.Generic.Dictionary<string, AudioClip> Clips =
            new System.Collections.Generic.Dictionary<string, AudioClip>();

        public static AudioClip Clip(string name)
        {
            if (!Clips.TryGetValue(name, out AudioClip clip) || clip == null)
                Clips[name] = clip = Load<AudioClip>(name);
            return clip;
        }

        private static T Load<T>(string name) where T : Object
        {
            if (Bundle == null || Missing.Contains(name))
                return null;
            T asset = Bundle.LoadAsset<T>(name);
            if (asset == null)
            {
                Missing.Add(name);
                PaPHideoutModulePlugin.Log.LogError($"'{name}' not in bundle. Assets: {string.Join(", ", Bundle.GetAllAssetNames())}");
            }
            return asset;
        }

        private static readonly System.Collections.Generic.HashSet<string> Missing = new System.Collections.Generic.HashSet<string>();

        /// <summary>Folder of the plugin DLL; loose assets (area icon, power-on sound) sit next to it.</summary>
        public static string PluginDir => Path.GetDirectoryName(BundlePath);

        private static AudioClip _denyClip;

        /// <summary>BO3's "can't afford it" buzz (pap_deny.wav beside the DLL), for the machine refusing an upgrade.</summary>
        public static AudioClip DenyClip => _denyClip != null ? _denyClip : _denyClip = LooseWav("pap_deny");

        private static AudioClip _acceptClip;

        /// <summary>BO3's purchase "cha-ching" (pap_accept.wav beside the DLL), for a paid upgrade.</summary>
        public static AudioClip AcceptClip => _acceptClip != null ? _acceptClip : _acceptClip = LooseWav("pap_accept");

        /// <summary>A plain 16-bit PCM WAV next to the DLL, built into a clip.</summary>
        private static AudioClip LooseWav(string name)
        {
            string path = Path.Combine(PluginDir, name + ".wav");
            if (!File.Exists(path))
            {
                PaPHideoutModulePlugin.Log.LogWarning($"Sound missing: {path}");
                return null;
            }
            byte[] bytes = File.ReadAllBytes(path);
            int channels = System.BitConverter.ToInt16(bytes, 22), rate = System.BitConverter.ToInt32(bytes, 24);
            // Walk the chunks to "data" (there can be others before it)
            for (int at = 12; at + 8 <= bytes.Length;)
            {
                string id = System.Text.Encoding.ASCII.GetString(bytes, at, 4);
                int size = System.BitConverter.ToInt32(bytes, at + 4);
                if (id == "data")
                {
                    int count = System.Math.Min(size, bytes.Length - at - 8) / 2;
                    var samples = new float[count];
                    for (int i = 0; i < count; i++)
                        samples[i] = System.BitConverter.ToInt16(bytes, at + 8 + i * 2) / 32768f;
                    AudioClip clip = AudioClip.Create(name, count / channels, channels, rate, false);
                    clip.SetData(samples, 0);
                    return clip;
                }
                at += 8 + size + (size & 1);
            }
            return null;
        }

        private static Sprite _areaIcon;

        /// <summary>
        /// The area icon, cropped to its glyph and sized against the vanilla one. Vanilla icons
        /// (<paramref name="reference"/>) are a white glyph the game frames in its own hexagon; ours is drawn
        /// "Area icon size" times that glyph's height (the UI uses native sprite size).
        /// </summary>
        public static Sprite AreaIconLike(Sprite reference)
        {
            if (_areaIcon != null)
                return _areaIcon;
            string path = Path.Combine(PluginDir, "pap_area_icon.png");
            if (!File.Exists(path))
                return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "pap_area_icon" };
            ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false);
            Rect shape = OpaqueRect(texture);

            // On-screen height = glyph height x size: native size is pixels / pixelsPerUnit
            float size = Mathf.Max(0.1f, PaPHideoutModulePlugin.AreaIconSize);
            float ppu = 100f;
            if (reference != null && reference.rect.height > 0f)
                ppu = shape.height * reference.pixelsPerUnit / (reference.rect.height * size);
            _areaIcon = Sprite.Create(texture, shape, new Vector2(0.5f, 0.5f), ppu);
            _areaIcon.name = "pap_area_icon";
            texture.Apply(false, true); // done reading pixels
            return _areaIcon;
        }

        /// <summary>Bounding box of the non-transparent pixels (the whole texture if there are none).</summary>
        private static Rect OpaqueRect(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            int w = texture.width, h = texture.height;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (pixels[y * w + x].a <= 20)
                    continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            return maxX < 0 ? new Rect(0, 0, w, h) : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        // In the bundle like the other machine sounds: loose WAVs next to the DLL, built into clips at runtime, loaded
        // without error but played silent in game
        /// <summary>The 115 fragments' looping hum, or null before the bundle is loaded.</summary>
        public static AudioClip RockHumClip => Preloading ? null : Clip("pap_rock_hum");

        private static readonly Texture2D[] RockIcons = new Texture2D[5];

        /// <summary>The pre-rendered inventory picture of fragment 0..4 (pap_rock_icon_1..5), or null.</summary>
        public static Texture2D RockIcon(int index)
        {
            if (index < 0 || index >= RockIcons.Length || Preloading)
                return null;
            return RockIcons[index] != null ? RockIcons[index] : RockIcons[index] = Load<Texture2D>($"pap_rock_icon_{index + 1}");
        }

        /// <summary>
        /// <paramref name="image"/> as a sprite of exactly the size, pivot and pixels-per-unit of the one the game's icon
        /// studio made (<paramref name="like"/>), so it sits in the cell like any other icon.
        /// </summary>
        public static Sprite SpriteLike(Sprite like, Texture2D image)
        {
            int w = Mathf.Max(1, Mathf.RoundToInt(like.rect.width)), h = Mathf.Max(1, Mathf.RoundToInt(like.rect.height));
            Vector2 pivot = new Vector2(like.pivot.x / like.rect.width, like.pivot.y / like.rect.height);
            Texture2D fitted = image.isReadable ? FitOnCpu(image, w, h) : FitOnGpu(image, w, h);
            return Sprite.Create(fitted, new Rect(0, 0, w, h), pivot, like.pixelsPerUnit);
        }

        // Fit the square picture into the cell's rectangle, centred, keeping its shape (outside it, the picture's
        // transparent edge is clamped outward). Done on the CPU: a GPU blit run from the icon studio's callbacks picks
        // up whatever render state the game left behind, and outside raids that washed the rocks out white
        private static Texture2D FitOnCpu(Texture2D image, int w, int h)
        {
            float side = Mathf.Min(w, h);
            float scaleX = w / side, scaleY = h / side;
            float offsetX = (1f - scaleX) / 2f, offsetY = (1f - scaleY) / 2f;
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = image.GetPixelBilinear((x + 0.5f) / w * scaleX + offsetX, (y + 0.5f) / h * scaleY + offsetY);
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = image.name, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(pixels);
            texture.Apply(false, false); // stays readable: the game reads icons back to cache them, and threw on ours
            return texture;
        }

        /// <summary>For a bundle built before the icons were readable.</summary>
        private static Texture2D FitOnGpu(Texture2D image, int w, int h)
        {
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            try
            {
                float side = Mathf.Min(w, h);
                var scale = new Vector2(w / side, h / side);
                Graphics.Blit(image, rt, scale, new Vector2((1f - scale.x) / 2f, (1f - scale.y) / 2f));
                RenderTexture.active = rt;
                var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = image.name };
                texture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                texture.Apply(false, false);
                return texture;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static GameObject _greenRock;

        /// <summary>The hideout's green 115 rock (BO3's Origins artifact piece), or null.</summary>
        public static GameObject GreenRock => _greenRock != null ? _greenRock : Preloading ? null : _greenRock = Load<GameObject>("pap_green_rock");

        /// <summary>The song the hideout radio plays once the three green rocks are investigated, or null.</summary>
        public static AudioClip SongClip => Preloading ? null : Clip("pap_115_song");

        /// <summary>BO3's sound for picking up a 115 rock, or null (before the bundle loads, or a bundle without it).</summary>
        public static AudioClip RockPickupClip => Preloading ? null : Clip("pap_rock_pickup");

        /// <summary>The machine's start-up sound, or null before the bundle is loaded.</summary>
        public static AudioClip PowerOnClip => Preloading ? null : Clip("pap_power_on");

        /// <summary>Origins' own start-up (zm_tomb's tomb_pap_07); the BO3 one isn't played on that machine.</summary>
        public static AudioClip OriginsPowerOnClip => Preloading ? null : Clip("pap_origins_power_on");

        public static void Unload()
        {
            if (_bundle != null)
                _bundle.Unload(true);
            _bundle = null;
            _prefab = null;
            _camo = null;
        }
    }
}
