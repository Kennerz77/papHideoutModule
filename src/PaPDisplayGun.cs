using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using UnityEngine;

namespace PaPHideoutModule
{
    /// <summary>
    /// The model of the player's weapon that rides through the machine. Built from the real item (attachments
    /// included) the same way the hideout's weapon stands build theirs, laid across the machine's mouth, and
    /// handed back to the game's model pool when done.
    /// </summary>
    internal class PaPDisplayGun
    {
        private readonly Transform _anchor;
        private GameObject _model;
        private List<(Collider collider, bool enabled)> _colliders = new List<(Collider, bool)>();
        private List<(Rigidbody body, bool kinematic, bool detect)> _bodies = new List<(Rigidbody, bool, bool)>();

        public bool Ready => _model != null;

        public PaPDisplayGun(Transform machine)
        {
            // Anchor cancels the machine's scale so the gun stays real size
            _anchor = new GameObject("PaP_DisplayGun").transform;
            _anchor.SetParent(machine, false);
            float scale = machine.lossyScale.x;
            _anchor.localScale = Vector3.one / Mathf.Max(scale, 0.0001f);
        }

        /// <summary>
        /// Builds the model from a throwaway clone of the weapon: a model built from the real (or held) item can be
        /// the very one on the player's arms, and moving that drags the character around.
        /// </summary>
        public IEnumerator Load(Item realWeapon)
        {
            Item weapon = realWeapon.CloneItem();
            // The finished callback must not be null (the game invokes it unchecked), same as the hideout weapon stands
            Task bundles = EasyAssetsExtensions.WaitForAllBundlesJob(weapon.GetAllBundleTokens(), () => { }, CancellationToken.None, null);
            while (!bundles.IsCompleted)
                yield return null;
            if (bundles.IsFaulted || _anchor == null)
            {
                PaPHideoutModulePlugin.Log.LogWarning($"Could not load the model of {weapon.LocalizedName()}: {bundles.Exception?.GetBaseException().Message}");
                yield break;
            }

            ObjectsFactory factory = Singleton<ObjectsFactory>.Instance;
            GameObject model = factory?.CreateItem(weapon, false);
            if (model == null)
            {
                PaPHideoutModulePlugin.Log.LogWarning($"Could not build the model of {weapon.LocalizedName()}.");
                yield break;
            }

            // Purely visual: no physics, nothing for raycasts to hit. Remember exactly how they were: this model goes
            // back to the pool and is often handed straight out again as the gun on the player's arms, where a
            // collider that's normally off pushes against the player's own body (the slide across the floor)
            _colliders = model.GetComponentsInChildren<Collider>(true).Select(c => (c, c.enabled)).ToList();
            _bodies = model.GetComponentsInChildren<Rigidbody>(true).Select(rb => (rb, rb.isKinematic, rb.detectCollisions)).ToList();
            foreach (var (c, _) in _colliders)
                c.enabled = false;
            foreach (var (rb, _, _) in _bodies)
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }

            model.transform.SetParent(null, false);
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.SetActive(true);

            // Lay the barrel across the machine (its longest side along the machine's width), centred on the anchor
            Bounds bounds = WorldBounds(model);
            Quaternion lay = bounds.size.z > bounds.size.x ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
            model.transform.SetParent(_anchor, false);
            model.transform.localRotation = lay;
            model.transform.localPosition = -(lay * bounds.center);

            // Plain on the way in, and untracked so a visuals refresh mid-sequence can't paint it early
            PaPCamo.Apply(model, null);
            PaPCamo.Forget(model);
            _model = model;
        }

        public void SetLocalPosition(Vector3 machineLocal, float bob = 0f)
        {
            _anchor.localPosition = machineLocal + Vector3.up * bob;
        }

        public void SetVisible(bool visible)
        {
            if (_model != null)
                _model.SetActive(visible);
        }

        /// <summary>Camo on (the weapon's own infused state) or off (plain, on the way in).</summary>
        public void ShowCamo(Item weapon, bool on)
        {
            if (_model != null)
                PaPCamo.Apply(_model, on ? weapon : null);
        }

        public void Dispose()
        {
            if (_model != null)
            {
                PaPCamo.Apply(_model, null);
                PaPCamo.Forget(_model);
                foreach (var (c, enabled) in _colliders)
                {
                    if (c != null)
                        c.enabled = enabled;
                }
                foreach (var (rb, kinematic, detect) in _bodies)
                {
                    if (rb == null)
                        continue;
                    rb.isKinematic = kinematic;
                    rb.detectCollisions = detect;
                }

                // The pool hands this model out again (often straight back as the weapon the player redraws),
                // so it must leave exactly as it came: no offset, tilt or scale from riding through the machine
                Transform t = _model.transform;
                t.SetParent(null, false);
                t.localPosition = Vector3.zero;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
                AssetPoolObject.ReturnToPool(_model, true);
                _model = null;
            }
            if (_anchor != null)
                Object.Destroy(_anchor.gameObject);
        }

        private static Bounds WorldBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one * 0.5f);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers.Skip(1))
                bounds.Encapsulate(r.bounds);
            return bounds;
        }
    }
}
