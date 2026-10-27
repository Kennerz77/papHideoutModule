using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the old tarp that covers the machine before its hideout area is built: a cloth simulation (position based
/// dynamics) dropped over the machine's real mesh, saved as a mesh with a generated canvas material, and added to the
/// prefab as "pap_tarp" (inactive; the plugin switches it on).
/// </summary>
public static class PaPTarpBaker
{
    private const string Dir = "Assets/PaP/Tarp";
    private const int ProbeLayer = 31;
    private const int N = 120;              // particles per side (the sheet is large: keeps the folds fine)
    private const float Thickness = 0.09f;  // metres at true BO3 scale (the prefab is scaled down in game); thick enough that the sign's rim stays under it
    private const int Steps = 1200;
    private const int Iterations = 10;
    private const float Dt = 1f / 90f;

    public static void Bake(GameObject root)
    {
        Directory.CreateDirectory(Dir);
        var probes = new List<GameObject>();
        var baked = new List<Mesh>();
        try
        {
            // Collision copies of the machine's own meshes (not the flag, which is down while covered)
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.GetComponentInParent<Animator>()?.gameObject.name == "pap_flag")
                    continue;
                var mesh = new Mesh();
                smr.BakeMesh(mesh);
                baked.Add(mesh);
                var probe = new GameObject("tarp_probe") { layer = ProbeLayer };
                probe.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                probe.transform.localScale = smr.transform.lossyScale;
                probe.AddComponent<MeshCollider>().sharedMesh = mesh;
                probes.Add(probe);
            }
            // The machine's body and sign boxes, a little larger: thin parts (the sign's rim) slip between cloth
            // particles, so the sheet also rests on these and nothing pokes through
            foreach (BoxCollider box in root.GetComponentsInChildren<BoxCollider>())
            {
                var probe = new GameObject("tarp_probe_box") { layer = ProbeLayer };
                probe.transform.SetPositionAndRotation(box.transform.position, box.transform.rotation);
                probe.transform.localScale = box.transform.lossyScale;
                var hull = probe.AddComponent<BoxCollider>();
                hull.center = box.center;
                hull.size = box.size * 1.04f + Vector3.one * 0.05f;
                probes.Add(probe);
            }
            Physics.SyncTransforms();

            Bounds bounds = new Bounds(root.transform.position, Vector3.zero);
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(r.bounds);

            Vector3[] positions = Simulate(bounds, out float spacing);
            Mesh tarp = BuildMesh(positions, spacing, root.transform);
            AssetDatabase.CreateAsset(tarp, Dir + "/pap_tarp_mesh.asset");

            var go = new GameObject("pap_tarp");
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = tarp;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BuildMaterial();
            go.SetActive(false);
            Debug.Log($"[PaP] Tarp baked: {positions.Length} particles, {spacing:F3} m spacing");
        }
        finally
        {
            foreach (GameObject probe in probes)
                Object.DestroyImmediate(probe);
            foreach (Mesh mesh in baked)
                Object.DestroyImmediate(mesh);
        }
    }

    private static Vector3[] Simulate(Bounds bounds, out float spacing)
    {
        // A square sheet big enough to reach the floor on every side and spread out over it
        float size = Mathf.Max(bounds.size.x, bounds.size.z) + bounds.size.y * 2.6f;
        spacing = size / (N - 1);
        float floor = bounds.min.y;
        var random = new System.Random(115);

        var pos = new Vector3[N * N];
        var prev = new Vector3[N * N];
        for (int i = 0; i < N; i++)
        for (int j = 0; j < N; j++)
        {
            int k = i * N + j;
            // Dropped flat from just above the machine, with a little unevenness so the folds aren't symmetric
            pos[k] = new Vector3(bounds.center.x - size / 2 + i * spacing,
                bounds.max.y + 0.5f + (float)random.NextDouble() * 0.05f,
                bounds.center.z - size / 2 + j * spacing);
            prev[k] = pos[k];
        }

        var constraints = new List<(int a, int b, float rest, float stiffness)>();
        void Link(int i0, int j0, int i1, int j1, float stiffness)
        {
            if (i1 < 0 || j1 < 0 || i1 >= N || j1 >= N)
                return;
            int a = i0 * N + j0, b = i1 * N + j1;
            constraints.Add((a, b, Vector3.Distance(pos[a], pos[b]), stiffness));
        }
        for (int i = 0; i < N; i++)
        for (int j = 0; j < N; j++)
        {
            Link(i, j, i + 1, j, 1f);       // stretch
            Link(i, j, i, j + 1, 1f);
            Link(i, j, i + 1, j + 1, 0.8f); // shear
            Link(i, j, i + 1, j - 1, 0.8f);
            Link(i, j, i + 2, j, 0.15f);    // a little bending stiffness: canvas, not silk
            Link(i, j, i, j + 2, 0.15f);
        }

        Vector3 gravity = Physics.gravity * Dt * Dt;
        int mask = 1 << ProbeLayer;
        for (int step = 0; step < Steps; step++)
        {
            for (int k = 0; k < pos.Length; k++)
            {
                Vector3 velocity = (pos[k] - prev[k]) * 0.985f;
                prev[k] = pos[k];
                pos[k] += velocity + gravity;
            }

            for (int it = 0; it < Iterations; it++)
            {
                foreach (var (a, b, rest, stiffness) in constraints)
                {
                    Vector3 delta = pos[b] - pos[a];
                    float length = delta.magnitude;
                    if (length < 1e-6f)
                        continue;
                    Vector3 fix = delta * ((length - rest) / length * 0.5f * stiffness);
                    pos[a] += fix;
                    pos[b] -= fix;
                }
            }

            for (int k = 0; k < pos.Length; k++)
                Collide(ref pos[k], ref prev[k], floor, mask);
        }
        return pos;
    }

    private static void Collide(ref Vector3 p, ref Vector3 previous, float floor, int mask)
    {
        Vector3 move = p - previous;
        float distance = move.magnitude;
        if (distance > 1e-5f && Physics.Raycast(previous, move / distance, out RaycastHit hit, distance + Thickness, mask))
        {
            // Stop on the surface, keep a bit of the sliding motion (friction)
            Vector3 slide = Vector3.ProjectOnPlane(move, hit.normal) * 0.3f;
            p = hit.point + hit.normal * Thickness + slide;
            previous = p - slide;
        }
        if (p.y < floor + Thickness)
        {
            p.y = floor + Thickness;
            previous = Vector3.Lerp(previous, p, 0.8f); // fabric on concrete doesn't slide far
        }
    }

    private static Mesh BuildMesh(Vector3[] positions, float spacing, Transform root)
    {
        // Two layers (front and back) so the underside at the hem isn't see-through, with their own normals
        int count = positions.Length;
        var vertices = new Vector3[count * 2];
        var uvs = new Vector2[count * 2];
        for (int k = 0; k < count; k++)
        {
            vertices[k] = vertices[k + count] = root.InverseTransformPoint(positions[k]);
            // UVs from the flat sheet, so the weave never stretches; about one texture repeat per metre
            int i = k / N, j = k % N;
            uvs[k] = uvs[k + count] = new Vector2(i * spacing, j * spacing);
        }

        var front = new List<int>();
        var back = new List<int>();
        for (int i = 0; i < N - 1; i++)
        for (int j = 0; j < N - 1; j++)
        {
            int a = i * N + j, b = a + 1, c = a + N, d = c + 1;
            front.AddRange(new[] { a, b, c, b, d, c });
            back.AddRange(new[] { a + count, c + count, b + count, b + count, c + count, d + count });
        }

        var mesh = new Mesh { name = "pap_tarp", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.subMeshCount = 1;
        front.AddRange(back);
        mesh.SetTriangles(front, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material BuildMaterial()
    {
        const int size = 512;
        var albedo = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var height = new float[size, size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Plain weave: alternating over/under threads
            float u = x / 4f, v = y / 4f;
            float warp = Mathf.Sin(u * Mathf.PI) * 0.5f + 0.5f, weft = Mathf.Sin(v * Mathf.PI) * 0.5f + 0.5f;
            bool over = ((int)u + (int)v) % 2 == 0;
            float weave = over ? warp : weft;
            height[x, y] = weave;

            // Faded olive canvas, darker in the creases, with grime and water stains
            float grime = Mathf.PerlinNoise(x * 0.012f, y * 0.012f);
            float stain = Mathf.Clamp01(Mathf.PerlinNoise(x * 0.004f + 30f, y * 0.004f + 30f) * 1.6f - 0.6f);
            Color canvas = Color.Lerp(new Color(0.30f, 0.31f, 0.24f), new Color(0.22f, 0.22f, 0.18f), grime);
            canvas = Color.Lerp(canvas, new Color(0.16f, 0.14f, 0.11f), stain * 0.6f);
            canvas *= 0.85f + 0.15f * weave;
            canvas.a = 1f;
            albedo.SetPixel(x, y, canvas);
        }
        albedo.Apply();

        var normal = new Texture2D(size, size, TextureFormat.RGBA32, true);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = height[(x + 1) % size, y] - height[(x + size - 1) % size, y];
            float dy = height[x, (y + 1) % size] - height[x, (y + size - 1) % size];
            Vector3 n = new Vector3(-dx * 1.5f, -dy * 1.5f, 1f).normalized;
            normal.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f));
        }
        normal.Apply();

        string albedoPath = Dir + "/pap_tarp_c.png", normalPath = Dir + "/pap_tarp_n.png";
        File.WriteAllBytes(albedoPath, albedo.EncodeToPNG());
        File.WriteAllBytes(normalPath, normal.EncodeToPNG());
        Object.DestroyImmediate(albedo);
        Object.DestroyImmediate(normal);
        AssetDatabase.ImportAsset(albedoPath);
        AssetDatabase.ImportAsset(normalPath);
        var normalImporter = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        normalImporter.textureType = TextureImporterType.NormalMap;
        normalImporter.SaveAndReimport();

        var material = new Material(Shader.Find("Standard")) { name = "pap_tarp" };
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        material.EnableKeyword("_NORMALMAP");
        material.SetFloat("_Glossiness", 0.12f);
        material.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(material, Dir + "/pap_tarp.mat");
        return material;
    }
}
