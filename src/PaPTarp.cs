using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PaPHideoutModule
{
    /// <summary>
    /// An old tarp thrown over the machine while its area isn't built. The cloth is draped over the machine's real
    /// shape (a height map taken from its mesh, softened so it hangs like fabric) and wears the material of one of the
    /// tarps already in the hideout scene, so it looks like it was always there.
    /// </summary>
    internal static class PaPTarp
    {
        private const int Grid = 56;
        private const float Margin = 0.35f;

        // Tarps the hideout scene ships with; the first one found lends its material
        private static readonly string[] TarpNames = { "City_Generic_Tarp_04", "Sniper_defence_tarps_05", "Sniper_defence_tarps_06", "trash_VinilTents" };

        public static GameObject Build(GameObject machine)
        {
            // The cloth simulated in Unity and shipped in the bundle, if this bundle has it
            Transform baked = machine.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "pap_tarp");
            if (baked != null)
            {
                try
                {
                    UseSceneTarpLook(baked.gameObject, machine.transform);
                    ClearSceneProps(baked.gameObject, machine.transform);
                }
                catch (Exception e)
                {
                    PaPHideoutModulePlugin.Log.LogWarning($"Could not fit the tarp around the room's props: {e.Message}");
                }
                return baked.gameObject;
            }

            try
            {
                return Drape(machine);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogWarning($"Could not build the tarp: {e.Message}");
                return null;
            }
        }

        private static GameObject Drape(GameObject machine)
        {
            Transform root = machine.transform;
            SkinnedMeshRenderer[] skinned = machine.GetComponentsInChildren<SkinnedMeshRenderer>();
            MeshRenderer[] rigid = machine.GetComponentsInChildren<MeshRenderer>();

            // Temporary colliders on the machine's actual meshes, for the height map; the real colliders are just boxes
            var probe = new GameObject("PaP_TarpProbe");
            var bakedMeshes = new System.Collections.Generic.List<Mesh>();
            int layer = 31;
            try
            {
                foreach (SkinnedMeshRenderer smr in skinned)
                {
                    var baked = new Mesh();
                    bakedMeshes.Add(baked);
                    smr.BakeMesh(baked); // posed
                    baked.RecalculateBounds();
                    // The bones already carry the prefab's scale, so the baked mesh can be full size: only scale the
                    // probe if that brings it to the size the renderer really draws (applying it twice shrank the
                    // machine to a bump and the sheet fell flat)
                    float drawn = smr.bounds.size.magnitude;
                    float unscaled = baked.bounds.size.magnitude;
                    float scaled = Vector3.Scale(baked.bounds.size, smr.transform.lossyScale).magnitude;
                    bool applyScale = Mathf.Abs(scaled - drawn) < Mathf.Abs(unscaled - drawn);
                    AddProbe(probe, smr.transform, baked, layer, applyScale);
                }
                foreach (MeshRenderer mr in rigid)
                {
                    MeshFilter filter = mr.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                        AddProbe(probe, mr.transform, filter.sharedMesh, layer, true);
                }
                Physics.SyncTransforms();

                Bounds bounds = WorldBounds(machine);
                float floor = root.position.y;
                float top = bounds.max.y + 0.05f;
                Vector3 right = root.right, forward = root.forward;
                float halfX = Extent(bounds, right) + Margin, halfZ = Extent(bounds, forward) + Margin;
                Vector3 centre = new Vector3(bounds.center.x, floor, bounds.center.z);

                // Height of the machine under each point of the sheet (floor where there's nothing)
                var height = new float[Grid + 1, Grid + 1];
                for (int i = 0; i <= Grid; i++)
                for (int j = 0; j <= Grid; j++)
                {
                    Vector3 p = centre + right * Mathf.Lerp(-halfX, halfX, i / (float)Grid) + forward * Mathf.Lerp(-halfZ, halfZ, j / (float)Grid);
                    height[i, j] = Physics.Raycast(new Vector3(p.x, top + 1f, p.z), Vector3.down, out RaycastHit hit, top - floor + 2f, 1 << layer)
                        ? hit.point.y + 0.03f
                        : floor + 0.02f;
                }

                // Cloth sags between high points but never through the machine: blur, keep at least the surface below
                float[,] raw = (float[,])height.Clone();
                for (int pass = 0; pass < 10; pass++)
                {
                    var next = new float[Grid + 1, Grid + 1];
                    for (int i = 0; i <= Grid; i++)
                    for (int j = 0; j <= Grid; j++)
                    {
                        float sum = 0f;
                        int n = 0;
                        for (int di = -1; di <= 1; di++)
                        for (int dj = -1; dj <= 1; dj++)
                        {
                            int a = Mathf.Clamp(i + di, 0, Grid), b = Mathf.Clamp(j + dj, 0, Grid);
                            sum += height[a, b];
                            n++;
                        }
                        next[i, j] = Mathf.Max(raw[i, j], sum / n);
                    }
                    height = next;
                }

                var vertices = new Vector3[(Grid + 1) * (Grid + 1)];
                var uvs = new Vector2[vertices.Length];
                var seed = UnityEngine.Random.Range(0f, 100f);
                for (int i = 0; i <= Grid; i++)
                for (int j = 0; j <= Grid; j++)
                {
                    float u = Mathf.Lerp(-halfX, halfX, i / (float)Grid), v = Mathf.Lerp(-halfZ, halfZ, j / (float)Grid);
                    // Wrinkles, stronger where the cloth hangs
                    float hang = Mathf.Clamp01((top - height[i, j]) / Mathf.Max(0.01f, top - floor));
                    float wrinkle = (Mathf.PerlinNoise(seed + u * 3.1f, seed + v * 3.1f) - 0.5f) * (0.03f + 0.05f * hang);
                    Vector3 world = centre + right * u + forward * v + Vector3.up * (height[i, j] - floor + wrinkle);
                    int index = i * (Grid + 1) + j;
                    vertices[index] = root.InverseTransformPoint(world);
                    // Texture follows the fabric down the sides instead of smearing
                    uvs[index] = new Vector2(u + (height[i, j] - floor) * Mathf.Sign(u), v + (height[i, j] - floor) * Mathf.Sign(v)) * 0.6f;
                }

                var triangles = new int[Grid * Grid * 12];
                int t = 0;
                for (int i = 0; i < Grid; i++)
                for (int j = 0; j < Grid; j++)
                {
                    int a = i * (Grid + 1) + j, b = a + 1, c = a + Grid + 1, d = c + 1;
                    // Both faces, so the underside isn't see-through at the hem
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                    triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }

                var mesh = new Mesh { name = "PaP tarp", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.vertices = vertices;
                mesh.uv = uvs;
                mesh.triangles = triangles;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                var tarp = new GameObject("PaP_Tarp");
                tarp.transform.SetParent(root, false);
                tarp.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = tarp.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = TarpMaterial();
                return tarp;
            }
            finally
            {
                Object.Destroy(probe);
                foreach (Mesh baked in bakedMeshes)
                    Object.Destroy(baked);
            }
        }

        /// <summary>
        /// The baked cloth was simulated without the room around it, so its hem can run through the props next to
        /// the machine (the ladder leaning there). Every point of the sheet inside a scene collider is pushed out to
        /// that collider's surface, so the cloth rests against the prop instead of passing through it. The machine,
        /// triggers and anything that moves (the player) are left alone.
        /// </summary>
        private static void ClearSceneProps(GameObject tarp, Transform machine)
        {
            MeshFilter filter = tarp.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                return;
            Mesh mesh = Object.Instantiate(filter.sharedMesh); // the bundle's mesh stays as it is
            mesh.name = "pap_tarp (fitted)";

            const float radius = 0.025f;
            var probe = new GameObject("PaP_TarpVertexProbe");
            var sphere = probe.AddComponent<SphereCollider>();
            sphere.radius = radius;
            var hits = new Collider[16];
            Transform t = tarp.transform;
            Vector3[] vertices = mesh.vertices;
            int moved = 0;
            List<GameObject> solids = AddPropSolids(tarp, machine);
            List<Bounds> ladders = LaddersOn(tarp, machine);
            try
            {
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 world = t.TransformPoint(vertices[i]);
                    bool pushed = FlattenUnder(ladders, ref world);
                    int count = Physics.OverlapSphereNonAlloc(world, radius, hits, ~0, QueryTriggerInteraction.Ignore);
                    for (int h = 0; h < count; h++)
                    {
                        Collider hit = hits[h];
                        if (hit == sphere || hit.transform.IsChildOf(machine) || hit is CharacterController || hit.attachedRigidbody != null
                            || LadderName.IsMatch(NameWithParents(hit.transform)))
                            continue;
                        if (Physics.ComputePenetration(sphere, world, Quaternion.identity, hit, hit.transform.position, hit.transform.rotation,
                                out Vector3 direction, out float distance))
                        {
                            world += direction * (distance + 0.005f);
                            pushed = true;
                        }
                    }
                    if (pushed)
                    {
                        vertices[i] = t.InverseTransformPoint(world);
                        moved++;
                    }
                }
            }
            finally
            {
                Object.Destroy(probe);
                foreach (GameObject solid in solids)
                    Object.Destroy(solid);
            }

            if (moved == 0)
            {
                Object.Destroy(mesh);
                return;
            }
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            PaPHideoutModulePlugin.Log.LogInfo($"Tarp fitted around the room's props ({moved} cloth points moved).");
        }

        private static readonly Regex LadderName = new Regex("ladder", RegexOptions.IgnoreCase);
        private const float LadderMargin = 0.25f; // metres over which the flattened patch eases back into the drape

        /// <summary>The room's step ladder standing on the tarp: its world box (it's static-batched, so no mesh to use).</summary>
        private static List<Bounds> LaddersOn(GameObject tarp, Transform machine)
        {
            Renderer tarpRenderer = tarp.GetComponent<Renderer>();
            if (tarpRenderer == null)
                return new List<Bounds>();
            Bounds area = tarpRenderer.bounds;
            return Object.FindObjectsOfType<MeshRenderer>()
                .Where(r => r.enabled && !r.transform.IsChildOf(machine) && r.bounds.Intersects(area) && LadderName.IsMatch(NameWithParents(r.transform)))
                .Select(r => r.bounds)
                .ToList();
        }

        /// <summary>
        /// Under the ladder the tarp lies flat at its feet, easing back up to the drape around it, so the ladder stands
        /// on the sheet instead of the cloth tenting around it.
        /// </summary>
        private static bool FlattenUnder(List<Bounds> ladders, ref Vector3 world)
        {
            bool moved = false;
            foreach (Bounds ladder in ladders)
            {
                float dx = Mathf.Max(0f, Mathf.Abs(world.x - ladder.center.x) - ladder.extents.x);
                float dz = Mathf.Max(0f, Mathf.Abs(world.z - ladder.center.z) - ladder.extents.z);
                float outside = Mathf.Sqrt(dx * dx + dz * dz);
                float feet = ladder.min.y + 0.01f;
                if (outside >= LadderMargin || world.y <= feet)
                    continue;
                world.y = Mathf.Lerp(feet, world.y, Mathf.SmoothStep(0f, 1f, outside / LadderMargin));
                moved = true;
            }
            return moved;
        }

        // Tarkov's tarp textures cover a couple of metres; the baked sheet's UVs are in BO3 metres (machine scale 1)
        private const float TarpTextureMetres = 2.5f;

        /// <summary>The baked cloth dressed in one of the hideout's own tarps (if the scene has one) instead of the bake's canvas.</summary>
        private static void UseSceneTarpLook(GameObject tarp, Transform machine)
        {
            Material scene = SceneTarpMaterial();
            MeshFilter filter = tarp.GetComponent<MeshFilter>();
            Renderer renderer = tarp.GetComponent<Renderer>();
            if (scene == null || filter == null || filter.sharedMesh == null || renderer == null)
                return;
            Mesh mesh = Object.Instantiate(filter.sharedMesh); // the bundle's mesh stays as it is
            mesh.name = "pap_tarp (scene look)";
            float uvScale = machine.lossyScale.x / TarpTextureMetres;
            mesh.uv = mesh.uv.Select(uv => uv * uvScale).ToArray();
            mesh.RecalculateTangents(); // Tarkov's tarp shader is normal-mapped
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = scene;
        }

        // (Not the ladder: the tarp lies flat under it instead of tenting round it)
        private static readonly Regex SolidPropName = new Regex("stair|step", RegexOptions.IgnoreCase);

        /// <summary>
        /// Props under the tarp the room gives no usable collider (the step ladder: none, or only a coarse low-poly
        /// one) get a temporary solid for the push-out: the convex hull of their own mesh, so the cloth drapes over
        /// the ladder's shape and thin rails can't slip between cloth points, or their box when the mesh is
        /// static-batched into the room. Logs what is near, so a prop with another name can be added.
        /// </summary>
        private static List<GameObject> AddPropSolids(GameObject tarp, Transform machine)
        {
            var solids = new List<GameObject>();
            Renderer tarpRenderer = tarp.GetComponent<Renderer>();
            if (tarpRenderer == null)
                return solids;
            Bounds area = tarpRenderer.bounds;
            area.Expand(0.3f);
            foreach (MeshRenderer renderer in Object.FindObjectsOfType<MeshRenderer>())
            {
                if (!renderer.enabled || renderer.transform.IsChildOf(machine) || renderer.transform.IsChildOf(tarp.transform)
                    || !renderer.bounds.Intersects(area) || !SolidPropName.IsMatch(NameWithParents(renderer.transform)))
                    continue;

                Mesh source = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                bool batched = source == null || !source.isReadable || source.name.StartsWith("Combined Mesh");
                var solid = new GameObject("PaP_TarpPropSolid");
                if (batched)
                {
                    solid.transform.position = renderer.bounds.center;
                    solid.AddComponent<BoxCollider>().size = renderer.bounds.size;
                }
                else
                {
                    solid.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                    solid.transform.localScale = renderer.transform.lossyScale;
                    solid.AddComponent<MeshCollider>().convex = true;
                    solid.GetComponent<MeshCollider>().sharedMesh = source;
                }
                solids.Add(solid);
            }
            if (solids.Count > 0)
                Physics.SyncTransforms();
            return solids;
        }

        private static string NameWithParents(Transform t) =>
            t.parent == null ? t.name : t.parent.parent == null ? $"{t.parent.name}/{t.name}" : $"{t.parent.parent.name}/{t.parent.name}/{t.name}";

        private static void AddProbe(GameObject probe, Transform source, Mesh mesh, int layer, bool applyScale)
        {
            var part = new GameObject("part") { layer = layer };
            part.transform.SetParent(probe.transform, false);
            part.transform.SetPositionAndRotation(source.position, source.rotation);
            part.transform.localScale = applyScale ? source.lossyScale : Vector3.one;
            part.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private static float Extent(Bounds bounds, Vector3 axis) =>
            Mathf.Abs(Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z))));

        private static Bounds WorldBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers.Skip(1))
                bounds.Encapsulate(r.bounds);
            return bounds;
        }

        private static Material _fallback;

        private static Material TarpMaterial() => SceneTarpMaterial() ?? Fallback();

        /// <summary>The material of one of the hideout's own tarps, or null if the scene has none.</summary>
        private static Material SceneTarpMaterial()
        {
            Renderer found = Resources.FindObjectsOfTypeAll<MeshRenderer>()
                // Not the shadow-only copies (City_Generic_Tarp_04_SHADOW_LOD0 / "SHADOW_TRUE"): they draw as noise
                .Where(r => r.gameObject.scene.IsValid() && r.sharedMaterial != null
                            && r.name.IndexOf("SHADOW", StringComparison.OrdinalIgnoreCase) < 0
                            && r.sharedMaterial.name.IndexOf("SHADOW", StringComparison.OrdinalIgnoreCase) < 0)
                .Select(r => (r, rank: Array.FindIndex(TarpNames, n => r.name.StartsWith(n, StringComparison.OrdinalIgnoreCase))))
                .Where(x => x.rank >= 0)
                .OrderBy(x => x.rank)
                .Select(x => x.r)
                .FirstOrDefault();
            if (found != null)
            {
                PaPHideoutModulePlugin.Log.LogInfo($"Tarp uses the material of {found.name} ({found.sharedMaterial.name}).");
                return found.sharedMaterial;
            }
            return null;
        }

        private static Material Fallback()
        {
            PaPHideoutModulePlugin.Log.LogInfo("No tarp in the scene to borrow from; using a plain canvas material.");
            if (_fallback == null)
            {
                _fallback = new Material(Shader.Find("Standard")) { name = "PaP tarp canvas", color = new Color(0.22f, 0.24f, 0.2f) };
                _fallback.SetFloat("_Glossiness", 0.15f);
            }
            return _fallback;
        }
    }
}
