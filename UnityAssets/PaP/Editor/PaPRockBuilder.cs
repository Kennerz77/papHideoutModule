using System.Collections.Generic;
using System.IO;
using System.Linq;
using PaP.SEFormats;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The five 115-infused rock fragments for "115 Riddles": Assets/PaP/Rocks/pap_rock_1..5.prefab. Small BO3 debris
/// rocks wearing a baked 115 texture set: Origins hex stone with pockets of the Zetsubou 115 crystal, which glow
/// through the Pack-a-Punch crystal emissive. Each prefab sits on its pivot (bottom centre) at scale 1; the plugin
/// pulses the glow and scales them per spawn.
/// </summary>
public static class PaPRockBuilder
{
    private const string Xmodels = @"E:\Greyhound boz\exported_files\black_ops_3\xmodels";
    private const string StoneSource = Xmodels + @"\p7_zm_ori_rock_hex_single_sml_01\_images\mtl_p7_zm_ori_rock_hex_single_sml_01\i_mtl_p7_zm_ori_rock_hex_rock_pattern_c.png";
    private const string CrystalDir = Xmodels + @"\p7_zm_ori_rock_hex_glow_lrg_01\_images\mtl_p7_zm_ori_crystal_ice_emissive";
    private const string CrystalSource = CrystalDir + @"\i_mtl_p7_zm_isl_crystal_115_01_c.png";
    private const string GlowSource = CrystalDir + @"\i_mtl_p7_zm_ctl_packapunch_crystal_e.png";

    public const string Dir = "Assets/PaP/Rocks";
    public const int Count = 5;
    public static string PrefabPath(int index) => $"{Dir}/pap_rock_{index + 1}.prefab";

    private const float Scale = 0.0254f; // CoD units are inches
    private const int Size = 1024;

