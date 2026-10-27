using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PaPHideoutModule
{
    /// <summary>
    /// The 115-infused rock fragments from Mechanic's "115 Riddles" (the server mod defines them): the five quest
    /// items found on Factory and the five ordinary ones he sends back. Both get the 115 look in the inventory.
    /// </summary>
    internal static class PaPFragments
    {
        // Same ids as the server's PaPQuestInfo: 6650a115a115a115a115f101..f105 (quest) and f201..f205 (returned)
        private static readonly HashSet<string> Templates = BuildTemplates();

        private static HashSet<string> BuildTemplates()
        {
            var ids = new HashSet<string>();
            for (int i = 1; i <= 5; i++)
            {
                ids.Add($"6650a115a115a115a115f1{i:D2}");
                ids.Add($"6650a115a115a115a115f2{i:D2}");
            }
            return ids;
        }

        public static bool Is(Item item) => item != null && Templates.Contains(item.TemplateId.ToString());

        /// <summary>0..4 from the template's last two digits (..f101 is the first, ..f205 the fifth).</summary>
        private static int IndexOf(Item item)
        {
            string tpl = item.TemplateId.ToString();
            return int.TryParse(tpl.Substring(tpl.Length - 2), out int n) ? n - 1 : 0;
        }

        private const string RockName = "PaP_Rock";

        /// <summary>The bronze pocket watch the fragments were cloned from: its pooled models are shared with them.</summary>
        private const string CloneSource = "5937fd0086f7742bf33fc198";

        /// <summary>True for items whose model may be one a fragment dressed up (the real bronze watch).</summary>
        public static bool SharesModel(Item item) => item != null && item.TemplateId.ToString() == CloneSource;

        /// <summary>
        /// Turns an item model of a fragment (built from the bronze watch it was cloned from) into its 115 rock: the
        /// watch draws nothing, the rock goes in at the scale its spawn was placed at, and the pickup collider is
        /// fitted to the rock. <paramref name="live"/> adds the pulsing glow, light and hum (not for icon photos).
        /// Models come from a pool, so this also re-dresses one used before, as a different fragment or for an icon.
        /// </summary>
        public static void Dress(GameObject model, Item item, bool live)
        {
            if (model == null || !Is(item))
                return;
            int index = IndexOf(item);
            PaPRockDress dress = model.GetComponent<PaPRockDress>() ?? model.AddComponent<PaPRockDress>();
            dress.Wear(item, live);

            // Bundle still preloading: the watch stays as it is and the dress retries once the rock can load
            GameObject prefab = PaPAssets.Rock(index);
            if (prefab == null)
            {
                dress.Pending = true;
                return;
            }
            dress.Pending = false;
            dress.HideWatch();

            Transform existing = FindRock(model);
            if (existing != null && existing.GetComponent<PaPRockModel>()?.Index != index)
            {
                Discard(existing);
                existing = null;
            }
            GameObject rock = existing != null ? existing.gameObject : Object.Instantiate(prefab, model.transform, false);
            rock.name = RockName;
            if (!rock.activeSelf)
                rock.SetActive(true);
            (rock.GetComponent<PaPRockModel>() ?? rock.AddComponent<PaPRockModel>()).Index = index;
            float scale = PaPRegistry.FragmentScales.TryGetValue(item.TemplateId.ToString(), out float s) && s > 0f ? s : 1f;
            rock.transform.localScale = Vector3.one * scale;
            // Icon and preview cameras only see their own layer
            foreach (Transform t in rock.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = model.layer;
            foreach (Renderer renderer in rock.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = true;

            PaPRockGlow glow = rock.GetComponent<PaPRockGlow>() ?? rock.AddComponent<PaPRockGlow>();
            glow.SetLive(live, scale);

            dress.FitColliders(rock);
        }

        /// <summary>The fragment's pre-rendered inventory picture, or null (an older bundle, or still loading).</summary>
        public static Texture2D IconImage(Item item) => Is(item) ? PaPAssets.RockIcon(IndexOf(item)) : null;

        /// <summary>A pooled model a fragment dressed, going to a real bronze watch: put the watch back.</summary>
        public static void Undress(GameObject model)
        {
            if (model == null)
                return;
            Transform rock = FindRock(model);
            if (rock != null)
                Discard(rock);
            model.GetComponent<PaPRockDress>()?.Restore();
        }

        /// <summary>The live rock on a model; one being destroyed this frame is renamed, so it's never found.</summary>
        internal static Transform FindRock(GameObject model) => model.transform.Find(RockName);

        // Destroy waits for the end of the frame; rename and hide first, so a re-dress in the same frame neither
        // reuses it nor sees it drawn
        private static void Discard(Transform rock)
        {
            rock.name = RockName + "_Discarded";
            rock.gameObject.SetActive(false);
            Object.Destroy(rock.gameObject);
        }
    }

    /// <summary>Which fragment a rock child shows, so a pooled model reused for another fragment gets the right one.</summary>
    internal class PaPRockModel : MonoBehaviour
    {
        public int Index;
    }

    /// <summary>
    /// What a fragment changed on the bronze-watch model it wears: the watch's materials (emptied, so nothing of it
    /// draws whatever the game's culling switches on) and its colliders (fitted to the rock). Kept so the model can
    /// be handed back to a real watch.
    /// </summary>
    internal class PaPRockDress : MonoBehaviour
    {
        private readonly Dictionary<Renderer, Material[]> _materials = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Collider, (Vector3 centre, Vector3 size)> _colliders = new Dictionary<Collider, (Vector3, Vector3)>();

        private static readonly Material[] NoMaterials = new Material[0];
        private const float CheckInterval = 0.5f;

        private Item _item;
        private bool _live;
        private float _nextCheck;

        /// <summary>True while the rock couldn't be built yet (bundle still loading); the check retries it.</summary>
        public bool Pending { get; set; }

        public void Wear(Item item, bool live)
        {
            _item = item;
            _live = live;
            enabled = true;
        }

        public void HideWatch()
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (InRock(renderer.transform))
                {
                    // Taken for watch by an older pass: give the rock its materials back
                    if (_materials.TryGetValue(renderer, out Material[] own))
                    {
                        renderer.sharedMaterials = own;
                        _materials.Remove(renderer);
                    }
                    continue;
                }
                // The first materials seen are the watch's own; later ones (put back by the pool) are hidden again
                if (!_materials.ContainsKey(renderer))
                    _materials[renderer] = renderer.sharedMaterials;
                if (renderer.sharedMaterials.Length > 0)
                    renderer.sharedMaterials = NoMaterials;
            }
        }

        // Walked by hand: GetComponentInParent finds nothing while the model is inactive, and loose loot is dressed
        // inactive in the pool. That made the rock count as watch and lose its materials (the invisible Factory rocks)
        private bool InRock(Transform t)
        {
            for (; t != null && t != transform; t = t.parent)
            {
                if (t.GetComponent<PaPRockModel>() != null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Self-repair while the model is out: builds the rock once the bundle is ready, and puts it back if the
        /// pool or the game took it off or showed the watch again. Pooled models sitting in the pool are inactive and
        /// don't run this.
        /// </summary>
        private void Update()
        {
            if (_item == null || Time.unscaledTime < _nextCheck)
                return;
            _nextCheck = Time.unscaledTime + CheckInterval;
            try
            {
                Transform rock = PaPFragments.FindRock(gameObject);
                if (Pending || rock == null || !rock.gameObject.activeSelf || WatchShowing() || RockBlank(rock))
                    PaPFragments.Dress(gameObject, _item, _live);
            }
            catch (System.Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP fragment re-dress failed: {e.Message}");
                _item = null;
            }
        }

        private static bool RockBlank(Transform rock)
        {
            foreach (Renderer renderer in rock.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterials.Length == 0 || !renderer.enabled)
                    return true;
            }
            return false;
        }

        private bool WatchShowing()
        {
            foreach (Renderer renderer in _materials.Keys)
            {
                if (renderer != null && renderer.sharedMaterials.Length > 0)
                    return true;
            }
            return false;
        }

        /// <summary>The watch's colliders, resized to the rock, so it can be picked up by looking at the rock.</summary>
        public void FitColliders(GameObject rock)
        {
            Renderer rockRenderer = rock.GetComponentInChildren<MeshRenderer>();
            if (rockRenderer == null)
                return;
            Bounds world = rockRenderer.bounds;
            foreach (Collider collider in GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || collider.transform.IsChildOf(rock.transform))
                    continue;
                Transform t = collider.transform;
                Vector3 centre = t.InverseTransformPoint(world.center);
                Vector3 size = t.InverseTransformVector(world.size);
                size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                switch (collider)
                {
                    case BoxCollider box:
                        if (!_colliders.ContainsKey(box))
                            _colliders[box] = (box.center, box.size);
                        box.center = centre;
                        box.size = size;
                        break;
                    case SphereCollider sphere:
                        if (!_colliders.ContainsKey(sphere))
                            _colliders[sphere] = (sphere.center, Vector3.one * sphere.radius);
                        sphere.center = centre;
                        sphere.radius = Mathf.Max(size.x, size.y, size.z) * 0.5f;
                        break;
                    case CapsuleCollider capsule:
                        if (!_colliders.ContainsKey(capsule))
                            _colliders[capsule] = (capsule.center, new Vector3(capsule.radius, capsule.height, 0f));
                        capsule.center = centre;
                        capsule.radius = Mathf.Max(size.x, size.z) * 0.5f;
                        capsule.height = Mathf.Max(size.y, capsule.radius * 2f);
                        break;
                }
            }
        }

        public void Restore()
        {
            foreach (var pair in _materials)
            {
                if (pair.Key != null)
                    pair.Key.sharedMaterials = pair.Value;
            }
            foreach (var pair in _colliders)
            {
                switch (pair.Key)
                {
                    case BoxCollider box:
                        box.center = pair.Value.centre;
                        box.size = pair.Value.size;
                        break;
                    case SphereCollider sphere:
                        sphere.center = pair.Value.centre;
                        sphere.radius = pair.Value.size.x;
                        break;
                    case CapsuleCollider capsule:
                        capsule.center = pair.Value.centre;
                        capsule.radius = pair.Value.size.x;
                        capsule.height = pair.Value.size.y;
                        break;
                }
            }
            // Kept (inert) rather than destroyed: Destroy waits for the frame's end, and a fragment taking the model
            // back in the same frame would find the dying component and skip hiding the watch
            _materials.Clear();
            _colliders.Clear();
            _item = null;
            Pending = false;
            enabled = false;
        }
    }

    /// <summary>
    /// The 115 inside a fragment is alive: its glowing pockets and light breathe slowly, with a faint shimmer on
    /// top, each rock on its own rhythm, and it hums if pap_rock_hum.wav is there. Icon photos get it still, without
    /// the light (which washed the picture out).
    /// </summary>
    internal class PaPRockGlow : MonoBehaviour
    {
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColor = Shader.PropertyToID("_Color");
        private static readonly int SpecColor = Shader.PropertyToID("_SpecColor");
        private static readonly int Glossiness = Shader.PropertyToID("_Glossiness");
        // The icon studio lights items far brighter than the world (the pale stone came out washed white even at 0.2):
        // for photos the stone is all but black, so the studio light barely touches it and the 115 glows on its own
        private static readonly Color IconDarken = new Color(0.05f, 0.05f, 0.06f, 1f);
        private const float IconGlow = 0.9f;

        // On top of the bundle's values: the 115 pockets read brighter in dim raid lighting
        private const float GlowBoost = 1.8f;
        private const float LightBoost = 1.5f;

        private Material[] _materials = new Material[0];
        private Color[] _emission = new Color[0];
        private Color[] _baseColor = new Color[0], _specColor = new Color[0];
        private float[] _glossiness = new float[0];
        private Light _light;
        private float _lightIntensity, _lightRange;
        private AudioSource _hum;
        private bool _live = true;
        private float _phase;

        /// <summary>A green rock hidden in the hideout: its own, quieter hum volume and range (F12).</summary>
        public bool InHideout { get; set; }

        /// <summary>Investigated (hideout): the hum stops for good; the glow stays.</summary>
        public bool Silenced
        {
            get => _silenced;
            set
            {
                _silenced = value;
                if (value && _hum != null)
                    _hum.Stop();
            }
        }

        private bool _silenced;

        private float HumVolume => InHideout ? PaPHideoutModulePlugin.HideoutRockHumVolume.Value : PaPHideoutModulePlugin.RockHumVolume.Value;

        private float HumDistance => InHideout ? PaPHideoutModulePlugin.HideoutRockHumDistance : PaPHideoutModulePlugin.RockHumDistance;

        /// <summary>Live (in the world, the inspect view): pulse, light, hum. Not live: still, unlit and quiet.</summary>
        public void SetLive(bool live, float scale)
        {
            Init(); // Awake waits while a pooled model is inactive
            _live = live;
            if (_light != null)
            {
                _light.enabled = live;
                _light.range = _lightRange * scale;
                _light.intensity = _lightIntensity;
            }
            // The icon studio lights far brighter than the world and the Standard shader isn't tuned for it: icons
            // came out washed white. For photos the stone is darkened, the shine taken off and the glow softened
            for (int i = 0; i < _materials.Length; i++)
            {
                Material material = _materials[i];
                material.SetColor(EmissionColor, _emission[i] * (live ? 1f : IconGlow / GlowBoost)); // photos: the blue pockets read on the dark stone
                if (material.HasProperty(BaseColor))
                    material.SetColor(BaseColor, live ? _baseColor[i] : _baseColor[i] * IconDarken);
                if (material.HasProperty(SpecColor))
                    material.SetColor(SpecColor, live ? _specColor[i] : Color.black);
                if (material.HasProperty(Glossiness))
                    material.SetFloat(Glossiness, live ? _glossiness[i] : 0.05f);
            }

            if (live)
                EnsureHum();
            else if (_hum != null)
                _hum.Stop();
        }

        /// <summary>The looping hum, made once the clip is there (the bundle may still be loading when dressed).</summary>
        private void EnsureHum()
        {
            if (_hum != null || _silenced)
                return;
            AudioClip clip = PaPAssets.RockHumClip;
            if (clip == null)
                return;
            _hum = gameObject.AddComponent<AudioSource>();
            _hum.clip = clip;
            _hum.loop = true;
            _hum.playOnAwake = false;
            _hum.spatialBlend = 1f;
            _hum.rolloffMode = AudioRolloffMode.Linear;
            _hum.minDistance = 0.5f;
            _hum.maxDistance = HumDistance;
            _hum.dopplerLevel = 0f;
            _hum.volume = HumVolume;
            _hum.time = Random.value * clip.length; // rocks near each other don't hum in step
        }

        private bool _ready;

        private void Awake() => Init();

        private void Init()
        {
            if (_ready)
                return;
            _ready = true;
            var materials = new List<Material>();
            var emission = new List<Color>();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.materials) // own copies, so rocks pulse separately
                {
                    if (!material.HasProperty(EmissionColor))
                        continue;
                    materials.Add(material);
                    emission.Add(material.GetColor(EmissionColor) * GlowBoost);
                    material.EnableKeyword("_EMISSION");
                }
            }
            _materials = materials.ToArray();
            _emission = emission.ToArray();
            _baseColor = _materials.Select(m => m.HasProperty(BaseColor) ? m.GetColor(BaseColor) : Color.white).ToArray();
            _specColor = _materials.Select(m => m.HasProperty(SpecColor) ? m.GetColor(SpecColor) : Color.black).ToArray();
            _glossiness = _materials.Select(m => m.HasProperty(Glossiness) ? m.GetFloat(Glossiness) : 0f).ToArray();
            _light = GetComponentInChildren<Light>(true);
            _lightIntensity = _light != null ? _light.intensity * LightBoost : 0f;
            _lightRange = _light != null ? _light.range : 0f;
            _phase = Random.value * 100f;
        }

        private void Update()
        {
            if (!_live)
                return;
            EnsureHum();
            if (_hum != null && !_silenced)
            {
                // Follows the F12 sliders live
                _hum.volume = HumVolume;
                _hum.maxDistance = HumDistance;
                // Loose loot is dressed while still inactive in the pool, where Play() does nothing: start it once the
                // rock is out in the world
                if (!_hum.isPlaying)
                    _hum.Play();
            }
            float t = Time.time + _phase;
            float breath = 0.78f + 0.22f * Mathf.Sin(t * 1.3f);
            float shimmer = 0.06f * Mathf.Sin(t * 7.1f) * Mathf.Sin(t * 3.3f + 1f);
            float glow = breath + shimmer;
            for (int i = 0; i < _materials.Length; i++)
                _materials[i].SetColor(EmissionColor, _emission[i] * glow);
            if (_light != null && _light.enabled)
                _light.intensity = _lightIntensity * glow;
        }

        private void OnDestroy()
        {
            foreach (Material material in _materials)
                Destroy(material);
        }
    }
}
