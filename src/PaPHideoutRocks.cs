using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EFT;
using EFT.Hideout;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PaPHideoutModule
{
    /// <summary>
    /// Three green 115 rocks hidden around the hideout (BO3's Origins artifact piece). They glow and hum like the
    /// Factory fragments, only quieter. Look at one and pick "Investigate": the pickup sound plays, the rock stays and
    /// falls silent. When all three are investigated, the hideout radio plays BO3's "115" until the song ends, then the
    /// hideout's own music comes back. Progress lasts the game session across hideout visits; a raid or a restart
    /// starts it over. The rocks sit at fixed spots (<see cref="Spots"/>).
    /// </summary>
    internal static class PaPHideoutRocks
    {
        public const int Count = 3;
        private const float InteractDistance = 2.5f;
        private const string RockName = "PaP_HideoutRock_";

        // Where they're hidden (placed in game): position, rotation (euler), scale
        private static readonly (Vector3 position, Vector3 rotation, Vector3 scale)[] Spots =
        {
            (new Vector3(-4.3347f, 2.9311f, -18.4059f), new Vector3(285.27f, 43.19f, 93.08f), new Vector3(3.1f, 5.7f, 2.5f)),
            (new Vector3(13.6136f, 0.0787f, 2.2254f), new Vector3(303.38f, 270.40f, 90.00f), Vector3.one),
            (new Vector3(12.9741f, 0.0993f, 6.3163f), new Vector3(289.14f, 128.54f, 322.14f), Vector3.one),
        };

        private static readonly GameObject[] Rocks = new GameObject[Count];

        // Decided once per hideout visit: the rocks only appear on a visit that starts with the Pack-a-Punch built
        private static bool _visitDecided;
        private static readonly PaPInteractionMenu[] Menus = Enumerable.Range(0, Count).Select(_ => new PaPInteractionMenu()).ToArray();

        // This session's progress: kept across hideout visits, cleared by a raid (or a restart)
        private static readonly bool[] Investigated = new bool[Count];
        private static bool _songPlayed;

        // ---------- Rocks ----------

        /// <summary>Puts the rocks in the hideout; investigated ones stay silent.</summary>
        private static void Spawn()
        {
            GameObject prefab = PaPAssets.GreenRock;
            if (prefab == null)
                return;
            for (int i = 0; i < Count; i++)
            {
                if (Rocks[i] != null)
                    continue;
                Rocks[i] = Create(prefab, i);
                Rocks[i].transform.SetPositionAndRotation(Spots[i].position, Quaternion.Euler(Spots[i].rotation));
                Rocks[i].transform.localScale = Spots[i].scale;
            }
        }

        /// <summary>
        /// Once per visit, as soon as the hideout's areas are known: the rocks come out only if the Pack-a-Punch was
        /// already built when the player walked in (building it mid-visit shows them next time).
        /// </summary>
        private static void DecideVisit()
        {
            if (_visitDecided)
                return;
            AreaData area = PaPArea.Data;
            if (area == null)
                return; // areas not set up yet (or a guest hideout): ask again next frame
            _visitDecided = true;
            if (area.CurrentLevel >= 1)
                Spawn();
        }

        /// <summary>The player walked into the hideout: decide again whether the rocks are out (the area may be built now).</summary>
        public static void NewVisit() => _visitDecided = false;

        public static void Despawn()
        {
            _visitDecided = false;
            StopSong();
            for (int i = 0; i < Count; i++)
            {
                Menus[i].Reset();
                if (Rocks[i] != null)
                    Object.Destroy(Rocks[i]);
                Rocks[i] = null;
            }
        }

        private static GameObject Create(GameObject prefab, int index)
        {
            GameObject rock = Object.Instantiate(prefab);
            rock.name = RockName + (index + 1);
            // Something for the crosshair to land on (the model has no collider of its own)
            Renderer renderer = rock.GetComponentInChildren<MeshRenderer>();
            if (renderer != null)
            {
                var box = rock.AddComponent<BoxCollider>();
                Bounds local = renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
                box.center = local.center;
                box.size = local.size * 1.15f;
            }
            PaPRockGlow glow = rock.AddComponent<PaPRockGlow>();
            glow.InHideout = true;
            glow.Silenced = Investigated[index];
            glow.SetLive(true, 1f);
            return rock;
        }

        /// <summary>Called every frame while in the hideout: the "Investigate" option and the song.</summary>
        public static void Tick(HideoutPlayerOwner owner)
        {
            UpdateSong();
            DecideVisit();
            if (owner == null)
                return;
            for (int i = 0; i < Count; i++)
            {
                if (Rocks[i] == null)
                    continue;
                int index = i;
                Menus[i].Tick(owner, Rocks[i].transform, InteractDistance, () => Options(index));
            }
        }

        private static IEnumerable<(string name, Action action)> Options(int index)
        {
            if (!Investigated[index])
                yield return ("Investigate", () => Investigate(index));
        }

        private static void Investigate(int index)
        {
            if (Investigated[index])
                return;
            Investigated[index] = true;
            GameObject rock = Rocks[index];
            if (rock != null)
            {
                AudioClip pickup = PaPAssets.RockPickupClip;
                if (pickup != null)
                    AudioSource.PlayClipAtPoint(pickup, rock.transform.position, PaPHideoutModulePlugin.RockPickupVolume.Value);
                PaPRockGlow glow = rock.GetComponent<PaPRockGlow>();
                if (glow != null)
                    glow.Silenced = true;
            }
            PaPHideoutModulePlugin.Log.LogInfo($"Hideout 115 rock {index + 1} investigated ({Investigated.Count(x => x)}/{Count}).");
            if (Investigated.All(x => x) && !_songPlayed)
                StartSong();
        }

        /// <summary>A raid happened: the rocks hum and wait to be investigated again next hideout visit.</summary>
        public static void ResetSession()
        {
            if (!_songPlayed && !Investigated.Any(x => x))
                return;
            Array.Clear(Investigated, 0, Count);
            _songPlayed = false;
            PaPHideoutModulePlugin.Log.LogInfo("Hideout 115 rocks reset for the next visit (a raid happened).");
        }

        // ---------- The 115 song ----------

        private static readonly List<AudioSource> Song = new List<AudioSource>();
        private static double _songEndsAt;
        private static readonly List<AudioSource> Muted = new List<AudioSource>();
        private static HideoutAudioBackground _background;
        private static float _backgroundVolume;

        /// <summary>
        /// Every music player in the hideout (the radio, the TV, the gym's speakers: the ambiance objects with a track
        /// list) goes quiet, and BO3's "115" plays from each of them at once, started on the same audio-clock tick so
        /// they're in step. Each copy takes its player's 3D settings and mixer channel, so it sounds like that speaker
        /// and the game's music volume still applies. When the song ends, the players carry on as they were.
        /// </summary>
        private static void StartSong()
        {
            AudioClip clip = PaPAssets.SongClip;
            if (clip == null)
            {
                PaPHideoutModulePlugin.Log.LogWarning("The 115 song isn't in pap_hideout.bundle.");
                return;
            }
            _songPlayed = true;

            // The speakers: music ambiance objects, plus anything named like a radio
            var speakers = new List<AudioSource>();
            var names = new List<string>();
            foreach (AudioAmbiance ambiance in Object.FindObjectsOfType<AudioAmbiance>(true))
            {
                if (!PlaysMusic(ambiance))
                    continue;
                AudioSource[] sources = ambiance.GetComponentsInChildren<AudioSource>(true);
                if (sources.Length == 0)
                    continue;
                speakers.Add(sources.FirstOrDefault(s => s.isPlaying) ?? sources[0]);
                names.Add(NameWithParent(ambiance.transform));
                Mute(sources);
            }
            // Named speakers count even when silent (the TV may be off); one copy of the song per speaker object
            foreach (var group in Object.FindObjectsOfType<AudioSource>(true).Where(s => !Muted.Contains(s) && IsRadio(s.transform))
                         .GroupBy(s => s.transform.parent != null ? s.transform.parent : s.transform))
            {
                AudioSource[] sources = group.ToArray();
                speakers.Add(sources.FirstOrDefault(s => s.isPlaying) ?? sources[0]);
                names.Add(NameWithParent(sources[0].transform));
                Mute(sources);
            }
            _background = Object.FindObjectOfType<HideoutAudioBackground>();
            if (_background != null)
            {
                _backgroundVolume = _background._masterVolume;
                _background._masterVolume = 0f; // its Update applies it to the hollow and inhabited loops
            }

            // One copy of the song per speaker, all starting together
            double start = AudioSettings.dspTime + 0.2;
            foreach (AudioSource speaker in speakers.DefaultIfEmpty())
                Song.Add(SongFrom(speaker, clip, start));
            _songEndsAt = start + clip.length;
            PaPHideoutModulePlugin.Log.LogInfo($"All three hideout 115 rocks investigated: \"115\" plays from {Song.Count} speaker(s): " +
                                               $"{(names.Count > 0 ? string.Join(", ", names) : "none found, so everywhere")}" +
                                               $"{(_background != null ? "; hideout background faded" : "")}.");
        }

        private static AudioSource SongFrom(AudioSource speaker, AudioClip clip, double start)
        {
            var go = new GameObject("PaP_115Song");
            AudioSource song = go.AddComponent<AudioSource>();
            song.clip = clip;
            song.loop = false;
            song.playOnAwake = false;
            song.volume = PaPHideoutModulePlugin.SongVolume.Value;
            if (speaker != null)
            {
                go.transform.position = speaker.transform.position;
                song.outputAudioMixerGroup = speaker.outputAudioMixerGroup;
                song.spatialBlend = speaker.spatialBlend;
                song.rolloffMode = speaker.rolloffMode;
                song.minDistance = speaker.minDistance;
                song.maxDistance = speaker.maxDistance;
                song.spread = speaker.spread;
                if (speaker.rolloffMode == AudioRolloffMode.Custom)
                    song.SetCustomCurve(AudioSourceCurveType.CustomRolloff, speaker.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
            }
            else
            {
                song.spatialBlend = 0f; // no speaker found: just play it
            }
            song.PlayScheduled(start);
            return song;
        }

        /// <summary>
        /// A hideout ambiance object that plays music (radio, TV, gym): a track list, or one long looping clip, rather
        /// than a short one-off sound.
        /// </summary>
        private static bool PlaysMusic(AudioAmbiance ambiance)
        {
            if (ambiance.Patterns == null)
                return false;
            foreach (Pattern<IAudioPlaybackSettings> pattern in ambiance.Patterns.Values)
            {
                switch (pattern?.Value)
                {
                    case AudioAmbiance.ContinuingPlaybackSettings tracks when tracks.TrackList != null && tracks.TrackList.Count > 0:
                        return true;
                    case AudioAmbiance.SimpleAudioSettings loop when loop._loop && loop._audioClip != null && loop._audioClip.length > 30f:
                        return true;
                }
            }
            return false;
        }


        private static void Mute(IEnumerable<AudioSource> sources)
        {
            foreach (AudioSource source in sources)
            {
                if (source != null && !source.mute && !Muted.Contains(source))
                {
                    source.mute = true;
                    Muted.Add(source);
                }
            }
        }

        private static void UpdateSong()
        {
            if (Song.Count == 0)
                return;
            foreach (AudioSource song in Song)
            {
                if (song != null)
                    song.volume = PaPHideoutModulePlugin.SongVolume.Value; // follows the F12 slider live
            }
            // It ended: the hideout's own music comes back
            if (AudioSettings.dspTime > _songEndsAt + 0.5)
                StopSong();
        }

        private static void StopSong()
        {
            foreach (AudioSource song in Song)
            {
                if (song != null)
                    Object.Destroy(song.gameObject);
            }
            Song.Clear();
            foreach (AudioSource source in Muted)
            {
                if (source != null)
                    source.mute = false;
            }
            Muted.Clear();
            if (_background != null)
                _background._masterVolume = _backgroundVolume;
            _background = null;
        }
        private static bool IsRadio(Transform t)
        {
            for (int depth = 0; t != null && depth < 4; t = t.parent, depth++)
            {
                // The living space's radio and the TV's PlayStation ("PlatStation4/PSAudio") play sound, not track lists
                string name = t.name.ToLowerInvariant();
                if (name.Contains("radio") || name.Contains("television") || name.StartsWith("tv") || name.Contains("_tv")
                    || name.Contains("platstation") || name.Contains("playstation") || name == "psaudio" || name.Contains("boombox"))
                    return true;
            }
            return false;
        }

        private static string NameWithParent(Transform t) => t.parent != null ? $"{t.parent.name}/{t.name}" : t.name;
    }
}
