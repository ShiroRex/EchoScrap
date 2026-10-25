using System.Collections.Generic;
using EchoScrap.Combat;
using EchoScrap.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EchoScrap.Editor
{
    /// <summary>Resizes the saved layout and fits compound boxes to occupied model geometry.</summary>
    public static class ArenaReliefRepairAuthoring
    {
        private const float Size = 200f;
        private const string Folder = "Assets/_Project/Art/Mundo3D/Materiales/";

        [MenuItem("EchoScrap/Arte 3D/Actualizar relieve y cajas de arena 200m")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Stop Play Mode before updating the arena.");
            const string scenePath = "Assets/_Project/Scenes/CombatArena.unity";
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != scenePath)
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(scenePath);
            }
            var arena = GameObject.Find("Arena_120m").transform;
            var floor = arena.Find("Floor");
            var surface = floor.GetComponent<ArenaSurface>();
            float factor = Size / surface.WorldSize;
            var world = GameObject.Find("Mundo3D_EchoScrap").transform;
            // Move placement anchors, preserving model scale, rotation and prefab overrides.
            foreach (Transform group in world)
            {
                if (IsDecoration(group.name)) ScalePosition(group, factor);
                else foreach (Transform anchor in group) ScalePosition(anchor, factor);
            }
            foreach (var lava in Object.FindObjectsByType<LavaHazard>(FindObjectsSortMode.None))
                if (!lava.transform.IsChildOf(world)) ScalePosition(lava.transform, factor);
            surface.WorldSize = Size;
            BuildFloor(floor);
            Physics.SyncTransforms();
            int boxCount = 0, tinyRocks = 0;
            foreach (var collider in world.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (Transform group in world)
            {
                if (IsDecoration(group.name)) continue;
                foreach (Transform anchor in group)
                {
                    var old = anchor.Find("CollisionBoxes");
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                    var renderers = anchor.GetComponentsInChildren<Renderer>();
                    if (renderers.Length == 0) continue;
                    Bounds bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    // The horizontal hill profile scales with the layout; retain local model height.
                    float support = ArenaSurface.Profile(anchor.position.x, anchor.position.z, Size);
                    for (int i = 0; i < 4; i++)
                        support = Mathf.Min(support, ArenaSurface.Profile(i % 2 == 0 ? bounds.min.x : bounds.max.x,
                            i < 2 ? bounds.min.z : bounds.max.z, Size));
                    anchor.position += Vector3.up * (support - bounds.min.y);
                    if (anchor.name.StartsWith("roca_baja") && Mathf.Max(bounds.size.x, bounds.size.z) <= 1.65f && bounds.size.y <= 1f)
                    { tinyRocks++; continue; }
                    boxCount += FitBoxes(anchor);
                }
            }
            BuildWalls(arena);
            BuildBounds(arena);
            var settings = AssetDatabase.LoadAssetAtPath<CombatWaveSettings>("Assets/_Project/Settings/CombatWaves_Default.asset");
            settings.ArenaHalfExtents = Vector2.one * (Size * 0.5f);
            EditorUtility.SetDirty(settings);
            GameObject.Find("ArenaDirectionalLight").GetComponent<Light>().intensity = 0.22f;
            RenderSettings.ambientIntensity = 0.22f;
            // Trilight uses its colors directly, so scale these as well as the inspector multiplier.
            RenderSettings.ambientSkyColor = new Color(0.44f, 0.48f, 0.56f) * 0.22f;
            RenderSettings.ambientEquatorColor = new Color(0.35f, 0.29f, 0.22f) * 0.22f;
            RenderSettings.ambientGroundColor = new Color(0.19f, 0.14f, 0.10f) * 0.22f;
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"ARENA_REPAIR_OK size={Size} boxes={boxCount} tinyRocks={tinyRocks}");
        }

        private static bool IsDecoration(string name) => name == "Emblema_triangular" || name == "PortalVortex" || name == "PortalGlow";
        private static void ScalePosition(Transform target, float factor)
        { var p = target.position; p.x *= factor; p.z *= factor; target.position = p; }

        private static void BuildFloor(Transform floor)
        {
            const int n = 240;
            var vertices = new Vector3[(n + 1) * (n + 1)];
            var uv = new Vector2[vertices.Length]; var triangles = new int[n * n * 6];
            for (int z = 0; z <= n; z++) for (int x = 0; x <= n; x++)
            {
                float wx = (x / (float)n - 0.5f) * Size, wz = (z / (float)n - 0.5f) * Size;
                int i = z * (n + 1) + x;
                vertices[i] = new Vector3(wx / Size, ArenaSurface.Profile(wx, wz, Size), wz / Size);
                uv[i] = new Vector2(wx, wz) / 12f;
                if (x == n || z == n) continue;
                int t = (z * n + x) * 6;
                triangles[t] = i; triangles[t + 1] = i + n + 1; triangles[t + 2] = i + 1;
                triangles[t + 3] = i + 1; triangles[t + 4] = i + n + 1; triangles[t + 5] = i + n + 2;
            }
            var mesh = SaveMesh("Relieve_200", vertices, triangles, uv);
            floor.position = Vector3.zero; floor.localScale = new Vector3(Size, 1, Size);
            floor.GetComponent<MeshFilter>().sharedMesh = mesh;
            floor.GetComponent<MeshCollider>().sharedMesh = mesh;
        }

        private static void BuildWalls(Transform arena)
        {
            foreach (string name in new[] { "North", "South", "East", "West" })
            {
                var wall = arena.Find("Wall_" + name);
                float h = Size * 0.5f;
                Vector2 from = name == "North" ? new Vector2(-h, h) : name == "South" ? new Vector2(h, -h)
                    : name == "East" ? new Vector2(h, h) : new Vector2(-h, -h);
                Vector2 to = name == "North" ? new Vector2(h, h) : name == "South" ? new Vector2(-h, -h)
                    : name == "East" ? new Vector2(h, -h) : new Vector2(-h, h);
                var vertices = new List<Vector3>(); var indices = new List<int>();
                for (int i = 0; i < 200; i++)
                {
                    var a = Vector2.Lerp(from, to, i / 200f); var b = Vector2.Lerp(from, to, (i + 1) / 200f);
                    float ah = ArenaSurface.Profile(a.x, a.y, Size), bh = ArenaSurface.Profile(b.x, b.y, Size); int k = vertices.Count;
                    vertices.Add(new Vector3(a.x, ah - 0.2f, a.y)); vertices.Add(new Vector3(b.x, bh - 0.2f, b.y));
                    vertices.Add(new Vector3(a.x, ah + 2.7f, a.y)); vertices.Add(new Vector3(b.x, bh + 2.7f, b.y));
                    indices.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3, k, k + 1, k + 2, k + 1, k + 3, k + 2 });
                }
                var mesh = SaveMesh("Wall_" + name + "_200", vertices.ToArray(), indices.ToArray(), null);
                wall.position = Vector3.zero; wall.rotation = Quaternion.identity; wall.localScale = Vector3.one;
                wall.GetComponent<MeshFilter>().sharedMesh = mesh;
                foreach (var c in wall.GetComponents<Collider>()) Object.DestroyImmediate(c);
                wall.GetComponent<Renderer>().enabled = true;
            }
        }

        private static void BuildBounds(Transform arena)
        {
            var old = arena.Find("ArenaBounds"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("ArenaBounds").transform; root.SetParent(arena, false);
            foreach (string side in new[] { "North", "South", "East", "West" })
            {
                var go = new GameObject("Border_" + side); go.transform.SetParent(root, false);
                bool ns = side == "North" || side == "South"; float sign = side == "North" || side == "East" ? 1 : -1;
                go.transform.position = ns ? new Vector3(0, 15, sign * 101) : new Vector3(sign * 101, 15, 0);
                go.AddComponent<BoxCollider>().size = ns ? new Vector3(204, 50, 2) : new Vector3(2, 50, 204);
            }
        }

        private static Mesh SaveMesh(string name, Vector3[] vertices, int[] triangles, Vector2[] uv)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + name + ".asset");
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, Folder + name + ".asset"); }
            mesh.Clear(); mesh.name = name; mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices; mesh.triangles = triangles; if (uv != null) mesh.uv = uv;
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); return mesh;
        }

        private static int FitBoxes(Transform anchor)
        {
            var filters = anchor.GetComponentsInChildren<MeshFilter>();
            if (filters.Length == 0) return 0;
            Bounds bounds = new Bounds(); bool first = true;
            var triangles = new List<Vector3>();
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                var vertices = filter.sharedMesh.vertices; var indices = filter.sharedMesh.triangles;
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = filter.transform.TransformPoint(vertices[i]);
                    if (first) { bounds = new Bounds(vertices[i], Vector3.zero); first = false; } else bounds.Encapsulate(vertices[i]);
                }
                foreach (int index in indices) triangles.Add(vertices[index]);
            }
            if (first) return 0;
            // Keep architectural openings detailed. Solid rubble needs a stable, simpler envelope.
            bool detailed = anchor.name.StartsWith("casa_") || anchor.name.StartsWith("ruina_")
                || anchor.name.StartsWith("portal_") || anchor.name.StartsWith("arbol_")
                || anchor.name.StartsWith("tubo_") || anchor.name.StartsWith("marco_") || anchor.name.StartsWith("muro_");
            float cell = Mathf.Max(detailed ? 0.3f : 0.5f,
                Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) / (detailed ? 64f : 40f));
            Vector3 origin = bounds.min - Vector3.one * cell;
            int nx = Mathf.CeilToInt(bounds.size.x / cell) + 3, ny = Mathf.CeilToInt(bounds.size.y / cell) + 3, nz = Mathf.CeilToInt(bounds.size.z / cell) + 3;
            var occupied = new bool[nx * ny * nz];
            int Index(int x, int y, int z) => (y * nz + z) * nx + x;
            Vector3 half = Vector3.one * (cell * 0.5f + 0.0001f);
            for (int t = 0; t < triangles.Count; t += 3)
            {
                Vector3 a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                var min = (Vector3.Min(a, Vector3.Min(b, c)) - origin) / cell;
                var max = (Vector3.Max(a, Vector3.Max(b, c)) - origin) / cell;
                for (int y = Mathf.FloorToInt(min.y); y <= Mathf.FloorToInt(max.y); y++)
                for (int z = Mathf.FloorToInt(min.z); z <= Mathf.FloorToInt(max.z); z++)
                for (int x = Mathf.FloorToInt(min.x); x <= Mathf.FloorToInt(max.x); x++)
                {
                    int index = Index(x, y, z); if (occupied[index]) continue;
                    Vector3 center = origin + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cell;
                    if (TriangleIntersectsBox(a - center, b - center, c - center, half)) occupied[index] = true;
                }
            }
            // Close only vertical cracks too short for a character. This avoids thousands of
            // tiny boxes on scrap piles while keeping doorways and overhead passages open.
            int maxGap = Mathf.FloorToInt(1.25f / cell);
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
            {
                int last = -1;
                for (int y = 0; y < ny; y++) if (occupied[Index(x, y, z)])
                {
                    if (last >= 0 && y - last - 1 <= maxGap)
                        for (int fill = last + 1; fill < y; fill++) occupied[Index(x, fill, z)] = true;
                    last = y;
                }
            }
            // Flood exterior space: enclosed model interiors become solid; doorways stay open.
            var outside = new bool[occupied.Length]; var queue = new Queue<int>(); queue.Enqueue(0); outside[0] = true;
            while (queue.Count > 0)
            {
                int index = queue.Dequeue(), x = index % nx, z = index / nx % nz, y = index / (nx * nz);
                void Visit(int vx, int vy, int vz)
                {
                    if (vx < 0 || vx >= nx || vy < 0 || vy >= ny || vz < 0 || vz >= nz) return;
                    int next = Index(vx, vy, vz); if (occupied[next] || outside[next]) return;
                    outside[next] = true; queue.Enqueue(next);
                }
                Visit(x - 1, y, z); Visit(x + 1, y, z); Visit(x, y - 1, z); Visit(x, y + 1, z); Visit(x, y, z - 1); Visit(x, y, z + 1);
            }
            for (int i = 0; i < occupied.Length; i++) occupied[i] |= !outside[i];
            var root = new GameObject("CollisionBoxes").transform; root.SetParent(anchor, false);
            root.position = Vector3.zero; root.rotation = Quaternion.identity;
            var scale = anchor.lossyScale; root.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            int count = 0;
            for (int y = 0; y < ny; y++) for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
            {
                if (!occupied[Index(x, y, z)]) continue;
                int ex = x + 1, ez = z + 1, ey = y + 1;
                while (ex < nx && occupied[Index(ex, y, z)]) ex++;
                bool Filled(int cy, int cz)
                { for (int cx = x; cx < ex; cx++) if (!occupied[Index(cx, cy, cz)]) return false; return true; }
                while (ez < nz && Filled(y, ez)) ez++;
                bool LayerFilled(int cy)
                { for (int cz = z; cz < ez; cz++) if (!Filled(cy, cz)) return false; return true; }
                while (ey < ny && LayerFilled(ey)) ey++;
                for (int cy = y; cy < ey; cy++) for (int cz = z; cz < ez; cz++) for (int cx = x; cx < ex; cx++) occupied[Index(cx, cy, cz)] = false;
                var box = root.gameObject.AddComponent<BoxCollider>();
                box.center = origin + new Vector3((x + ex) * 0.5f, (y + ey) * 0.5f, (z + ez) * 0.5f) * cell;
                box.size = new Vector3(ex - x, ey - y, ez - z) * cell;
                count++;
            }
            return count;
        }

        private static bool TriangleIntersectsBox(Vector3 a, Vector3 b, Vector3 c, Vector3 half)
        {
            bool Separated(Vector3 axis)
            {
                float p = Vector3.Dot(a, axis), q = Vector3.Dot(b, axis), r = Vector3.Dot(c, axis);
                float extent = Vector3.Dot(half, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)));
                return Mathf.Min(p, Mathf.Min(q, r)) > extent || Mathf.Max(p, Mathf.Max(q, r)) < -extent;
            }
            if (Separated(Vector3.right) || Separated(Vector3.up) || Separated(Vector3.forward)) return false;
            Vector3 ab = b - a, bc = c - b, ca = a - c;
            if (Separated(Vector3.Cross(ab, bc))) return false;
            bool EdgeSeparated(Vector3 edge) => Separated(Vector3.Cross(edge, Vector3.right)) || Separated(Vector3.Cross(edge, Vector3.up)) || Separated(Vector3.Cross(edge, Vector3.forward));
            if (EdgeSeparated(ab) || EdgeSeparated(bc) || EdgeSeparated(ca)) return false;
            return true;
        }
    }
}
