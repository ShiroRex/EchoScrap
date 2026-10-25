using System.Collections.Generic;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Editor
{
    /// <summary>Apply the Blender art kit without rebuilding the user's layout.</summary>
    public static class YardArtAuthoring
    {
        private const string Kit = "Assets/_Project/Art/Mundo3D/YardKit/";
        private static readonly Dictionary<Material, Material> looks = new Dictionary<Material, Material>();

        [MenuItem("EchoScrap/Mapa/Aplicar arte de Blender")]
        public static void Apply() => ApplyScene("Assets/_Project/Scenes/CombatArena.unity");

        public static void ApplyScene(string scenePath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath);
            ImportTextures(); looks.Clear();
            Material tree = KitMaterial("DeadTree"), a = KitMaterial("ScrapPocket0"), b = KitMaterial("ScrapPocket1");
            Transform world = GameObject.Find("Mundo3D_EchoScrap").transform;
            var anchors = new List<Transform>(); foreach (Transform child in world) anchors.Add(child);
            int trees = 0, pockets = 0;
            foreach (Transform anchor in anchors)
            {
                if (anchor.name.StartsWith("arbol_seco_"))
                {
                    DisableOriginal(anchor);
                    Transform model = Install(anchor, "DeadTree", tree, 4.3f, true);
                    Ground(model); trees++;
                    var collider = anchor.GetComponent<CapsuleCollider>();
                    collider.radius = .52f / anchor.lossyScale.x;
                    collider.height = 2.4f / anchor.lossyScale.y;
                    collider.center = anchor.InverseTransformPoint(new Vector3(anchor.position.x,
                        ArenaSurface.Profile(anchor.position.x, anchor.position.z) + 1.15f, anchor.position.z));
                }
                if (!anchor.name.StartsWith("escombros_")) continue;
                DisableOriginal(anchor);
                Transform pocket = Install(anchor, "ScrapPocket" + (pockets % 2), pockets % 2 == 0 ? a : b, 2.8f, false);
                Ground(pocket); pockets++;
                foreach (Transform other in anchors)
                {
                    bool detail = other.name.StartsWith("llanta_") || other.name.StartsWith("tablas_")
                        || other.name.StartsWith("cajas_") || other.name.StartsWith("bolsa_basura_");
                    if (detail && Vector2.Distance(new Vector2(anchor.position.x, anchor.position.z),
                        new Vector2(other.position.x, other.position.z)) < 3f) DisableOriginal(other);
                }
            }
            foreach (var renderer in world.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || renderer.name == "PortalVortex" || renderer.name.Contains("Refined")) continue;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material original = materials[i]; string path = AssetDatabase.GetAssetPath(original);
                    if (!path.Contains("/Mundo3D/Texturas/")) continue;
                    if (!looks.TryGetValue(original, out Material look))
                    {
                        string guid = AssetDatabase.AssetPathToGUID(path);
                        string target = Kit + "Look_" + guid + ".mat";
                        look = AssetDatabase.LoadAssetAtPath<Material>(target);
                        if (look == null) { look = new Material(original); AssetDatabase.CreateAsset(look, target); }
                        look.CopyPropertiesFromMaterial(original); look.name = "Dusty " + Path.GetFileName(Path.GetDirectoryName(path));
                        look.SetColor("_BaseColor", new Color(.80f, .78f, .74f));
                        look.SetFloat("_Smoothness", .32f); look.SetFloat("_BumpScale", .55f);
                        EditorUtility.SetDirty(look); looks.Add(original, look);
                    }
                    materials[i] = look;
                }
                renderer.sharedMaterials = materials;
            }
            Material soil = GameObject.Find("Arena_120m/Floor").GetComponent<Renderer>().sharedMaterial;
            soil.shader = Shader.Find("Universal Render Pipeline/Lit");
            soil.SetTexture("_BaseMap", Texture("CompactedSoil_Color"));
            soil.SetTexture("_BumpMap", Texture("CompactedSoil_Normal"));
            soil.SetTextureScale("_BaseMap", Vector2.one * 3f); soil.SetTextureScale("_BumpMap", Vector2.one * 3f);
            soil.EnableKeyword("_NORMALMAP"); soil.SetFloat("_BumpScale", .45f); soil.SetFloat("_Smoothness", .08f);
            soil.SetColor("_BaseColor", Color.white); EditorUtility.SetDirty(soil);
            GroundMarks(world); Lighting(); RebindOcclusion(world);
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            Debug.Log($"YARD_ART_OK trees={trees} composedPockets={pockets} layout=preserved playerScale=1.1");
        }

        private static void DisableOriginal(Transform anchor)
        {
            // Only the first native model; manually added siblings remain intact.
            if (anchor.childCount == 0) return;
            foreach (var renderer in anchor.GetChild(0).GetComponentsInChildren<MeshRenderer>()) renderer.enabled = false;
        }

        private static Transform Install(Transform anchor, string asset, Material material, float size, bool height)
        {
            Transform old = anchor.Find("Refined " + asset); if (old != null) Object.DestroyImmediate(old.gameObject);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Kit + asset + ".fbx"));
            Transform t = instance.transform; t.SetParent(anchor, false); t.name = "Refined " + asset;
            var renderers = instance.GetComponentsInChildren<MeshRenderer>();
            Bounds bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            t.localScale *= size / (height ? bounds.size.y : Mathf.Max(bounds.size.x, bounds.size.z));
            foreach (var renderer in renderers) renderer.sharedMaterial = material;
            return t;
        }

        private static void Ground(Transform model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var gaps = new List<float>();
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 p = filter.transform.TransformPoint(vertex);
                    if (p.y < bounds.min.y + .12f) gaps.Add(p.y - ArenaSurface.Profile(p.x, p.z));
                }
            gaps.Sort(); if (gaps.Count != 0) model.position -= Vector3.up * (gaps[gaps.Count / 3] + .025f);
        }

        private static Texture2D Texture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Kit + name + ".png");
        private static void GroundMarks(Transform world)
        {
            var old = GameObject.Find("Yard Ground Marks"); if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("Yard Ground Marks"); root.transform.SetParent(GameObject.Find("Arena_120m").transform, false);
            var marks = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "WornCirculation.fbx"));
            marks.transform.SetParent(root.transform, false);
            Material tracks = MarkMaterial("TireTracks", .32f, true);
            foreach (var renderer in marks.GetComponentsInChildren<MeshRenderer>())
            { renderer.sharedMaterial = tracks; renderer.shadowCastingMode = ShadowCastingMode.Off; }
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            foreach (Transform anchor in world)
            {
                if (anchor.name.StartsWith("Portal") || anchor.name.StartsWith("Emblema")) continue;
                var renderers = anchor.GetComponentsInChildren<MeshRenderer>(); bool found = false; Bounds b = default;
                foreach (var renderer in renderers)
                    if (renderer.enabled) { if (found) b.Encapsulate(renderer.bounds); else { b = renderer.bounds; found = true; } }
                if (!found) continue;
                const int steps = 8; int first = v.Count;
                Vector2 size = new Vector2(Mathf.Clamp(b.size.x + 1.2f, 1.5f, 14f), Mathf.Clamp(b.size.z + 1.2f, 1.5f, 14f));
                for (int z = 0; z <= steps; z++) for (int x = 0; x <= steps; x++)
                {
                    Vector2 tex = new Vector2(x, z) / steps;
                    Vector3 p = b.center + new Vector3((tex.x-.5f)*size.x, 0, (tex.y-.5f)*size.y);
                    p.y = ArenaSurface.Profile(p.x,p.z) + .018f; v.Add(p); uv.Add(tex);
                    if (x == steps || z == steps) continue;
                    int a = first + z * (steps+1) + x, c = a + steps + 1;
                    triangles.Add(a);triangles.Add(c);triangles.Add(a+1);triangles.Add(a+1);triangles.Add(c);triangles.Add(c+1);
                }
            }
            string path = Kit + "ContactStains.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh,path); }
            mesh.Clear();mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            var stains = new GameObject("Settled dirt"); stains.transform.SetParent(root.transform,false);
            stains.AddComponent<MeshFilter>().sharedMesh = mesh;
            var surface = stains.AddComponent<MeshRenderer>(); surface.sharedMaterial = MarkMaterial("ContactStains", .40f, false);
            surface.shadowCastingMode = ShadowCastingMode.Off;
        }
        private static Material MarkMaterial(string name, float alpha, bool tracks)
        {
            string path = Kit + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("EchoScrap/YardGroundMarks")); AssetDatabase.CreateAsset(material,path); }
            material.SetColor("_BaseColor",new Color(.025f,.019f,.013f,alpha));material.SetFloat("_Tracks",tracks?1:0);
            EditorUtility.SetDirty(material);return material;
        }
        private static void ImportTextures()
        {
            AssetDatabase.Refresh();
            foreach (string file in Directory.GetFiles(Kit, "*.png"))
            {
                string path = file.Replace('\\', '/');
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = path.Contains("_Normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !path.Contains("_Normal"); importer.maxTextureSize = 1024;
                importer.mipmapEnabled = true; importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = path.Contains("CompactedSoil") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
            }
        }
        private static Material KitMaterial(string asset)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(Kit + asset + ".mat");
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, Kit + asset + ".mat"); }
            material.SetTexture("_BaseMap", Texture(asset + "_Color"));
            material.SetTexture("_BumpMap", Texture(asset + "_Normal")); material.EnableKeyword("_NORMALMAP");
            material.SetColor("_BaseColor", Color.white); material.SetFloat("_BumpScale", .7f);
            material.SetFloat("_Smoothness", .12f); material.enableInstancing = true; EditorUtility.SetDirty(material);
            return material;
        }
        private static void Lighting()
        {
            var sun = GameObject.Find("ArenaDirectionalLight").GetComponent<Light>();
            sun.color = new Color(.94f, .87f, .74f); sun.intensity = 1.25f;
            sun.transform.rotation = Quaternion.Euler(43, -35, 0);
            sun.shadowStrength = .85f; sun.shadowBias = .025f; sun.shadowNormalBias = .12f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.18f,.21f,.26f);
            RenderSettings.ambientEquatorColor = new Color(.13f,.14f,.16f);
            RenderSettings.ambientGroundColor = new Color(.07f,.06f,.05f);
            RenderSettings.fogColor = new Color(.11f,.105f,.10f); RenderSettings.fogDensity = .007f;
            GameObject.Find("Main Camera").GetComponent<Camera>().backgroundColor = RenderSettings.fogColor;
        }
        private static void RebindOcclusion(Transform world)
        {
            var obstacles = new List<Collider>(world.GetComponentsInChildren<Collider>());
            foreach (string side in new[] {"North","South","East","West"})
                obstacles.Add(GameObject.Find("Arena_120m/Wall_" + side).GetComponent<Collider>());
            var materials = new HashSet<Material>(); var variants = new List<Material>();
            foreach (var collider in obstacles)
            {
                Transform root = collider.name == "SolidFootprint" ? collider.transform.parent : collider.transform;
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
                    foreach (var material in renderer.sharedMaterials) materials.Add(material);
            }
            foreach (var original in materials)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out string guid, out long id);
                string path = Kit + "Fade_" + guid + "_" + id + ".mat";
                Material fade = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (fade == null) { fade = new Material(original); AssetDatabase.CreateAsset(fade, path); }
                fade.CopyPropertiesFromMaterial(original); ArenaOcclusion.ConfigureTransparent(fade);
                EditorUtility.SetDirty(fade); variants.Add(fade);
            }
            world.GetComponent<ArenaOcclusion>().Bind(GameObject.Find("Main Camera").GetComponent<Camera>(),
                GameObject.Find("Player").GetComponent<CollectorSpriteView>().CharacterRenderer, obstacles.ToArray(), variants.ToArray());
        }
    }
}
