using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity 2022.3.43f1 editor helpers for the PaP Hideout Module.
/// Menu: PaP > 1. Import BO3 Pack-a-Punch, then PaP > 2. Build pap_hideout.bundle.
/// Batch: Unity.exe -batchmode -quit -projectPath ... -executeMethod PaPBundleTools.RunAllBatch
/// </summary>
public static class PaPBundleTools
{
    private const string BundleName = "pap_hideout.bundle";
    private const string BuildDir = "AssetBundles";
    // Only this install. Do NOT point at D:\OTHER LAUNCHERS\BSTATE GAMES.
    private const string PluginDir = @"D:\OTHER LAUNCHERS\BUTTERSTATE\BepInEx\plugins\PaPHideoutModule";

    private const string SongPath = "Assets/PaP/Audio/pap_115_song.wav";

    /// <summary>The 3:48 song: compressed and streamed from the bundle, instead of 40 MB of PCM loaded at once.</summary>
    private static void StreamSong()
    {
        if (!(AssetImporter.GetAtPath(SongPath) is AudioImporter importer))
            return;
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        if (settings.loadType == AudioClipLoadType.Streaming && settings.compressionFormat == AudioCompressionFormat.Vorbis)
            return;
        settings.loadType = AudioClipLoadType.Streaming;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = 0.7f;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
    }

    [MenuItem("PaP/2. Build pap_hideout.bundle")]
    public static void BuildBundleMenu()
    {
        string dest = BuildBundle();
        if (dest != null)
            EditorUtility.RevealInFinder(dest);
    }

    public static string BuildBundle()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PaPImporter.PrefabPath) == null)
        {
            Debug.LogError($"[PaP] No prefab at {PaPImporter.PrefabPath}. Run step 1 first.");
            return null;
        }

        Directory.CreateDirectory(BuildDir);
        StreamSong();
        var builds = new[]
        {
            new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = new[]
                {
                    PaPImporter.PrefabPath,
                    PaPImporter.CamoAlbedoPath,
                    // The 115 rock fragments ("115 Riddles"): loaded by name, pap_rock_1..5
                    PaPRockBuilder.PrefabPath(0),
                    PaPRockBuilder.PrefabPath(1),
                    PaPRockBuilder.PrefabPath(2),
                    PaPRockBuilder.PrefabPath(3),
                    PaPRockBuilder.PrefabPath(4),
                    // Their inventory pictures: pap_rock_icon_1..5
                    PaPRockBuilder.IconPath(0),
                    PaPRockBuilder.IconPath(1),
                    PaPRockBuilder.IconPath(2),
                    PaPRockBuilder.IconPath(3),
                    PaPRockBuilder.IconPath(4),
                    "Assets/PaP/Audio/pap_shot_first_plr.wav",
                    "Assets/PaP/Audio/pap_shot_last_plr.wav",
                    "Assets/PaP/Audio/pap_shot_first_npc.wav",
                    "Assets/PaP/Audio/pap_shot_last_npc.wav",
                    "Assets/PaP/Audio/pap_shot_ar_standard.wav",
                    "Assets/PaP/Audio/pap_shot_ar_accurate.wav",
                    "Assets/PaP/Audio/pap_shot_ar_damage.wav",
                    "Assets/PaP/Audio/pap_shot_smg.wav",
                    "Assets/PaP/Audio/pap_shot_lmg.wav",
                    "Assets/PaP/Audio/pap_shot_pistol.wav",
                    "Assets/PaP/Audio/pap_shot_pistol_auto.wav",
                    "Assets/PaP/Audio/pap_shot_launcher.wav",
                    // BO3's wpn_pap_last_custom: the tail that closes the PaP shot
                    "Assets/PaP/Audio/pap_shot_tail_plr.wav",
                    "Assets/PaP/Audio/pap_shot_tail_npc.wav",
                    // Loaded by name: the machine's start-up and the 115 rocks' hum (loose WAVs next to the DLL
                    // loaded fine but played silent)
                    "Assets/PaP/Audio/pap_power_on.wav",
                    "Assets/PaP/Audio/pap_rock_hum.wav",
                    // The hideout's green 115 rocks: the rock, its pickup sound, and the song the radio plays
                    PaPRockBuilder.GreenPrefabPath,
                    "Assets/PaP/Audio/pap_rock_pickup.wav",
                    SongPath,
                }
            }
        };

        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
            BuildDir, builds, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        if (manifest == null)
        {
            Debug.LogError("[PaP] Bundle build failed; see console.");
            return null;
        }

        Directory.CreateDirectory(PluginDir);
        string dest = Path.Combine(PluginDir, BundleName);
        File.Copy(Path.Combine(BuildDir, BundleName), dest, true);
        Debug.Log($"[PaP] Built and copied bundle -> {dest}");
        return dest;
    }

    public static void RunAllBatch()
    {
        try
        {
            PaPImporter.ImportAll();
            PaPRockBuilder.BuildAll();
            if (BuildBundle() == null)
                EditorApplication.Exit(1);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
}
