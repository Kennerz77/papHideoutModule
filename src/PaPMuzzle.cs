using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT.InventoryLogic;
using Systems.Effects;
using UnityEngine;

namespace PaPHideoutModule
{
    /// <summary>
    /// 115-coloured muzzle flash on infused weapons. Each weapon model owns its jet material (the game copies it per
    /// model) and its muzzle light, so those are tinted per gun. The flash particles are shared by every gun in the
    /// world but restarted on every shot, so they're tinted for an infused shot and put back for the next plain one.
    /// </summary>
    internal static class PaPMuzzle
    {
        private static readonly Color DefaultLight = new Color(1f, 0.65f, 0.45f); // what MuzzleLight.Start sets

        private class Tint
        {
            public Weapon Weapon;
            public Material Jet;
            public Dictionary<int, Color> JetColors;
            public bool Tinted;
        }

        private static readonly Dictionary<MuzzleManager, Tint> Managers = new Dictionary<MuzzleManager, Tint>();
        private static List<(ParticleSystem system, ParticleSystem.MinMaxGradient color)> _shared;
        private static bool _sharedTinted;

        /// <summary>Called right before a muzzle fires its effects.</summary>
        public static void BeforeShot(MuzzleManager muzzle)
        {
            Tint tint = Get(muzzle);
            bool infused = PaPHideoutModulePlugin.RedMuzzleFlash && tint.Weapon != null && PaPRegistry.IsInfused(tint.Weapon.Id);
            Color color = PaPHideoutModulePlugin.MuzzleFlashColor;

            if (infused || tint.Tinted)
                TintJet(muzzle, tint, infused, color);
            if (muzzle.Light != null && muzzle.Light.Lights != null)
            {
                foreach (Light light in muzzle.Light.Lights)
                {
                    if (light != null)
                        light.color = infused ? color : DefaultLight;
                }
            }
            TintShared(infused, color);
            tint.Tinted = infused;
        }

        private static Tint Get(MuzzleManager muzzle)
        {
            if (Managers.TryGetValue(muzzle, out Tint tint))
            {
                // A pooled model may come back for another weapon
                WeaponPrefab prefab = muzzle.GetComponentInParent<WeaponPrefab>();
                tint.Weapon = prefab != null ? WeaponOf(prefab) : null;
                return tint;
            }

            if (Managers.Count > 64)
            {
                foreach (MuzzleManager dead in Managers.Keys.Where(m => m == null).ToList())
                    Managers.Remove(dead);
            }

            WeaponPrefab owner = muzzle.GetComponentInParent<WeaponPrefab>();
            tint = new Tint { Weapon = owner != null ? WeaponOf(owner) : null };
            Managers[muzzle] = tint;
            return tint;
        }

        private static Weapon WeaponOf(WeaponPrefab prefab) =>
            HarmonyLib.Traverse.Create(prefab).Field("_weaponData").GetValue<Weapon>();

        private static void TintJet(MuzzleManager muzzle, Tint tint, bool on, Color color)
        {
            Material jet = muzzle.JetMaterial;
            if (jet == null)
                return;
            if (tint.Jet != jet)
            {
                // Remember the material's own colours once, so a plain shot can put them back
                tint.Jet = jet;
                tint.JetColors = new Dictionary<int, Color>();
                Shader shader = jet.shader;
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
                    {
                        int id = shader.GetPropertyNameId(i);
                        tint.JetColors[id] = jet.GetColor(id);
                    }
                }
            }

            foreach (var pair in tint.JetColors)
                jet.SetColor(pair.Key, on ? Recolor(pair.Value, color) : pair.Value);
        }

        private static void TintShared(bool on, Color color)
        {
            if (!on && !_sharedTinted)
                return;

            Effects effects = Singleton<Effects>.Instance;
            if (effects == null || effects.MuzzleEffect.CommonSystems == null)
                return;

            if (_shared == null || _shared.Any(s => s.system == null))
            {
                _shared = effects.MuzzleEffect.CommonSystems
                    .Where(c => c != null && c.RootParticleSystem != null)
                    .SelectMany(c => c.RootParticleSystem.GetComponentsInChildren<ParticleSystem>(true))
                    .Select(ps => (ps, ps.main.startColor))
                    .ToList();
            }

            foreach (var (system, original) in _shared)
            {
                ParticleSystem.MainModule main = system.main;
                main.startColor = on ? Recolor(original, color) : original;
            }
            _sharedTinted = on;
        }

        private static ParticleSystem.MinMaxGradient Recolor(ParticleSystem.MinMaxGradient original, Color color)
        {
            switch (original.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(Recolor(original.color, color));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(Recolor(original.colorMin, color), Recolor(original.colorMax, color));
                default:
                    // Gradients: a flat tint keeps it simple and still reads as the 115 colour
                    return new ParticleSystem.MinMaxGradient(color);
            }
        }

        /// <summary>Same brightness and alpha, new hue: a hot orange flash becomes an equally hot red one.</summary>
        private static Color Recolor(Color original, Color color)
        {
            float brightness = Mathf.Max(original.r, original.g, original.b);
            float target = Mathf.Max(color.r, color.g, color.b, 0.001f);
            Color result = color * (brightness / target);
            result.a = original.a;
            return result;
        }
    }
}