    /// <summary>Which debris rock each fragment uses, and how it is sized and turned, so no two look alike.</summary>
    private static readonly (string model, float size, Vector3 euler, Vector3 stretch, Vector2 offset)[] Variants =
    {
        ("p7_fxp_debris_rock_01", 1.35f, new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f), new Vector2(0.00f, 0.00f)),
        ("p7_fxp_debris_rock_02", 1.10f, new Vector3(0f, 40f, 0f), new Vector3(1f, 1.15f, 1f), new Vector2(0.37f, 0.12f)),
        ("p7_fxp_debris_rock_03", 0.80f, new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f), new Vector2(0.61f, 0.48f)),
        ("p7_fxp_debris_rock_01", 1.10f, new Vector3(75f, 20f, 0f), new Vector3(1f, 1f, 1.2f), new Vector2(0.18f, 0.71f)),
        ("p7_fxp_debris_rock_03", 0.65f, new Vector3(0f, 110f, 160f), new Vector3(1.2f, 0.9f, 1f), new Vector2(0.83f, 0.29f)),
    };

    public static void BuildAll()
    {
        Directory.CreateDirectory(Dir);
        (Texture2D albedo, Texture2D emission) = BakeTextures();
        for (int i = 0; i < Count; i++)
            BuildRock(i, albedo, emission);
        BuildGreenRock();
        AssetDatabase.SaveAssets();
        RenderIcons();
    }

    // ---------- Inventory icons ----------

    public static string IconPath(int index) => $"{Dir}/pap_rock_icon_{index + 1}.png";
    private const int IconSize = 256;

    /// <summary>
    /// A picture of each fragment for its inventory icon: three-quarter view, soft key light, transparent background.
    /// The game's icon studio lights the rocks flat white whatever their material says, so the plugin hands these
    /// over instead, at the size the studio asked for.
    /// </summary>
    private static void RenderIcons()
    {
        var lightGo = new GameObject("icon_light");
        var key = lightGo.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 0.9f;
        key.color = new Color(1f, 0.97f, 0.92f);
        lightGo.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
        Color ambient = RenderSettings.ambientLight;
        RenderSettings.ambientLight = new Color(0.32f, 0.33f, 0.36f);

        var camGo = new GameObject("icon_camera");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.fieldOfView = 20f;
        cam.allowHDR = false;
        var rt = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 8 };
        cam.targetTexture = rt;
        try
        {
            for (int i = 0; i < Count; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i));
                GameObject rock = Object.Instantiate(prefab);
                foreach (Light own in rock.GetComponentsInChildren<Light>())
                    own.enabled = false; // its glow light would wash the stone out
                Bounds b = rock.GetComponentInChildren<Renderer>().bounds;
                Vector3 dir = new Vector3(0.6f, 0.55f, -1f).normalized;
                float dist = b.extents.magnitude / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                camGo.transform.position = b.center + dir * dist;
                camGo.transform.LookAt(b.center);
                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(IconPath(i), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(rock);

                AssetDatabase.ImportAsset(IconPath(i));
                var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath(i));
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                // The plugin resizes these on the CPU (a GPU blit picked up the game's render state and came out white)
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                Debug.Log($"[PaP] Rock {i + 1} icon rendered: {IconPath(i)}");
            }
        }
        finally
        {
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            RenderSettings.ambientLight = ambient;
        }
    }

    // ---------- The green 115 rock in the hideout (Origins artifact piece) ----------

    private const string GreenModel = "p7_zm_ori_artifact_piece_sml_green";
    public const string GreenPrefabPath = Dir + "/pap_green_rock.prefab";
    private const float GreenRockLength = 0.3f; // metres, longest side (the placement scale can change it per rock)

    /// <summary>
    /// The hideout's green 115 rock: BO3's Origins artifact piece as it is, its carved stone and its green gem, each
    /// with its own textures. The gem glows through the Pack-a-Punch crystal emissive, tinted green, with a soft green
    /// light inside. The decal layer is left out: it sits on the stone's surface and would flicker without BO3's decal
    /// shader. Pivot at the bottom centre, scale 1.
    /// </summary>
    private static void BuildGreenRock()
    {
        string modelDir = Path.Combine(Xmodels, GreenModel);
        SEModel model = SEModel.Load(Path.Combine(modelDir, GreenModel + "_LOD0.semodel"));
        string Image(string material, string file) => Path.Combine(modelDir, "_images", material, file);

        var groups = new[]
        {
            ("mtl_p7_zm_ori_artifact_piece_01", (Material)null),
            ("mtl_p7_zm_ori_artifact_piece_gem_green", (Material)null),
        };
        Material stone = new Material(Shader.Find("Standard (Specular setup)")) { name = "pap_green_rock_stone" };
        stone.SetTexture("_MainTex", ImportColour(Image(groups[0].Item1, "i_t7_zm_isl_rock_carving_fingerprint_01_flat_c.png")));
        SetNormal(stone, Image(groups[0].Item1, "i_mtl_p7_zm_ori_artifact_piece_01_n.png"));
        stone.SetColor("_SpecColor", new Color(0.1f, 0.1f, 0.1f));
        stone.SetFloat("_Glossiness", 0.25f);
        AssetDatabase.CreateAsset(stone, $"{Dir}/pap_green_rock_stone.mat");

        Material gem = new Material(Shader.Find("Standard (Specular setup)")) { name = "pap_green_rock_gem" };
        gem.SetTexture("_MainTex", ImportColour(Image(groups[1].Item1, "i_mtl_p7_zm_ori_artifact_piece_gem_green_c.png")));
        SetNormal(gem, Image(groups[1].Item1, "i_mtl_p7_zm_isl_crystal_115_01_n.png"));
        gem.SetColor("_SpecColor", new Color(0.35f, 0.45f, 0.35f));
        gem.SetFloat("_Glossiness", 0.75f);
        gem.SetTexture("_EmissionMap", ImportColour(Image(groups[1].Item1, "i_mtl_p7_zm_ctl_packapunch_crystal_e.png")));
        gem.SetColor("_EmissionColor", new Color(0.35f, 1f, 0.4f) * 2.4f);
        gem.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        gem.EnableKeyword("_EMISSION");
        AssetDatabase.CreateAsset(gem, $"{Dir}/pap_green_rock_gem.mat");

        Mesh mesh = BuildSubMeshes(model, new[] { groups[0].Item1, groups[1].Item1 }, "pap_green_rock");
        AssetDatabase.CreateAsset(mesh, $"{Dir}/pap_green_rock_mesh.asset");

        var root = new GameObject("pap_green_rock");
        try
        {
            var visual = new GameObject("model");
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            visual.AddComponent<MeshRenderer>().sharedMaterials = new[] { stone, gem };

            var glow = new GameObject("glow");
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = mesh.bounds.center + Vector3.up * 0.05f;
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.4f, 1f, 0.45f);
            light.range = 0.8f;
            light.intensity = 0.5f;
            light.shadows = LightShadows.None;

            PrefabUtility.SaveAsPrefabAsset(root, GreenPrefabPath);
            Debug.Log($"[PaP] Green rock: {GreenModel}, {mesh.vertexCount} verts, size {mesh.bounds.size} m");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>One mesh with a sub-mesh per listed material (others dropped), pivot at the bottom centre.</summary>
    private static Mesh BuildSubMeshes(SEModel model, string[] materials, string name)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var subMeshes = materials.Select(_ => new List<int>()).ToArray();
        foreach (SEMesh m in model.Meshes)
        {
            string material = m.MaterialIndices.Length > 0 ? model.Materials[m.MaterialIndices[0]].Name : null;
            int slot = System.Array.IndexOf(materials, material);
            if (slot < 0)
                continue;
            int baseIndex = vertices.Count;
            for (int v = 0; v < m.VertexCount; v++)
            {
                SEVec3 p = m.Positions[v];
                vertices.Add(new Vector3(-p.X, p.Z, -p.Y) * Scale);
                normals.Add(m.Normals != null ? new Vector3(-m.Normals[v].X, m.Normals[v].Z, -m.Normals[v].Y) : Vector3.up);
                uvs.Add(m.UVs != null ? new Vector2(m.UVs[v * 2], 1f - m.UVs[v * 2 + 1]) : Vector2.zero);
            }
            int[] tris = m.Faces.Select(i => i + baseIndex).ToArray();
            int agree = 0;
            for (int f = 0; f < tris.Length; f += 3)
            {
                Vector3 a = vertices[tris[f]], b = vertices[tris[f + 1]], c = vertices[tris[f + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normals[tris[f]]) > 0f)
                    agree++;
            }
            if (agree * 2 < tris.Length / 3)
            {
                for (int f = 0; f < tris.Length; f += 3)
                    (tris[f + 1], tris[f + 2]) = (tris[f + 2], tris[f + 1]);
            }
            subMeshes[slot].AddRange(tris);
        }

        // This model isn't at the debris rocks' scale (it came out 2.7 m long): size it to a hand-sized piece
        Bounds bounds = GeometryUtility.CalculateBounds(vertices.ToArray(), Matrix4x4.identity);
        Vector3 pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        float fit = GreenRockLength / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        for (int i = 0; i < vertices.Count; i++)
            vertices[i] = (vertices[i] - pivot) * fit;

        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = subMeshes.Length;
        for (int s = 0; s < subMeshes.Length; s++)
            mesh.SetTriangles(subMeshes[s], s);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static Texture2D ImportColour(string source)
    {
        string path = $"{Dir}/{Path.GetFileName(source)}";
        if (!File.Exists(path))
            File.Copy(source, path);
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void SetNormal(Material material, string source)
    {
        material.SetTexture("_BumpMap", ImportNormal(source));
        material.SetFloat("_BumpScale", 1f);
        material.EnableKeyword("_NORMALMAP");
    }

    private static void BuildRock(int index, Texture2D albedo, Texture2D emission)
    {
        var (modelName, size, euler, stretch, offset) = Variants[index];
        string modelDir = Path.Combine(Xmodels, modelName);
        SEModel model = SEModel.Load(Path.Combine(modelDir, modelName + "_LOD0.semodel"));
        Matrix4x4 shape = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(euler), stretch * size);
        Mesh mesh = BuildMesh(model, shape, $"pap_rock_{index + 1}");
        AssetDatabase.CreateAsset(mesh, $"{Dir}/pap_rock_{index + 1}_mesh.asset");

        string normalSource = Directory.GetFiles(Path.Combine(modelDir, "_images"), "*_n.png", SearchOption.AllDirectories).FirstOrDefault();
        Material material = BuildMaterial(index, albedo, emission, normalSource, offset);

        var root = new GameObject($"pap_rock_{index + 1}");
        try
        {
            var visual = new GameObject("model");
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            // A soft blue light from inside, so a fragment can be spotted in Factory's dark corners
            var glow = new GameObject("glow");
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = mesh.bounds.center + Vector3.up * 0.05f;
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.35f, 0.75f, 1f);
            light.range = 0.8f;
            light.intensity = 0.5f; // a hint on what's around it; the pockets themselves carry the glow
            light.shadows = LightShadows.None;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(index));
            Debug.Log($"[PaP] Rock {index + 1}: {modelName}, {mesh.vertexCount} verts, size {mesh.bounds.size} m");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>One rigid mesh, shaped by <paramref name="shape"/>, with its pivot at the bottom centre.</summary>
    private static Mesh BuildMesh(SEModel model, Matrix4x4 shape, string name)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        foreach (SEMesh m in model.Meshes)
        {
            int baseIndex = vertices.Count;
            for (int v = 0; v < m.VertexCount; v++)
            {
                // CoD is right-handed Z-up, Unity left-handed Y-up (same mapping as the machine)
                SEVec3 p = m.Positions[v];
                vertices.Add(shape.MultiplyPoint3x4(new Vector3(-p.X, p.Z, -p.Y) * Scale));
                Vector3 n = m.Normals != null ? new Vector3(-m.Normals[v].X, m.Normals[v].Z, -m.Normals[v].Y) : Vector3.up;
                normals.Add(shape.inverse.transpose.MultiplyVector(n).normalized);
                uvs.Add(m.UVs != null ? new Vector2(m.UVs[v * 2], 1f - m.UVs[v * 2 + 1]) : Vector2.zero);
            }

            int[] tris = m.Faces.Select(i => i + baseIndex).ToArray();
            // Keep the winding whose face normals agree with the vertex normals (the axis swap mirrors)
            int agree = 0;
            for (int f = 0; f < tris.Length; f += 3)
            {
                Vector3 a = vertices[tris[f]], b = vertices[tris[f + 1]], c = vertices[tris[f + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normals[tris[f]]) > 0f)
                    agree++;
            }
            if (agree * 2 < tris.Length / 3)
            {
                for (int f = 0; f < tris.Length; f += 3)
                    (tris[f + 1], tris[f + 2]) = (tris[f + 2], tris[f + 1]);
            }
            triangles.AddRange(tris);
        }

        // Pivot on the bottom centre, so a rock placed on a surface sits on it
        Bounds bounds = GeometryUtility.CalculateBounds(vertices.ToArray(), Matrix4x4.identity);
        Vector3 pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        for (int i = 0; i < vertices.Count; i++)
            vertices[i] -= pivot;

        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static Material BuildMaterial(int index, Texture2D albedo, Texture2D emission, string normalSource, Vector2 offset)
    {
        var material = new Material(Shader.Find("Standard (Specular setup)")) { name = $"pap_rock_{index + 1}" };
        material.SetTexture("_MainTex", albedo);
        // Each rock shows a different part of the texture, so the glowing pockets fall in different places
        material.mainTextureOffset = offset;
        if (normalSource != null)
        {
            material.SetTexture("_BumpMap", ImportNormal(normalSource));
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
        }
        material.SetColor("_SpecColor", new Color(0.12f, 0.13f, 0.15f));
        material.SetFloat("_Glossiness", 0.3f);
        material.SetTexture("_EmissionMap", emission);
        material.SetColor("_EmissionColor", new Color(1f, 1f, 1f) * 2.4f);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        material.EnableKeyword("_EMISSION");
        AssetDatabase.CreateAsset(material, $"{Dir}/pap_rock_{index + 1}.mat");
        return material;
    }

    private static Texture2D ImportNormal(string source)
    {
        string path = $"{Dir}/{Path.GetFileName(source)}";
        if (!File.Exists(path))
            File.Copy(source, path);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------- The 115 texture set ----------

    /// <summary>
    /// Albedo and emission, tiling: grey stone with blotchy pockets of 115 crystal. The pockets come from a seamless
    /// noise mask; inside them the crystal's colour replaces the stone and the Pack-a-Punch crystal glow shows.
    /// </summary>
    private static (Texture2D albedo, Texture2D emission) BakeTextures()
    {
        Texture2D stone = LoadSource(StoneSource), crystal = LoadSource(CrystalSource), glow = LoadSource(GlowSource);
        var albedo = new Color[Size * Size];
        var emission = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float u = x / (float)Size, v = y / (float)Size;
            // Each rock only uses about a third of the texture each way, so the pockets have to be small and many
            float noise = SeamlessNoise(u, v);
            // Crisp-edged patches set into the stone, like the Origins samples on Mechanic's bench
            float pocket = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.575f, noise));

            // The Origins stone as it is: warm brown-grey, a touch brighter so the shape reads in the dark
            Color rock = stone.GetPixelBilinear(u, v) * 1.1f;
            float halo = Mathf.InverseLerp(0.505f, 0.55f, noise);

            // Inside a patch: the Pack-a-Punch crystal's bright speckle over the 115 crystal's deep blue
            Color c = crystal.GetPixelBilinear(u * 3f, v * 3f);
            Color e = glow.GetPixelBilinear(u * 2f, v * 2f);
            Color speckle = Color.Lerp(c * 0.6f, new Color(0.55f, 0.85f, 1f), Mathf.Clamp01(e.grayscale * 1.8f));
            Color surface = Color.Lerp(rock, speckle, pocket);
            surface.a = 1f;
            albedo[y * Size + x] = surface;
            // The patches glow, brightest in the speckle; a faint blue seep just around them
            Color light = (e * 1.3f + c * 0.3f) * pocket + new Color(0.1f, 0.35f, 0.6f) * (halo * (1f - pocket) * 0.15f);
            light.a = 1f;
            emission[y * Size + x] = light;
        }

        Object.DestroyImmediate(stone);
        Object.DestroyImmediate(crystal);
        Object.DestroyImmediate(glow);
        return (SaveTexture("pap_rock_115_c", albedo, true), SaveTexture("pap_rock_115_e", emission, true));
    }

    /// <summary>Two octaves of Perlin noise, blended so the texture repeats without seams.</summary>
    private static float SeamlessNoise(float u, float v)
    {
        float Octaves(float a, float b) =>
            Mathf.PerlinNoise(a * 8f + 11.5f, b * 8f + 3.7f) * 0.7f + Mathf.PerlinNoise(a * 17f + 40.1f, b * 17f + 17.3f) * 0.3f;

        // Mix the four wrapped copies, weighted by distance from each edge
        return Octaves(u, v) * (1 - u) * (1 - v)
               + Octaves(u - 1, v) * u * (1 - v)
               + Octaves(u, v - 1) * (1 - u) * v
               + Octaves(u - 1, v - 1) * u * v;
    }

    private static Texture2D LoadSource(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { wrapMode = TextureWrapMode.Repeat };
        texture.LoadImage(File.ReadAllBytes(path));
        texture.wrapMode = TextureWrapMode.Repeat;
        return texture;
    }

    private static Texture2D SaveTexture(string name, Color[] pixels, bool srgb)
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();
        string path = $"{Dir}/{name}.png";
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture = srgb;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
