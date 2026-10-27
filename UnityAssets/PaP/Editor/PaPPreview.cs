using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Renders the prefab from a few angles to logs/preview_*.png so the import can be checked without opening the editor.</summary>
public static class PaPPreview
{
    public static void RenderBatch()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PaPImporter.PrefabPath);
        GameObject go = Object.Instantiate(prefab);
        GameObject flag = go.GetComponentsInChildren<Transform>().First(t => t.name == "pap_flag").gameObject;

        var lightGo = new GameObject("light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.6f;
        lightGo.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
        RenderSettings.ambientLight = new Color(0.2f, 0.2f, 0.2f);

        var camGo = new GameObject("cam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
        cam.fieldOfView = 40f;
        var rt = new RenderTexture(768, 768, 24);
        cam.targetTexture = rt;

        Bounds bounds = go.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == "pap_mesh").bounds;
        float dist = bounds.extents.magnitude * 2.8f;
        Directory.CreateDirectory("../logs");

        Sample(flag, "flag_hidden", 0f);
        Shoot(cam, rt, bounds, dist, new Vector3(0.8f, 0.4f, 0.8f), "iso_flag_hidden");
        Shoot(cam, rt, bounds, dist, new Vector3(-0.8f, 0.4f, -0.8f), "back_flag_hidden");

        Sample(flag, "flag_up", 10f);
        Shoot(cam, rt, bounds, dist, new Vector3(0.8f, 0.4f, 0.8f), "iso_flag_up");
        Shoot(cam, rt, bounds, dist, new Vector3(-0.8f, 0.4f, -0.8f), "back_flag_up");

        var flagRenderer = flag.GetComponentInChildren<MeshRenderer>();
        foreach (var (clip, time) in new[] { ("flag_hidden", 0f), ("flag_up", 10f) })
        {
            Sample(flag, clip, time);
            Debug.Log($"[PaP] {clip}@{time}: flag bounds center={flagRenderer.bounds.center} size={flagRenderer.bounds.size}");
        }

        RenderSettings.ambientLight = new Color(0.9f, 0.9f, 0.9f);
        foreach (var (clip, time, tag) in new[] { ("flag_up", 10f, "up"), ("flag_up", 0.5f, "mid"), ("flag_hidden", 0f, "hidden") })
        {
            Sample(flag, clip, time);
            Shoot(cam, rt, bounds, dist, new Vector3(-1f, 0.3f, 0.35f), $"bright_left_{tag}");
            Shoot(cam, rt, bounds, dist, new Vector3(-0.7f, 0.4f, -1f), $"bright_backleft_{tag}");
        }

        // Camo on a test cube, to judge the look
        go.SetActive(false);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var camoMat = new Material(Shader.Find("Standard")) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(PaPImporter.CamoAlbedoPath) };
        camoMat.mainTextureScale = new Vector2(2f, 2f);
        cube.GetComponent<Renderer>().sharedMaterial = camoMat;
        cube.transform.localScale = new Vector3(1.6f, 0.4f, 0.25f);
        Shoot(cam, rt, new Bounds(cube.transform.position, Vector3.one), 2.2f, new Vector3(0.3f, 0.35f, 1f), "camo_test");
        go.SetActive(true);
        Object.DestroyImmediate(cube);

        foreach (Collider c in go.GetComponentsInChildren<Collider>())
            Debug.Log($"[PaP] collider {c.name} bounds center={c.bounds.center} size={c.bounds.size}");

        // The baked tarp over the machine (it ships switched off)
        Transform tarp = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "pap_tarp");
        if (tarp != null)
        {
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
            Sample(flag, "flag_hidden", 0f);
            tarp.gameObject.SetActive(true);
            Shoot(cam, rt, bounds, dist, new Vector3(0.8f, 0.4f, 0.8f), "tarp_front");
            Shoot(cam, rt, bounds, dist, new Vector3(-0.9f, 0.5f, -0.6f), "tarp_back");
        }
        go.SetActive(false);

        // The five 115 fragments in a row, under warm lamp-like light so stone and glow both show
        RenderSettings.ambientLight = new Color(0.22f, 0.2f, 0.18f);
        light.intensity = 0.8f;
        light.color = new Color(1f, 0.8f, 0.55f);
        var rocks = new GameObject("rocks");
        for (int i = 0; i < PaPRockBuilder.Count; i++)
        {
            var prefab2 = AssetDatabase.LoadAssetAtPath<GameObject>(PaPRockBuilder.PrefabPath(i));
            if (prefab2 == null)
                continue;
            GameObject rock = Object.Instantiate(prefab2, rocks.transform);
            rock.transform.localPosition = new Vector3((i - 2) * 0.38f, 0f, 0f);
        }
        Bounds rockBounds = new Bounds(new Vector3(0f, 0.1f, 0f), new Vector3(1.9f, 0.3f, 0.4f));
        Shoot(cam, rt, rockBounds, 2.6f, new Vector3(0f, 0.45f, 1f), "rocks");
        Shoot(cam, rt, new Bounds(new Vector3(-0.76f, 0.08f, 0f), Vector3.one * 0.3f), 0.55f, new Vector3(0.3f, 0.5f, 1f), "rock_close");
        Object.DestroyImmediate(rocks);
    }

    private static void Sample(GameObject target, string clipName, float time)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/PaP/Generated/{clipName}.anim");
        clip.SampleAnimation(target, Mathf.Min(time, clip.length));
    }

    private static void Shoot(Camera cam, RenderTexture rt, Bounds b, float dist, Vector3 dir, string name)
    {
        cam.transform.position = b.center + dir.normalized * dist;
        cam.transform.LookAt(b.center);
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes($"../logs/preview_{name}.png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
