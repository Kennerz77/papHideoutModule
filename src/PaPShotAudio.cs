using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;
using UnityEngine;

namespace PaPHideoutModule
{
    /// <summary>
    /// BO3's Pack-a-Punch shot sound on top of the normal gunshot of 115 Infused weapons, picked by weapon class
    /// (BO3 has its own PaP fire sound per weapon family; the generic layer covers the rest), each shot closed by BO3's
    /// PaP tail (wpn_pap_last_custom), quieter, overlapping in full auto as in COD.
    /// One shot layer per gun: each shot restarts that gun's own audio source instead of stacking copies.
    /// </summary>
    internal static class PaPShotAudio
    {
        // Guards against the same shot being reported more than once
        private const float SameShotWindow = 0.03f;

        // A touch above the slider (~+2.3 dB): the layer sits a little forward of the gunshot without covering it
        private const float Presence = 1.3f;

        private const int LocalShooter = int.MinValue;

        // The tail under the shot layer: present, but never louder than the shot it closes
        private const float TailLevel = 1.0f, SlowTailLevel = 1.8f; // slower guns (pistols, shotguns...) let it ring

        private class Shooter
        {
            public AudioSource Shot, Tail;
            public float LastShot = -1f;
        }

        private static readonly Dictionary<int, Shooter> Shooters = new Dictionary<int, Shooter>();

        // Weapon template -> its own PaP shot and tail (registered by other mods)
        private static readonly Dictionary<string, (AudioClip shot, AudioClip tail)> Custom = new Dictionary<string, (AudioClip, AudioClip)>();

        private static readonly HashSet<string> NoTails = new HashSet<string>();

        public static void NoTail(string templateId)
        {
            if (!string.IsNullOrEmpty(templateId))
                NoTails.Add(templateId);
        }

        public static void Register(string templateId, AudioClip shot, AudioClip tail)
        {
            if (!string.IsNullOrEmpty(templateId))
                Custom[templateId] = (shot, tail);
        }

        public static void OnShot(bool yourPlayer, int shooterId, Vector3 position, Weapon weapon)
        {
            // A weapon's own PaP sound (other mods, through PaPApi), else the layer for its class
            Custom.TryGetValue(weapon.TemplateId.ToString(), out var own);
            AudioClip clip = own.shot ?? PaPAssets.Clip(ClipFor(weapon)) ?? PaPAssets.Clip(yourPlayer ? "pap_shot_first_plr" : "pap_shot_first_npc");
            if (clip == null)
                return;

            int key = yourPlayer ? LocalShooter : shooterId;
            if (!Shooters.TryGetValue(key, out Shooter shooter) || shooter.Shot == null)
            {
                float spatial = yourPlayer ? 0f : 1f;
                Shooters[key] = shooter = new Shooter
                {
                    Shot = NewSource(yourPlayer ? "PaP_ShotAudio" : $"PaP_ShotAudio_{shooterId}", spatial),
                    Tail = NewSource(yourPlayer ? "PaP_ShotTail" : $"PaP_ShotTail_{shooterId}", spatial),
                };
            }
            if (Time.time - shooter.LastShot < SameShotWindow)
                return;
            shooter.LastShot = Time.time;
            if (!yourPlayer)
                shooter.Shot.transform.position = shooter.Tail.transform.position = position;

            Play(shooter.Shot, clip, yourPlayer ? 1f : PaPHideoutModulePlugin.EnemyShotVolume.Value);
            // Every shot ends in the tail, quieter, and tails overlap like COD's in full auto (one-shots stack)
            AudioClip tail = own.tail ?? PaPAssets.Clip(yourPlayer ? "pap_shot_tail_plr" : "pap_shot_tail_npc");
            if (tail != null && !NoTails.Contains(weapon.TemplateId.ToString()))
                shooter.Tail.PlayOneShot(tail, Mathf.Clamp01(PaPHideoutModulePlugin.ShotSoundVolume.Value * Presence * (yourPlayer ? 1f : PaPHideoutModulePlugin.EnemyShotVolume.Value) * PaPHideoutModulePlugin.TailVolume.Value * (IsSlowGun(weapon) ? SlowTailLevel : TailLevel)));
        }

        private static bool IsSlowGun(Weapon weapon)
        {
            switch (weapon.WeapClass?.ToLowerInvariant())
            {
                case "assaultrifle":
                case "assaultcarbine":
                case "smg":
                case "machinegun":
                    return false;
                case "pistol":
                    return weapon.WeapFireType == null || !weapon.WeapFireType.Contains(Weapon.EFireMode.fullauto);
                default:
                    return true;
            }
        }

        private static void Play(AudioSource source, AudioClip clip, float factor)
        {
            source.volume = Mathf.Clamp01(PaPHideoutModulePlugin.ShotSoundVolume.Value * Presence * factor);
            source.clip = clip;
            source.Play(); // restarts: never more than one PaP shot sound per gun
        }

        /// <summary>Closest BO3 PaP weapon family for a Tarkov weapon class (works for modded guns: it's the class, not the gun).</summary>
        private static string ClipFor(Weapon weapon)
        {
            switch (weapon.WeapClass?.ToLowerInvariant())
            {
                case "assaultrifle":
                case "assaultcarbine":
                    return "pap_shot_ar_standard";
                case "marksmanrifle":
                    return "pap_shot_ar_accurate";
                case "sniperrifle":
                    return "pap_shot_ar_damage";
                case "smg":
                    return "pap_shot_smg";
                case "machinegun":
                    return "pap_shot_lmg";
                case "pistol":
                    return weapon.WeapFireType != null && weapon.WeapFireType.Contains(Weapon.EFireMode.fullauto)
                        ? "pap_shot_pistol_auto"
                        : "pap_shot_pistol";
                case "grenadelauncher":
                    return "pap_shot_launcher";
                default:
                    return "pap_shot_first_plr"; // shotguns, special weapons
            }
        }

        private static AudioSource NewSource(string name, float spatialBlend)
        {
            var go = new GameObject(name);
            Object.DontDestroyOnLoad(go);
            AudioSource source = go.AddComponent<AudioSource>();
            source.spatialBlend = spatialBlend;
            source.playOnAwake = false;
            source.priority = 64; // kept over ambient sounds when many play at once (128 is Unity's default)
            source.rolloffMode = AudioRolloffMode.Linear;
            source.maxDistance = 60f;
            return source;
        }
    }
}
