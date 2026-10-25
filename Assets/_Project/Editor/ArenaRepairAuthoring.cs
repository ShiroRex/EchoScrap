using System.Collections.Generic;
using EchoScrap.Combat;
using EchoScrap.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EchoScrap.Editor
{
    /// <summary>Authored combat lanes, supported props and a closed scrapyard perimeter.</summary>
    public static class ArenaRepairAuthoring
    {
        private const string Folder = "Assets/_Project/Art/Mundo3D/Materiales/ArenaPlayable/";
        private const string Models = "Assets/_Project/Art/Mundo3D/Modelos/";
        private static Material concrete, steel, ash, ground, slag;
        private static Transform world;
        private static int number;

        [MenuItem("EchoScrap/Mapa/Aplicar correccion de jugabilidad")]
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/CombatArena.unity");
            System.IO.Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            concrete = Material("Concrete", new Color(0.21f, 0.225f, 0.23f));
            steel = Material("Steel", new Color(0.15f, 0.18f, 0.19f), 0.5f);
            ash = Material("Ash", new Color(0.08f, 0.075f, 0.065f));
            ground = AssetDatabase.LoadAssetAtPath<Material>(Folder + "Ground.mat");
            if (ground == null)
            {
                ground = new Material(Shader.Find("EchoScrap/WastelandGround"));
                AssetDatabase.CreateAsset(ground, Folder + "Ground.mat");
            }
            BuildFloor();
            var old = GameObject.Find("Mundo3D_EchoScrap");
            Transform emblem = old.transform.Find("Emblema_triangular");
            Transform portal = old.transform.Find("Portal");
            Transform vortex = old.transform.Find("PortalVortex");
            Transform glow = old.transform.Find("PortalGlow");
            foreach (var keep in new[] { emblem, portal, vortex, glow }) if (keep != null) keep.SetParent(null, true);
            Object.DestroyImmediate(old);
            world = new GameObject("Mundo3D_EchoScrap").transform;
            world.SetParent(GameObject.Find("Arena_120m").transform, false); number = 0;
            foreach (var keep in new[] { emblem, portal, vortex, glow }) if (keep != null) keep.SetParent(world, true);
            if (emblem != null)
            {
                foreach (var c in emblem.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                emblem.position = new Vector3(0f, -0.10f, 0f);
            }
            if (portal != null)
            {
                foreach (var c in portal.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                Support(portal.GetChild(0));
            }
            if (glow != null) { glow.position = OnGround(new Vector3(43, 0, -40), 2f); glow.GetComponent<Light>().intensity = 1.7f; }
            if (vortex != null) vortex.position = OnGround(vortex.position, 0.07f);
            Layout();
            BuildPerimeter();
            BuildSlag();
            Lighting();
            var player = GameObject.Find("Player").transform;
            var navigation = world.gameObject.AddComponent<ArenaNavigation>();
            navigation.Target = player;
            var obstacles = new List<Collider>(world.GetComponentsInChildren<Collider>());
            foreach (string side in new[] { "North", "South", "East", "West" })
                obstacles.Add(GameObject.Find("Arena_120m/Wall_" + side).GetComponent<Collider>());
            world.gameObject.AddComponent<ArenaOcclusion>().Bind(GameObject.Find("Main Camera").GetComponent<Camera>(),
                player.GetComponent<CollectorSpriteView>().CharacterRenderer, obstacles.ToArray(), FadeVariants(obstacles));
            player.SetPositionAndRotation(Vector3.up, Quaternion.identity);
            var rb = player.GetComponent<Rigidbody>(); rb.position = Vector3.up; rb.rotation = Quaternion.identity;
            Physics.SyncTransforms();
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            Debug.Log("ARENA_REPAIR_OK props=" + number + " perimeter=closed slag=3 cameraScale=fixed");
        }

        private static void Layout()
        {
            // The centre and a broad ring remain open. Large silhouettes sit along the rim.
            Place("casa", -48, 38, 10, 12, true);
            Place("carro", -38, 31, 5, -20, true); Place("carro", -43, 19, 5.5f, 65, true);
            Place("carro", -37, 9, 5, 18, true);
            Place("chatarra_alta", -46, -5, 8, 50, true);
            Place("contenedor", -40, -24, 8, 15, true); Place("casa", -48, -37, 9, 95, true);
            Place("chatarra_baja", -33, -46, 9, 15, true);
            Place("deposito", -33, 46, 7, 0, true); Place("ruina_almacen", -15, 48, 12, 0, true);
            Place("chatarra_alta", 11, 48, 10, -12, true); Place("contenedor", 32, 47, 9, 0, true);
            Place("ruina_almacen", 48, 32, 11, 90, true); Place("casa", 49, 6, 10, -90, true);
            Place("chatarra_baja", 48, -21, 8, 35, true);
            Place("contenedor", -17, -49, 9, 0, true); Place("chatarra_baja", 1, -49, 9, 0, true);
            Place("muro_derrumbe", 20, -49, 8, 0, true);
            Place("arbol_seco", -23, 17, 5.5f, 30, true); Place("arbol_seco", -2, 35, 4.5f, 15, true);
            Place("arbol_seco", 24, -18, 5.5f, 12, true); Place("arbol_seco", -14, -33, 5.5f, -15, true);
            Place("roca_baja", -27, -12, 3, 30, true); Place("roca_partida", 23, 22, 3.5f, 12, true);
            Place("tocon_bajo", 31, 3, 3, 0, true);
            Place("tubo_roto", 35, 33, 5, 0, true); Place("tubo_codo", 43, 24, 4, 85, true);
            Place("tubo_roto", 46, 14, 4, -40, true);
            var details = new[] { new Vector2(-41,35), new Vector2(-44,10), new Vector2(-46,-16),
                new Vector2(-37,-35), new Vector2(-24,46), new Vector2(2,47), new Vector2(39,42),
                new Vector2(46,-12), new Vector2(23,-45), new Vector2(-27,-47), new Vector2(-28,20), new Vector2(27,-24) };
            for (int i = 0; i < details.Length; i++)
            {
                var p = details[i];
                Place(i % 2 == 0 ? "escombros_concreto" : "escombros_de_ladrillo", p.x, p.y, 1.6f, i * 31, false);
                Place("llanta", p.x + 2.1f, p.y - 0.9f, 0.8f, i * 27, false);
                Place(i % 2 == 0 ? "cajas" : "bolsa_basura", p.x - 1.7f, p.y + 0.8f, 1.1f, i * 13, false);
                Place("tablas", p.x + 0.2f, p.y - 1.9f, 1.4f, i * 11, false);
            }
        }

        private static Material[] FadeVariants(List<Collider> obstacles)
        {
            var originals = new HashSet<Material>(); var variants = new List<Material>();
            foreach (var obstacle in obstacles)
            {
                Transform root = obstacle.name == "SolidFootprint" ? obstacle.transform.parent : obstacle.transform;
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
                    foreach (var original in renderer.sharedMaterials) originals.Add(original);
            }
            foreach (var original in originals)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out string guid, out long id);
                string path = Folder + "Fade_" + guid + "_" + id + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(original); AssetDatabase.CreateAsset(material, path); }
                material.CopyPropertiesFromMaterial(original); ArenaOcclusion.ConfigureTransparent(material);
                EditorUtility.SetDirty(material); variants.Add(material);
            }
            return variants.ToArray();
        }

        private static void Place(string asset, float x, float z, float width, float yaw, bool solid)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Models + asset + ".fbx");
            if (prefab == null) throw new System.IO.FileNotFoundException(asset);
            var anchor = new GameObject(asset + "_" + (++number)).transform; anchor.SetParent(world, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab); model.transform.SetParent(anchor, false);
            var bounds = BoundsOf(anchor); float size = Mathf.Max(bounds.size.x, bounds.size.z);
            anchor.localScale = Vector3.one * width / size;
            if (asset == "chatarra_alta") anchor.localScale = Vector3.Scale(anchor.localScale, new Vector3(1, 0.55f, 1));
            anchor.SetPositionAndRotation(new Vector3(x, 0, z), Quaternion.Euler(0, yaw, 0));
            bounds = BoundsOf(anchor);
            anchor.position += new Vector3(x - bounds.center.x, 0f, z - bounds.center.z);
            foreach (var c in anchor.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            anchor.rotation = Quaternion.FromToRotation(Vector3.up, GroundNormal(anchor.position)) * anchor.rotation;
            Support(anchor);
            if (!solid) return;
            bounds = BoundsOf(anchor);
            if (asset.StartsWith("arbol") || asset.StartsWith("tocon"))
            {
                var trunk = anchor.gameObject.AddComponent<CapsuleCollider>();
                float radius = Mathf.Min(0.75f, width * 0.13f);
                trunk.radius = radius / anchor.lossyScale.x; trunk.height = 2.6f / anchor.lossyScale.y;
                trunk.center = anchor.InverseTransformPoint(new Vector3(bounds.center.x, Height(anchor.position) + 1.25f, bounds.center.z));
            }
            else
            {
                // One simple footprint per solid prop; tiny debris is visual dressing.
                var block = new GameObject("SolidFootprint"); block.transform.SetParent(anchor, false);
                block.transform.rotation = Quaternion.identity;
                float hitHeight = Mathf.Clamp(bounds.size.y, 0.3f, 6f);
                block.transform.position = new Vector3(bounds.center.x, bounds.min.y + hitHeight * 0.5f, bounds.center.z);
                block.transform.localScale = new Vector3(1f / anchor.lossyScale.x, 1f / anchor.lossyScale.y, 1f / anchor.lossyScale.z);
                var box = block.AddComponent<BoxCollider>();
                box.size = new Vector3(Mathf.Max(0.5f, bounds.size.x * 0.9f), hitHeight, Mathf.Max(0.5f, bounds.size.z * 0.9f));
            }
        }

        private static Bounds BoundsOf(Transform anchor)
        {
            var renderers = anchor.GetComponentsInChildren<Renderer>();
            var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds); return b;
        }
        private static void Support(Transform anchor)
        {
            var bounds = BoundsOf(anchor);
            // Sample the actual feet/wheels, rather than the corners of an AABB.
            // A tilted AABB's lowest corner is usually empty space.
            var gaps = new List<float>();
            float bottomBand = Mathf.Max(0.10f, bounds.size.y * 0.08f);
            foreach (var filter in anchor.GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 p = filter.transform.TransformPoint(vertex);
                    if (p.y <= bounds.min.y + bottomBand) gaps.Add(p.y - Height(p));
                }
            gaps.Sort();
            float gap = gaps.Count > 0 ? gaps[gaps.Count / 3] : bounds.min.y - Height(bounds.center);
            anchor.position -= Vector3.up * (gap + 0.035f);
        }

        private static void BuildFloor()
        {
            const int steps = 120; var v = new Vector3[(steps + 1) * (steps + 1)]; var uv = new Vector2[v.Length];
            var t = new int[steps * steps * 6];
            for (int z = 0; z <= steps; z++) for (int x = 0; x <= steps; x++)
            {
                float wx = x - 60, wz = z - 60; int i = z * (steps + 1) + x;
                v[i] = new Vector3(wx / 120f, ArenaSurface.Profile(wx, wz), wz / 120f); uv[i] = new Vector2(wx, wz) / 12f;
                if (x == steps || z == steps) continue;
                int k = (z * steps + x) * 6;
                t[k] = i; t[k + 1] = i + steps + 1; t[k + 2] = i + 1;
                t[k + 3] = i + 1; t[k + 4] = i + steps + 1; t[k + 5] = i + steps + 2;
            }
            Mesh mesh = SaveMesh("GroundMesh", v, t, uv);
            var floor = GameObject.Find("Arena_120m/Floor");
            floor.GetComponent<MeshFilter>().sharedMesh = mesh; floor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            floor.transform.localScale = new Vector3(120, 1, 120); floor.GetComponent<MeshCollider>().sharedMesh = mesh;
            floor.GetComponent<Renderer>().sharedMaterial = ground;
            // Continue the ground beyond the physical perimeter, preventing a black void in edge views.
            var old = GameObject.Find("ArenaBackdrop"); if (old != null) Object.DestroyImmediate(old);
            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Plane); backdrop.name = "ArenaBackdrop";
            Object.DestroyImmediate(backdrop.GetComponent<Collider>());
            backdrop.transform.position = Vector3.down * 0.25f; backdrop.transform.localScale = Vector3.one * 22f;
            backdrop.GetComponent<Renderer>().sharedMaterial = ground;
        }

        private static void BuildPerimeter()
        {
            foreach (string side in new[] { "North", "South", "East", "West" })
            {
                var wall = GameObject.Find("Arena_120m/Wall_" + side);
                foreach (var c in wall.GetComponents<Collider>()) Object.DestroyImmediate(c);
                bool horizontal = side == "North" || side == "South"; float sign = side == "North" || side == "East" ? 1f : -1f;
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                for (int i = 0; i < 60; i++)
                {
                    float a = -60 + i * 2, b = a + 2;
                    Vector3 p = horizontal ? new Vector3(a, 0, sign * 59) : new Vector3(sign * 59, 0, a);
                    Vector3 q = horizontal ? new Vector3(b, 0, sign * 59) : new Vector3(sign * 59, 0, b);
                    float bottom = Mathf.Min(Height(p), Height(q)) - 0.2f;
                    float top = Mathf.Max(Height(p), Height(q)) + 3.3f;
                    AddBox(vertices, triangles, (p + q) * 0.5f + Vector3.up * (bottom + top) * 0.5f,
                        horizontal ? new Vector3(2f, top - bottom, 2f) : new Vector3(2f, top - bottom, 2f));
                }
                wall.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); wall.transform.localScale = Vector3.one;
                wall.GetComponent<MeshFilter>().sharedMesh = SaveMesh("Wall_" + side, vertices.ToArray(), triangles.ToArray());
                var renderer = wall.GetComponent<Renderer>(); renderer.sharedMaterial = concrete; renderer.enabled = true;
                var box = wall.AddComponent<BoxCollider>();
                box.center = horizontal ? new Vector3(0, 4, sign * 59) : new Vector3(sign * 59, 4, 0);
                box.size = horizontal ? new Vector3(120, 12, 2) : new Vector3(2, 12, 120);
            }
        }

        private static void BuildSlag()
        {
            var old = GameObject.Find("LavaZones"); if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("LavaZones");
            slag = AssetDatabase.LoadAssetAtPath<Material>(Folder + "Slag.mat");
            if (slag == null)
            {
                slag = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Lava/Lava.mat"));
                AssetDatabase.CreateAsset(slag, Folder + "Slag.mat");
            }
            slag.shader = Shader.Find("EchoScrap/MoltenWaste"); slag.EnableKeyword("_EMISSION");
            slag.SetColor("_BaseColor", new Color(0.6f, 0.42f, 0.28f));
            slag.SetColor("_EmissionColor", new Color(1.2f, 0.36f, 0.055f)); EditorUtility.SetDirty(slag);
            Vector3[] positions = { new Vector3(35, 0, 28), new Vector3(41, 0, 21), new Vector3(43, 0, 10) };
            for (int n = 0; n < positions.Length; n++)
            {
                var zone = new GameObject("Lava_" + (n + 1)); zone.transform.SetParent(root.transform, false);
                zone.transform.position = OnGround(positions[n]);
                var hazard = zone.AddComponent<LavaHazard>(); hazard.Radii = new Vector2(2.2f, 2.8f);
                Ellipse(zone.transform, "MoltenWaste", hazard.Radii, 0, 1, 0.045f, slag);
                Ellipse(zone.transform, "ContainmentRim", hazard.Radii, 1, 1.08f, 0.09f, steel);
                Ellipse(zone.transform, "ScorchedGround", hazard.Radii, 1.08f, 1.2f, 0.018f, ash);
                var lamp = new GameObject("WasteGlow"); lamp.transform.SetParent(zone.transform, false);
                var light = lamp.AddComponent<Light>(); light.type = LightType.Point; light.range = 5f;
                light.intensity = 1.2f; light.color = new Color(1, 0.28f, 0.045f); light.shadows = LightShadows.None;
                light.transform.localPosition = Vector3.up * 0.15f;
            }
        }
        private static void Ellipse(Transform parent, string name, Vector2 radii, float inner, float outer, float offset, Material material)
        {
            const int segments = 64; var v = new Vector3[segments * 2]; var uv = new Vector2[v.Length]; var t = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                for (int r = 0; r < 2; r++)
                {
                    float scale = r == 0 ? inner : outer;
                    Vector3 p = new Vector3(Mathf.Sin(angle) * radii.x, 0, Mathf.Cos(angle) * radii.y) * scale;
                    p.y = Height(parent.position + p) - parent.position.y + offset;
                    v[i * 2 + r] = p; uv[i * 2 + r] = new Vector2(p.x / radii.x, p.z / radii.y) * 0.5f + Vector2.one * 0.5f;
                }
                int k = i * 6, a = i * 2, b = (i + 1) % segments * 2;
                t[k] = a; t[k + 1] = a + 1; t[k + 2] = b; t[k + 3] = b; t[k + 4] = a + 1; t[k + 5] = b + 1;
            }
            var mesh = SaveMesh(name + "_" + parent.name, v, t, uv);
            MeshObject(name, mesh, material, parent, true);
        }

        private static void Lighting()
        {
            var sun = GameObject.Find("ArenaDirectionalLight").GetComponent<Light>();
            sun.color = new Color(0.80f, 0.88f, 1f); sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f; sun.shadowBias = 0.03f; sun.shadowNormalBias = 0.2f;
            sun.transform.rotation = Quaternion.Euler(48, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.17f, 0.21f, 0.28f);
            RenderSettings.ambientEquatorColor = new Color(0.11f, 0.14f, 0.18f);
            RenderSettings.ambientGroundColor = new Color(0.06f, 0.075f, 0.09f);
            RenderSettings.reflectionIntensity = 0.4f;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.11f, 0.14f, 0.18f); RenderSettings.fogDensity = 0.009f;
            var camera = GameObject.Find("Main Camera").GetComponent<Camera>(); camera.backgroundColor = RenderSettings.fogColor;
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            foreach (var feature in renderer.rendererFeatures)
            {
                if (feature.name != "ScreenSpaceAmbientOcclusion") continue;
                var so = new SerializedObject(feature); var settings = so.FindProperty("m_Settings");
                settings.FindPropertyRelative("Intensity").floatValue = 1.1f;
                settings.FindPropertyRelative("Radius").floatValue = 0.6f;
                settings.FindPropertyRelative("DirectLightingStrength").floatValue = 0.3f;
                so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(feature);
            }
        }
        // Editor authoring does not depend on MonoBehaviour OnEnable having run.
        private static float Height(Vector3 p) => ArenaSurface.Profile(p.x, p.z);
        private static Vector3 OnGround(Vector3 p, float offset = 0f) { p.y = Height(p) + offset; return p; }
        private static Vector3 GroundNormal(Vector3 p)
        {
            const float d = 0.1f;
            return new Vector3(Height(p - Vector3.right * d) - Height(p + Vector3.right * d), 2 * d,
                Height(p - Vector3.forward * d) - Height(p + Vector3.forward * d)).normalized;
        }
        private static Material Material(string name, Color color, float metallic = 0f)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Folder + name + ".mat");
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, Folder + name + ".mat"); }
            m.SetColor("_BaseColor", color); m.SetTexture("_BaseMap", Texture2D.whiteTexture);
            m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", 0.15f);
            m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
        }
        private static Mesh SaveMesh(string name, Vector3[] vertices, int[] triangles, Vector2[] uv = null)
        {
            string path = Folder + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear(); mesh.name = name; mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices; mesh.triangles = triangles; if (uv != null) mesh.uv = uv;
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); return mesh;
        }
        private static void MeshObject(string name, Mesh mesh, Material material, Transform parent, bool local = false)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            if (!local) go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        }
        private static void AddBox(List<Vector3> v, List<int> t, Vector3 c, Vector3 s)
        {
            int k = v.Count; Vector3 h = s * 0.5f;
            v.AddRange(new[] { c + new Vector3(-h.x,-h.y,-h.z), c + new Vector3(h.x,-h.y,-h.z), c + new Vector3(h.x,h.y,-h.z), c + new Vector3(-h.x,h.y,-h.z),
                c + new Vector3(-h.x,-h.y,h.z), c + new Vector3(h.x,-h.y,h.z), c + new Vector3(h.x,h.y,h.z), c + new Vector3(-h.x,h.y,h.z) });
            int[] indices = { 0,2,1,0,3,2,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,3,7,6,3,6,2,0,1,5,0,5,4 };
            foreach (int i in indices) t.Add(k + i);
        }
    }
}
