using System;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using Systems.Effects;
using UnityEngine;

namespace PaPHideoutModule
{
    /// <summary>
    /// Bullets from an infused weapon hit in the 115 colour, on every surface: the impact's dust, chips and smoke, its
    /// flash of light, and a small burst of 115 sparks. Hits on people keep their blood.
    ///
    /// EFT plays each impact through EffectsCommutator.PlayHitEffect, which knows the shot. While it runs for an
    /// infused shot, each particle emitted (ParticleSystemAdapter, one particle at a time with its own colour) and the
    /// effect's light take the 115 colour for that emit only, and are put back straight after: the effect objects are
    /// shared by every gun. EFT's own sparks are one GPU mesh for every spark in the scene with no colour per spark,
    /// so those can't be tinted alone: the 115 sparks are ours, drawn on top.
    /// </summary>
    internal static class PaPImpacts
    {
        private static bool _infusedHit;

        public static void Enable(Harmony harmony)
        {
            if (!PaPHideoutModulePlugin.ImpactTint)
                return;
            Hook(harmony, AccessTools.Method(typeof(EffectsCommutator), nameof(EffectsCommutator.PlayHitEffect)), nameof(HitPrefix), nameof(HitPostfix));
            foreach (var m in AccessTools.GetDeclaredMethods(typeof(ParticleSystemAdapter)))
            {
                if (m.Name == nameof(ParticleSystemAdapter.Emit))
                    Hook(harmony, m, nameof(ParticlePrefix), nameof(ParticlePostfix));
            }
            Hook(harmony, AccessTools.Method(typeof(Effects.Effect), nameof(Effects.Effect.Emit)), nameof(EffectPrefix), nameof(EffectPostfix));
        }

        private static void Hook(Harmony harmony, System.Reflection.MethodBase target, string prefix, string postfix)
        {
            try
            {
                if (target == null)
                    throw new MissingMethodException("target not found");
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(PaPImpacts), prefix), postfix: new HarmonyMethod(typeof(PaPImpacts), postfix));
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP impact hook {prefix} failed and is disabled: {e.Message}");
            }
        }

        // ---------- The hit ----------

        private static void HitPrefix(Shot info)
        {
            try
            {
                _infusedHit = info?.Weapon != null && PaPRegistry.IsInfused(info.Weapon.Id) && !(info.HittedBallisticCollider is BodyPartCollider);
            }
            catch (Exception)
            {
                _infusedHit = false;
            }
        }

        private static void HitPostfix(Shot info)
        {
            if (!_infusedHit)
                return;
            _infusedHit = false;
            try
            {
                Sparks.Burst(info.HitPoint, info.HitNormal);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogWarning($"PaP impact sparks skipped: {e.Message}");
            }
        }

        // ---------- Particles and light, for one emit ----------

        private static void ParticlePrefix(ParticleSystemAdapter __instance, out Color32? __state)
        {
            __state = null;
            if (!_infusedHit)
                return;
            __state = __instance.Color;
            __instance.Color = Tint(__instance.Color);
        }

        private static void ParticlePostfix(ParticleSystemAdapter __instance, Color32? __state)
        {
            if (__state.HasValue)
                __instance.Color = __state.Value;
        }

        private static void EffectPrefix(Effects.Effect __instance, out Color? __state)
        {
            __state = null;
            if (!_infusedHit)
                return;
            __state = __instance.LightColor;
            __instance.LightColor = PaPHideoutModulePlugin.TracerColor;
        }

        private static void EffectPostfix(Effects.Effect __instance, Color? __state)
        {
            if (__state.HasValue)
                __instance.LightColor = __state.Value;
        }

        /// <summary>The particle's colour moved most of the way to the 115 hue, at its own brightness and alpha.</summary>
        private static Color32 Tint(Color32 original)
        {
            Color c = original;
            float brightness = Mathf.Max(c.r, c.g, c.b);
            Color hue = PaPHideoutModulePlugin.TracerColor;
            Color target = hue / Mathf.Max(hue.r, hue.g, hue.b, 0.001f) * Mathf.Max(brightness, 0.35f);
            Color tinted = Color.Lerp(c, target, ImpactTintAmount);
            tinted.a = c.a;
            return tinted;
        }

        private const float ImpactTintAmount = 0.85f;

        // ---------- Our 115 sparks ----------

        /// <summary>One shared particle system: short, bright streaks thrown off the hit, falling a little.</summary>
        private static class Sparks
        {
            private static ParticleSystem _system;

            private const int MinCount = 8, MaxCount = 14;

            public static void Burst(Vector3 point, Vector3 normal)
            {
                ParticleSystem ps = System();
                if (ps == null)
                    return;
                Color hot = Color.Lerp(PaPHideoutModulePlugin.TracerColor, Color.white, 0.25f);
                int count = UnityEngine.Random.Range(MinCount, MaxCount + 1);
                for (int i = 0; i < count; i++)
                {
                    // Off the surface in a wide cone
                    Vector3 dir = (normal + UnityEngine.Random.insideUnitSphere * 0.9f).normalized;
                    ps.Emit(new ParticleSystem.EmitParams
                    {
                        position = point + normal * 0.02f,
                        velocity = dir * UnityEngine.Random.Range(1.5f, 5f),
                        startLifetime = UnityEngine.Random.Range(0.12f, 0.35f),
                        startSize = UnityEngine.Random.Range(0.008f, 0.016f),
                        startColor = i % 3 == 0 ? hot : PaPHideoutModulePlugin.TracerColor,
                    }, 1);
                }
            }

            private static ParticleSystem System()
            {
                if (_system != null)
                    return _system;
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    return null;
                var go = new GameObject("PaP 115 impact sparks");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.SetActive(false);
                ParticleSystem ps = go.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = ps.main;
                main.playOnAwake = false;
                main.loop = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = 0.6f;
                main.maxParticles = 600;
                ParticleSystem.EmissionModule emission = ps.emission;
                emission.enabled = false;
                ParticleSystem.ShapeModule shape = ps.shape;
                shape.enabled = false;
                // Fading out as they fly
                ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
                fade.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
                fade.color = gradient;

                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.03f;
                renderer.lengthScale = 2f;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterial = new Material(shader) { name = "PaP 115 spark", mainTexture = Dot() };
                go.SetActive(true);
                _system = ps;
                return ps;
            }

            /// <summary>A soft round dot: bright middle, fading edge.</summary>
            private static Texture2D Dot()
            {
                const int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float d = new Vector2(x - size / 2f + 0.5f, y - size / 2f + 0.5f).magnitude / (size / 2f);
                        float a = Mathf.Clamp01(1f - d);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                }
                tex.Apply();
                return tex;
            }
        }
    }
}
