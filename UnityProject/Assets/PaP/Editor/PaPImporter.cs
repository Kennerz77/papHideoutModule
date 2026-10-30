using System.Collections.Generic;
using System.IO;
using System.Linq;
using PaP.SEFormats;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds Assets/PaP/pap_machine.prefab straight from Greyhound's SEModel/SEAnim exports (no Blender/FBX):
/// skinned machine + "please wait" flag, Standard (Specular setup) materials, BO3 anim clips + controllers,
/// colliders, sign glow and 3D audio.
/// </summary>
public static class PaPImporter
{
    private const string ExportRoot = @"E:\Greyhound boz\exported_files\black_ops_3";
    private const string MachineDir = ExportRoot + @"\xmodels\p7_zm_vending_packapunch_on";
    private const string MachineFile = MachineDir + @"\p7_zm_vending_packapunch_on_LOD0.semodel";
    private const string FlagDir = ExportRoot + @"\xmodels\p7_zm_vending_packapunch_sign_wait";
    private const string FlagFile = FlagDir + @"\p7_zm_vending_packapunch_sign_wait_LOD0.semodel";
    private const string AnimDir = ExportRoot + @"\xanims";

    private const string Root = "Assets/PaP";
    private const string GeneratedDir = Root + "/Generated";
    private const string TextureDir = Root + "/Textures";
    private const string MaterialDir = Root + "/Materials";
    public const string PrefabPath = Root + "/pap_machine.prefab";
    public const string OriginsPrefabPath = Root + "/pap_machine_origins.prefab";
    private const string AudioDir = Root + "/Audio";

    private const string SignMaterial = "mtl_p7_zm_vending_packapunch_sign";

    // CoD units are inches
    private const float Scale = 0.0254f;
    private const float LoopBlendFrames = 10f;
    private const float BodyHeadSplit = 3.6f;

    // CoD is right-handed Z-up; Unity is left-handed Y-up. The machine's front (sign, rollers) is CoD -Y,
    // mapped to Unity +Z. The mapping mirrors one axis (det -1), so quaternion xyz = -(M * xyz).
    private static Vector3 ToUnity(SEVec3 v) => new Vector3(-v.X, v.Z, -v.Y) * Scale;
    private static Vector3 ToUnityDir(SEVec3 v) => new Vector3(-v.X, v.Z, -v.Y);
    private static Quaternion ToUnity(SEQuat q) => new Quaternion(q.X, -q.Z, q.Y, q.W);

    private enum ClipMode { RestPose, OneShot, Loop, HoldLastFrame }

    public const string CamoAlbedoPath = Root + "/Camo/pap_camo_albedo.png";
    private const string CamoColorPath = Root + "/Camo/pap_camo_c.png";
    private const string CamoSpecPath = Root + "/Camo/pap_camo_s.png";

    [MenuItem("PaP/1. Import BO3 Pack-a-Punch")]
    public static void ImportAll()
    {
        foreach (string dir in new[] { GeneratedDir, TextureDir, MaterialDir })
            Directory.CreateDirectory(dir);

        BuildCamoAlbedo();

        var root = new GameObject("pap_machine");
        try
        {
            // Machine
            SEModel machine = SEModel.Load(MachineFile);
            Debug.Log($"[PaP] Machine: {machine.Bones.Count} bones, {machine.Meshes.Count} meshes, {machine.Materials.Count} materials");
            Transform[] machineBones = BuildSkeleton(machine, root.transform);
            Mesh machineMesh = BuildModel(machine, MachineDir, machineBones, root.transform, "pap_mesh", out _);

            AnimatorController machineController = BuildController("pap_machine", machine, machineBones, root.transform, new[]
            {
                ("p_zm_pap_idle", (string)null, ClipMode.RestPose),
                ("p_zm_pap_poweron", "o_zombie_base_packapunch_poweron", ClipMode.OneShot),
                ("p_zm_pap_take_gun", "o_zombie_base_packapunch_working_take_gun", ClipMode.OneShot),
                ("p_zm_pap_working", "o_zombie_base_packapunch_working_loop", ClipMode.Loop),
                ("p_zm_pap_eject_gun", "o_zombie_base_packapunch_working_eject_gun", ClipMode.OneShot),
            });
            AddAnimator(root, machineController);

            // "Please wait" flag: the BO3 flag anims key its tag_origin at a fixed spot on the machine
            SEModel flag = SEModel.Load(FlagFile);
            SEAnim flagUp = SEAnim.Load(Path.Combine(AnimDir, "o_zombie_base_packapunch_flag_up.seanim"));
            SEAnimBone flagOrigin = flagUp.Bones.First(b => b.Name == "tag_origin");
            var flagRoot = new GameObject("pap_flag").transform;
            flagRoot.SetParent(machineBones.First(b => b.name == "tag_animate"), false);
            flagRoot.localPosition = ToUnity(flagOrigin.Positions[0].Value);
            flagRoot.localRotation = ToUnity(flagOrigin.Rotations[0].Value);
            Transform[] flagBones = BuildSkeleton(flag, flagRoot);
            BuildRigidModel(flag, FlagDir, flagBones, flagRoot, flagBones.First(b => b.name == "tag_animate"), "pap_flag_mesh");

            AnimatorController flagController = BuildController("pap_flag", flag, flagBones, flagRoot, new[]
            {
                ("flag_hidden", "o_zombie_base_packapunch_flag_down", ClipMode.HoldLastFrame),
                ("flag_up", "o_zombie_base_packapunch_flag_up", ClipMode.OneShot),
                ("flag_down", "o_zombie_base_packapunch_flag_down", ClipMode.OneShot),
            });
            AddAnimator(flagRoot.gameObject, flagController);

            AddColliders(root.transform, machineMesh);
            AddSignLight(root.transform, machine);
            PaPTarpBaker.Bake(root);

            AddAudio(root.transform, "Audio_Jingle", "pap_idle_jingle.wav", true, 0.35f);
            AddAudio(root.transform, "Audio_ProcessLoop", "pap_process_loop.wav", true, 0.6f);
            AddAudio(root.transform, "Audio_Sting", "pap_sting.wav", false, 0.7f);
            AddAudio(root.transform, "Audio_Crunch", "pap_crunch.wav", false, 0.8f);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PaP] Prefab saved: {PrefabPath} (bounds {machineMesh.bounds.size} m)");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ---------- Origins machine ----------

    // Converted from GrayWolf's cast export by C:\Git\99_Tools\cast_to_se.py (see Tools\CONTEXT.md): the semodel, baked
    // textures and the four castle_pap anims Origins' machine uses
    private static readonly string OriginsDir = Path.GetFullPath(Path.Combine(Application.dataPath, "PaP/Origins"));
    private const string OriginsModel = "p7_fxanim_zm_ori_packapunch_mod_LOD0.semodel";

    /// <summary>
    /// Builds Assets/PaP/pap_machine_origins.prefab: the Origins stone machine on the same state names, rollers and
    /// audio objects as the BO3 one, so the plugin drives both alike. Its sounds are Origins' own (tomb_pap_*).
    /// </summary>
    [MenuItem("PaP/1b. Import Origins Pack-a-Punch")]
    public static void ImportOrigins()
    {
        foreach (string dir in new[] { GeneratedDir, TextureDir, MaterialDir })
            Directory.CreateDirectory(dir);

        // The Origins camo's metal plating and glow are sampled on the CPU in game (the crystal-and-metal split)
        foreach (string readable in new[] { Root + "/Camo/origins_camo_c.png", Root + "/Camo/origins_camo_e.png" })
        {
            AssetDatabase.ImportAsset(readable);
            if (AssetImporter.GetAtPath(readable) is TextureImporter readableImporter && !readableImporter.isReadable)
            {
                readableImporter.isReadable = true;
                readableImporter.SaveAndReimport();
            }
        }

        // The crystal camo's facets are a normal map
        string facets = Root + "/Camo/origins_crystal_n.png";
        AssetDatabase.ImportAsset(facets);
        if (AssetImporter.GetAtPath(facets) is TextureImporter facetImporter && facetImporter.textureType != TextureImporterType.NormalMap)
        {
            facetImporter.textureType = TextureImporterType.NormalMap;
            facetImporter.SaveAndReimport();
        }

        var root = new GameObject("pap_machine_origins");
        try
        {
            SEModel machine = SEModel.Load(Path.Combine(OriginsDir, OriginsModel));
            Debug.Log($"[PaP] Origins machine: {machine.Bones.Count} bones, {machine.Meshes.Count} meshes, {machine.Materials.Count} materials");
            Transform[] bones = BuildSkeleton(machine, root.transform);
            Mesh mesh = BuildModel(machine, OriginsDir, bones, root.transform, "pap_origins_mesh", out _);

            string animDir = Path.Combine(OriginsDir, "anims");
            AnimatorController controller = BuildController("pap_machine_origins", machine, bones, root.transform, new[]
            {
                ("p_zm_pap_idle", (string)null, ClipMode.RestPose),
                ("p_zm_pap_poweron", "p7_fxanim_zm_castle_pap_poweron_anim", ClipMode.OneShot),
                ("p_zm_pap_take_gun", "p7_fxanim_zm_castle_pap_working_take_gun_anim", ClipMode.OneShot),
                ("p_zm_pap_working", "p7_fxanim_zm_castle_pap_working_loop_anim", ClipMode.Loop),
                ("p_zm_pap_eject_gun", "p7_fxanim_zm_castle_pap_working_eject_gun_anim", ClipMode.OneShot),
            }, animDir, "origins_");
            AddAnimator(root, controller);

            // The crystals glow blue when the machine is on (the plugin switches emission with the machine's power)
            foreach (string path in new[] { $"{MaterialDir}/mtl_p7_zm_ori_packapunch_crystal_off.mat" })
            {
                var crystal = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (crystal == null)
                    continue;
                // The same crystal as the gun camo: its colour map, facets and glow
                var crystalColour = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Camo/origins_crystal_c.png");
                var crystalGlow = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Camo/origins_camo_e.png");
                var crystalFacets = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Camo/origins_crystal_n.png");
                if (crystalColour != null)
                    crystal.SetTexture("_MainTex", crystalColour);
                if (crystalFacets != null)
                {
                    crystal.SetTexture("_BumpMap", crystalFacets);
                    crystal.EnableKeyword("_NORMALMAP");
                }
                crystal.SetTexture("_EmissionMap", crystalGlow != null ? crystalGlow : crystal.GetTexture("_MainTex"));
                crystal.SetColor("_EmissionColor", new Color(0.35f, 0.85f, 1f) * 1.4f); crystal.color = new Color(0.8f, 0.9f, 1f);
                crystal.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                crystal.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(crystal);
            }

            var colliders = new GameObject("Colliders").transform;
            colliders.SetParent(root.transform, false);
            AddBox(colliders, "Collider_Body", mesh.vertices);

            // Origins' own sounds only: no jingle, no hum. Which tomb_pap clip belongs to which moment is a guess
            AddAudio(root.transform, "Audio_Sting", "pap_origins_in.wav", false, 0.9f);
            AddAudio(root.transform, "Audio_ProcessLoop", "pap_origins_hum.wav", true, 0.8f);

            // Blue light in the cracks of the stone while it's on, and a stronger one in the middle while it works
            AddBlueLight(root.transform, "PaP_CrackGlow", new Vector3(0f, 3.2f, 0.7f), 0.9f, 2.6f);
            AddBlueLight(root.transform, "PaP_CrackGlow_Top", new Vector3(0f, 4.2f, 0.4f), 0.6f, 2.2f);
            AddBlueLight(root.transform, "PaP_WorkGlow", new Vector3(0f, 3.3f, 1.4f), 0f, 3.5f);

            PrefabUtility.SaveAsPrefabAsset(root, OriginsPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PaP] Prefab saved: {OriginsPrefabPath} (bounds {mesh.bounds.size} m, center {mesh.bounds.center})");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void AddBlueLight(Transform root, string name, Vector3 at, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = at;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.3f, 0.65f, 1f);
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
    }

    private static void AddAnimator(GameObject go, AnimatorController controller)
    {
        var animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    // ---------- Geometry ----------

    private static Transform[] BuildSkeleton(SEModel model, Transform root)
    {
        var bones = new Transform[model.Bones.Count];
        for (int i = 0; i < model.Bones.Count; i++)
        {
            SEBone b = model.Bones[i];
            var t = new GameObject(b.Name).transform;
            t.SetParent(b.Parent >= 0 ? bones[b.Parent] : root, false);
            t.localPosition = ToUnity(b.LocalPosition);
            t.localRotation = ToUnity(b.LocalRotation);
            bones[i] = t;
        }
        return bones;
    }

    private static Mesh BuildModel(SEModel model, string modelDir, Transform[] bones, Transform root, string name, out SkinnedMeshRenderer smr)
    {
        Material[] materials = model.Materials.Select(m => BuildMaterial(m, modelDir)).ToArray();
        Mesh mesh = BuildMesh(model, bones, root, name);
        AssetDatabase.CreateAsset(mesh, $"{GeneratedDir}/{name}.asset");

        var meshGo = new GameObject(name);
        meshGo.transform.SetParent(root, false);
        smr = meshGo.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.bones = bones;
        smr.rootBone = bones[0];
        smr.updateWhenOffscreen = true;
        smr.sharedMaterials = model.Meshes
            .Select(m => m.MaterialIndices.Length > 0 && m.MaterialIndices[0] >= 0 ? materials[m.MaterialIndices[0]] : materials[0])
            .ToArray();
        return mesh;
    }

    /// <summary>For models fully weighted to one bone (the flag): a plain mesh parented to that bone, no skinning.</summary>
    private static void BuildRigidModel(SEModel model, string modelDir, Transform[] bones, Transform root, Transform bone, string name)
    {
        Material[] materials = model.Materials.Select(m => BuildMaterial(m, modelDir)).ToArray();
        Mesh mesh = BuildMesh(model, bones, root, name);

        // Move vertices from model space into the bone's space
        Matrix4x4 toBone = bone.worldToLocalMatrix * root.localToWorldMatrix;
        mesh.vertices = mesh.vertices.Select(v => toBone.MultiplyPoint3x4(v)).ToArray();
        mesh.normals = mesh.normals.Select(n => toBone.MultiplyVector(n).normalized).ToArray();
        mesh.boneWeights = null;
        mesh.bindposes = new Matrix4x4[0];
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        AssetDatabase.CreateAsset(mesh, $"{GeneratedDir}/{name}.asset");

        var meshGo = new GameObject(name);
        meshGo.transform.SetParent(bone, false);
        meshGo.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshGo.AddComponent<MeshRenderer>().sharedMaterials = model.Meshes
            .Select(m => m.MaterialIndices.Length > 0 && m.MaterialIndices[0] >= 0 ? materials[m.MaterialIndices[0]] : materials[0])
            .ToArray();
    }

    private static Mesh BuildMesh(SEModel model, Transform[] bones, Transform root, string name)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var weights = new List<BoneWeight>();
        var submeshes = new List<int[]>();

        foreach (SEMesh m in model.Meshes)
        {
            int baseIndex = vertices.Count;
            for (int v = 0; v < m.VertexCount; v++)
            {
                vertices.Add(ToUnity(m.Positions[v]));
                normals.Add(m.Normals != null ? ToUnityDir(m.Normals[v]).normalized : Vector3.up);
                uvs.Add(m.UVs != null ? new Vector2(m.UVs[v * 2], 1f - m.UVs[v * 2 + 1]) : Vector2.zero);
                weights.Add(ToBoneWeight(m, v));
            }

            int[] tris = m.Faces.Select(i => i + baseIndex).ToArray();

            // Keep the winding whose face normals agree with the vertex normals
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
            submeshes.Add(tris);
        }

        var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.boneWeights = weights.ToArray();
        mesh.subMeshCount = submeshes.Count;
        for (int i = 0; i < submeshes.Count; i++)
            mesh.SetTriangles(submeshes[i], i);
        mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * root.localToWorldMatrix).ToArray();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static BoneWeight ToBoneWeight(SEMesh m, int v)
    {
        if (m.MaxInfluence == 0)
            return new BoneWeight { boneIndex0 = 0, weight0 = 1f };

        var top = Enumerable.Range(0, m.MaxInfluence)
            .Select(i => (bone: m.WeightBones[v * m.MaxInfluence + i], weight: m.WeightValues[v * m.MaxInfluence + i]))
            .Where(x => x.weight > 0f)
            .OrderByDescending(x => x.weight)
            .Take(4)
            .ToArray();
        float sum = top.Sum(x => x.weight);
        if (top.Length == 0 || sum <= 0f)
            return new BoneWeight { boneIndex0 = 0, weight0 = 1f };

        var bw = new BoneWeight();
        for (int i = 0; i < top.Length; i++)
        {
            float w = top[i].weight / sum;
            switch (i)
            {
                case 0: bw.boneIndex0 = top[i].bone; bw.weight0 = w; break;
                case 1: bw.boneIndex1 = top[i].bone; bw.weight1 = w; break;
                case 2: bw.boneIndex2 = top[i].bone; bw.weight2 = w; break;
                case 3: bw.boneIndex3 = top[i].bone; bw.weight3 = w; break;
            }
        }
        return bw;
    }

    /// <summary>Body box (legs to shoulders) and head box (sign), in prefab space, on a "Colliders" child.</summary>
    private static void AddColliders(Transform root, Mesh mesh)
    {
        var colliders = new GameObject("Colliders").transform;
        colliders.SetParent(root, false);

        Vector3[] verts = mesh.vertices;
        AddBox(colliders, "Collider_Body", verts.Where(v => v.y <= BodyHeadSplit));
        AddBox(colliders, "Collider_Head", verts.Where(v => v.y > BodyHeadSplit));
    }

    private static void AddBox(Transform parent, string name, IEnumerable<Vector3> points)
    {
        Vector3[] p = points.ToArray();
        if (p.Length == 0)
            return;
        var bounds = new Bounds(p[0], Vector3.zero);
        foreach (Vector3 v in p)
            bounds.Encapsulate(v);

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var box = go.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size;
    }

    private static void AddSignLight(Transform root, SEModel machine)
    {
        SEMaterial sign = machine.Materials.FirstOrDefault(m => m.Name == SignMaterial);
        SEMesh signMesh = sign == null ? null : machine.Meshes.FirstOrDefault(m => m.MaterialIndices.Length > 0 && machine.Materials[m.MaterialIndices[0]] == sign);
        if (signMesh == null)
            return;

        Vector3 center = signMesh.Positions.Select(ToUnity).Aggregate(Vector3.zero, (a, b) => a + b) / signMesh.VertexCount;
        var go = new GameObject("SignLight");
        go.transform.SetParent(root, false);
        go.transform.localPosition = center + new Vector3(0f, 0f, 0.6f);
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.8f, 0.42f); // warm yellow like the BO3 machine (the plugin sets it too)
        light.intensity = 0.8f;
        light.range = 4f; // plugin multiplies by the machine's scale
        light.shadows = LightShadows.None;
    }

    // ---------- Materials ----------

    private static Material BuildMaterial(SEMaterial se, string modelDir)
    {
        string path = $"{MaterialDir}/{se.Name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard (Specular setup)")) { name = se.Name };
            AssetDatabase.CreateAsset(mat, path);
        }

        string imageDir = Path.Combine(modelDir, "_images", se.Name);
        string diffuse = SourceImage(modelDir, se.DiffuseMap);
        string normal = SourceImage(modelDir, se.NormalMap);
        string spec = SourceImage(modelDir, se.SpecularMap);
        string gloss = FindBySuffix(imageDir, "_g.png");
        string occlusion = FindBySuffix(imageDir, "_o.png");

        Texture2D albedo = null;
        if (diffuse != null)
        {
            albedo = ImportTexture(diffuse, TextureImporterType.Default, true);
            mat.SetTexture("_MainTex", albedo);
            mat.color = Color.white;
        }
        else
        {
            // e.g. $black_color placeholders
            mat.SetTexture("_MainTex", null);
            mat.color = se.DiffuseMap != null && se.DiffuseMap.Contains("$black") ? new Color(0.02f, 0.02f, 0.02f) : Color.gray;
        }

        if (normal != null)
        {
            mat.SetTexture("_BumpMap", ImportTexture(normal, TextureImporterType.NormalMap, false));
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
        }
        else
        {
            mat.SetTexture("_BumpMap", null);
            mat.DisableKeyword("_NORMALMAP");
        }

        if (spec != null)
        {
            mat.SetTexture("_SpecGlossMap", BuildSpecGloss(spec, gloss));
            mat.SetFloat("_GlossMapScale", 1f);
            mat.EnableKeyword("_SPECGLOSSMAP");
        }
        else
        {
            mat.SetColor("_SpecColor", new Color(0.2f, 0.2f, 0.2f));
            mat.SetFloat("_Glossiness", 0.35f);
        }

        if (occlusion != null)
            mat.SetTexture("_OcclusionMap", ImportTexture(occlusion, TextureImporterType.Default, false));

        if (se.Name == SignMaterial && albedo != null)
        {
            mat.SetTexture("_EmissionMap", albedo);
            mat.SetColor("_EmissionColor", new Color(0.55f, 0.52f, 0.52f)); // dimmer than the BO3 sign: Tarkov's hideout is dark and it bloomed to white
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.EnableKeyword("_EMISSION");
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static string SourceImage(string modelDir, string relative)
    {
        if (string.IsNullOrEmpty(relative))
            return null;
        string rel = relative.Replace(@"\\", @"\");
        if (Path.GetFileName(rel).StartsWith("$"))
            return null;
        string full = Path.Combine(modelDir, rel);
        return File.Exists(full) ? full : null;
    }

    private static string FindBySuffix(string dir, string suffix) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, "*" + suffix).FirstOrDefault(f => !Path.GetFileName(f).StartsWith("$")) : null;

    private static Texture2D ImportTexture(string source, TextureImporterType type, bool srgb)
    {
        string assetPath = $"{TextureDir}/{Path.GetFileName(source)}";
        if (!File.Exists(assetPath) || File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(assetPath))
        {
            File.Copy(source, assetPath, true);
            AssetDatabase.ImportAsset(assetPath);
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        if (importer.textureType != type || importer.sRGBTexture != srgb)
        {
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    /// <summary>Standard (Specular setup) reads smoothness from the specular map's alpha, so pack _g into _s.a.</summary>
    private static Texture2D BuildSpecGloss(string specSource, string glossSource)
    {
        string assetPath = $"{TextureDir}/{Path.GetFileNameWithoutExtension(specSource)}_sg.png";
        if (!File.Exists(assetPath))
        {
            var spec = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            spec.LoadImage(File.ReadAllBytes(specSource));
            Texture2D gloss = null;
            if (glossSource != null)
            {
                gloss = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                gloss.LoadImage(File.ReadAllBytes(glossSource));
            }

            int w = spec.width, h = spec.height;
            Color[] pixels = spec.GetPixels();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    pixels[i].a = gloss != null ? gloss.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h).r : 0.35f;
                }
            }
            spec.SetPixels(pixels);
            File.WriteAllBytes(assetPath, spec.EncodeToPNG());
            Object.DestroyImmediate(spec);
            if (gloss != null)
                Object.DestroyImmediate(gloss);
            AssetDatabase.ImportAsset(assetPath);
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    // ---------- Animation ----------

    private static AnimatorController BuildController(string name, SEModel model, Transform[] bones, Transform root,
        (string state, string anim, ClipMode mode)[] states, string animDir = AnimDir, string clipPrefix = "")
    {
        // tag_origin carries delta root motion / placement in BO3 anims; the object itself must stay put
        Transform[] animated = bones.Where(b => b.name != "tag_origin").ToArray();

        string controllerPath = $"{Root}/{name}.controller";
        AssetDatabase.DeleteAsset(controllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        foreach (var (state, animName, mode) in states)
        {
            SEAnim anim = animName != null ? SEAnim.Load(Path.Combine(animDir, animName + ".seanim")) : null;
            AnimatorState s = sm.AddState(state);
            s.motion = BuildClip(state, anim, mode, model, animated, root, clipPrefix);
            if (sm.defaultState == null)
                sm.defaultState = s;
        }
        return controller;
    }

    /// <summary>
    /// Every animated bone gets full position + rotation curves so switching states always restores the pose.
    /// </summary>
    private static AnimationClip BuildClip(string name, SEAnim anim, ClipMode mode, SEModel model, Transform[] animated, Transform root,
        string clipPrefix)
    {
        float fps = anim != null && anim.FrameRate > 0f ? anim.FrameRate : 30f;
        float length = anim != null && mode != ClipMode.HoldLastFrame ? Mathf.Max(1, anim.FrameCount - 1) / fps : 1f;
        var clip = new AnimationClip { name = name, frameRate = fps, legacy = false };

        foreach (Transform bone in animated)
        {
            string path = AnimationUtility.CalculateTransformPath(bone, root);
            SEBone rest = model.Bones.First(b => b.Name == bone.name);
            SEAnimBone track = anim?.Bones.FirstOrDefault(b => b.Name == bone.name);

            // SEAnim "delta" keys are offsets from the rest pose
            bool delta = anim != null && anim.Type != SEAnimType.Absolute;
            Vector3 restPos = ToUnity(rest.LocalPosition);
            Quaternion restRot = ToUnity(rest.LocalRotation);

            var posKeys = track != null && track.Positions.Count > 0
                ? track.Positions.Select(k => (t: k.Frame / fps, v: delta ? restPos + ToUnity(k.Value) : ToUnity(k.Value))).ToList()
                : new List<(float t, Vector3 v)> { (0f, restPos) };
            var rotKeys = track != null && track.Rotations.Count > 0
                ? track.Rotations.Select(k => (t: k.Frame / fps, v: delta ? restRot * ToUnity(k.Value) : ToUnity(k.Value))).ToList()
                : new List<(float t, Quaternion v)> { (0f, restRot) };

            if (mode == ClipMode.HoldLastFrame)
            {
                posKeys = new List<(float t, Vector3 v)> { (0f, posKeys[posKeys.Count - 1].v) };
                rotKeys = new List<(float t, Quaternion v)> { (0f, rotKeys[rotKeys.Count - 1].v) };
            }

            // Hold the last key to the end of the clip
            if (posKeys[posKeys.Count - 1].t < length) posKeys.Add((length, posKeys[posKeys.Count - 1].v));
            if (rotKeys[rotKeys.Count - 1].t < length) rotKeys.Add((length, rotKeys[rotKeys.Count - 1].v));

            // BO3 loops sit between take/eject anims and don't end where they start; blend back so the loop is seamless
            if (mode == ClipMode.Loop)
            {
                float wrapEnd = length + LoopBlendFrames / fps;
                posKeys.Add((wrapEnd, posKeys[0].v));
                rotKeys.Add((wrapEnd, rotKeys[0].v));
            }

            SetCurve(clip, path, "m_LocalPosition.x", posKeys.Select(k => (k.t, k.v.x)));
            SetCurve(clip, path, "m_LocalPosition.y", posKeys.Select(k => (k.t, k.v.y)));
            SetCurve(clip, path, "m_LocalPosition.z", posKeys.Select(k => (k.t, k.v.z)));
            SetCurve(clip, path, "m_LocalRotation.x", rotKeys.Select(k => (k.t, k.v.x)));
            SetCurve(clip, path, "m_LocalRotation.y", rotKeys.Select(k => (k.t, k.v.y)));
            SetCurve(clip, path, "m_LocalRotation.z", rotKeys.Select(k => (k.t, k.v.z)));
            SetCurve(clip, path, "m_LocalRotation.w", rotKeys.Select(k => (k.t, k.v.w)));
        }

        clip.EnsureQuaternionContinuity();
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = mode == ClipMode.Loop || mode == ClipMode.RestPose || mode == ClipMode.HoldLastFrame;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        string clipPath = $"{GeneratedDir}/{clipPrefix}{name}.anim";
        AssetDatabase.DeleteAsset(clipPath);
        AssetDatabase.CreateAsset(clip, clipPath);
        Debug.Log($"[PaP] Clip {name}: {clip.length:F2}s @ {fps}fps ({mode})");
        return clip;
    }

    private static void SetCurve(AnimationClip clip, string path, string property, IEnumerable<(float t, float v)> keys)
    {
        var curve = new AnimationCurve(keys.Select(k => new Keyframe(k.t, k.v)).ToArray());
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        clip.SetCurve(path, typeof(Transform), property, curve);
    }

    // ---------- Weapon camo ----------

    /// <summary>
    /// The BO1 PaP colour map is near-black; its red/blue circuit pattern lives in the specular map.
    /// Tarkov's weapon shaders only take our albedo, so bake some of the pattern into it to keep the look.
    /// </summary>
    private static void BuildCamoAlbedo()
    {
        var color = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        color.LoadImage(File.ReadAllBytes(CamoColorPath));
        var spec = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        spec.LoadImage(File.ReadAllBytes(CamoSpecPath));

        int w = color.width, h = color.height;
        Color[] pixels = color.GetPixels();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                Color s = spec.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
                Color c = pixels[i] + s * SpecWeight;
                c = Pop(new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b)));
                c.a = 1f;
                pixels[i] = c;
            }
        }
        color.SetPixels(pixels);
        File.WriteAllBytes(CamoAlbedoPath, color.EncodeToPNG());
        Object.DestroyImmediate(color);
        Object.DestroyImmediate(spec);

        AssetDatabase.ImportAsset(CamoAlbedoPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(CamoAlbedoPath);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.sRGBTexture = true;
        importer.SaveAndReimport();
        Debug.Log($"[PaP] Camo albedo built: {CamoAlbedoPath}");
    }

    private const float CamoSaturation = 1.4f;
    private const float TraceLift = 1.4f, MetalLift = 0.75f;
    // How much of the spec map is added to the colour map: the BO1 camo is near-black metal with glinting traces
    private const float SpecWeight = 0.5f;

    /// <summary>
    /// Makes the camo read in Tarkov's lighting: the circuit traces get more saturated and brighter, the grey metal a
    /// little darker, so the contrast carries it. Brightness rolls off softly above 0.85 instead of clipping.
    /// </summary>
    private static Color Pop(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        v *= Mathf.Lerp(MetalLift, TraceLift, Mathf.Clamp01(s * 2f));
        if (v > 0.85f)
            v = 0.85f + 0.15f * (float)System.Math.Tanh((v - 0.85f) / 0.15f);
        return Color.HSVToRGB(h, Mathf.Clamp01(s * CamoSaturation), v);
    }

    // ---------- Audio ----------

    private static void AddAudio(Transform parent, string name, string file, bool loop, float volume)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/{file}");
        if (clip == null)
        {
            Debug.LogError($"[PaP] Missing audio clip {file} for {name}");
            return;
        }

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 2.5f, 0.5f);

        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = loop;
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1f;
        source.maxDistance = 15f;
        source.dopplerLevel = 0f;
        source.volume = volume;
    }
}
