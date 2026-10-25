using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoScrap.Editor
{
    /// <summary>
    /// Montaje decorativo del vertedero 200x200. No modifica combate, enemigos ni jugador.
    /// Los FBX se generan con Tools/EchoScrap3D/ConvertirMundo.ps1.
    /// </summary>
    public static class EchoScrapWorldBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/CombatArena.unity";
        const string ModelPath = "Assets/_Project/Art/Mundo3D/Modelos/";
        const string MaterialPath = "Assets/_Project/Art/Mundo3D/Materiales/SueloVertedero.mat";
        const string RootName = "Mundo3D_EchoScrap";
        const string MenuBase = "EchoScrap/Arte 3D/";

        struct Prop
        {
            public string name;
            public float x, z, width, yaw;
            public bool solid;
            public string section;
            public Prop(string name, float x, float z, float width, float yaw = 0f, bool solid = false, string section = "Atrezzo")
            {
                this.name = name; this.x = x; this.z = z; this.width = width;
                this.yaw = yaw; this.solid = solid; this.section = section;
            }
        }

        // Plano fijo trazado desde la referencia: oeste/desguace, norte/depósitos,
        // este/tuberías y portal sureste. El eje central queda transitable.
        static readonly Prop[] Layout = new Prop[]
        {
            new Prop("casa", -48, 44, 14, 28, true, "Oeste"),
            new Prop("carro", -39, 45, 7, -20, true, "Oeste"),
            new Prop("carro", -47, 34, 8, 85, true, "Oeste"),
            new Prop("carro", -39, 32, 7, 15, true, "Oeste"),
            new Prop("casa", -51, 20, 13, 90, true, "Oeste"),
            new Prop("carro", -43, 17, 7, 35, true, "Oeste"),
            new Prop("contenedor", -53, 4, 12, 91, true, "Oeste"),
            new Prop("chatarra_alta", -46, -7, 13, 72, true, "Oeste"),
            new Prop("casa", -52, -22, 14, 95, true, "Oeste"),
            new Prop("carro", -42, -26, 8, 40, true, "Oeste"),
            new Prop("carro", -47, -36, 7, 15, true, "Oeste"),
            new Prop("chatarra_alta", -49, -49, 17, 50, true, "Oeste"),
            new Prop("ruina_almacen", -23, 53, 20, 0, true, "Norte"),
            new Prop("deposito", -40, 55, 12, -10, true, "Norte"),
            new Prop("deposito", -30, 46, 9, 20, true, "Norte"),
            new Prop("contenedor", -10, 53, 11, 0, true, "Norte"),
            new Prop("chatarra_alta", 5, 52, 18, -15, true, "Norte"),
            new Prop("chatarra_alta", 22, 49, 19, 35, true, "Norte"),
            new Prop("chatarra_alta", 40, 49, 18, 65, true, "Norte"),
            new Prop("tubo_roto", 7, 43, 9, 70, false, "Norte"),
            new Prop("tubo_codo", 20, 42, 9, 40, false, "Norte"),
            new Prop("vigas", 31, 42, 13, 75, false, "Norte"),
            new Prop("tubo_roto", 42, 42, 8, -25, false, "Norte"),
            new Prop("ruina_almacen", 51, 31, 18, 90, true, "Este"),
            new Prop("casa", 51, 12, 15, -90, true, "Este"),
            new Prop("marco_roto", 44, 23, 10, 85, true, "Este"),
            new Prop("tubo_codo", 42, 35, 7, 100, false, "Este"),
            new Prop("chatarra_baja", 47, -3, 12, 40, true, "Este"),
            new Prop("ruina_almacen", 51, -17, 15, 88, true, "Este"),
            new Prop("portal", 43, -40, 19, -25, false, "Portal"),
            new Prop("muro_derrumbe", 34, -49, 12, -20, true, "Portal"),
            new Prop("muro_esquina", 54, -31, 10, -30, true, "Portal"),
            new Prop("vigas", 53, -49, 10, -35, false, "Portal"),
            new Prop("chatarra_alta", -30, -54, 15, 25, true, "Sur"),
            new Prop("contenedor", -13, -55, 11, -3, true, "Sur"),
            new Prop("chatarra_baja", 2, -54, 13, 0, true, "Sur"),
            new Prop("muro_derrumbe", 18, -55, 12, 0, true, "Sur"),
            new Prop("arbol_seco", -24, 17, 10, 30, false, "Interior"),
            new Prop("arbol_seco", 0, 30, 7, 15, false, "Interior"),
            new Prop("arbol_seco", 20, 22, 9, -20, false, "Interior"),
            new Prop("tocon_bajo", 32, 8, 5, 35, false, "Interior"),
            new Prop("arbol_seco", 23, -18, 10, 10, false, "Interior"),
            new Prop("arbol_seco", -8, -37, 11, -20, false, "Interior"),
            new Prop("tocon_bajo", -13, 43, 5, 5, false, "Interior"),
            new Prop("roca_baja", -33, 5, 3, 15, false, "Interior"),
            new Prop("roca_partida", 13, 16, 3, 20, false, "Interior"),
            new Prop("roca_baja", -18, -23, 2, 5, false, "Interior"),
            new Prop("roca_baja", 13, -42, 3, 50, false, "Interior"),
            new Prop("muro_recto", -58, 43, 12, 90, true, "Bardas"),
            new Prop("muro_recto", -58, 29, 12, 90, true, "Bardas"),
            new Prop("muro_derrumbe", -58, 12, 15, 90, true, "Bardas"),
            new Prop("muro_recto", -58, -7, 12, 90, true, "Bardas"),
            new Prop("muro_derrumbe", -58, -27, 15, 90, true, "Bardas"),
            new Prop("muro_recto", 58, 42, 12, 90, true, "Bardas"),
            new Prop("muro_recto", 58, 23, 12, 90, true, "Bardas"),
            new Prop("muro_derrumbe", 58, 3, 14, 90, true, "Bardas"),
            new Prop("muro_recto", 58, -17, 12, 90, true, "Bardas"),
            new Prop("muro_recto", -39, 59, 14, 0, true, "Bardas"),
            new Prop("muro_derrumbe", -20, 59, 16, 0, true, "Bardas"),
            new Prop("muro_recto", 0, 59, 14, 0, true, "Bardas"),
            new Prop("muro_derrumbe", 20, 59, 16, 0, true, "Bardas"),
            new Prop("muro_recto", 40, 59, 14, 0, true, "Bardas")
        };

        // Acumulaciones dibujadas junto a construcciones y árboles; sin Random ni dispersión global.
        static readonly Vector2[] DetailSites = {
            new Vector2(-39,39), new Vector2(-42,25), new Vector2(-44,10), new Vector2(-44,-17),
            new Vector2(-38,-34), new Vector2(-35,-47), new Vector2(-18,45), new Vector2(-2,44),
            new Vector2(30,36), new Vector2(40,15), new Vector2(39,-8), new Vector2(32,-29),
            new Vector2(-25,17), new Vector2(20,22), new Vector2(23,-18), new Vector2(-8,-37),
            new Vector2(-18,29), new Vector2(10,31), new Vector2(-22,-10), new Vector2(18,-7),
            new Vector2(-8,-18), new Vector2(12,-32), new Vector2(28,12), new Vector2(-28,30),
            new Vector2(4,12), new Vector2(30,-36)
        };
        [MenuItem(MenuBase + "1 - Crear mapa de vertedero")]
        public static void Build()
        {
            Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                EditorUtility.DisplayDialog("EchoScrap - escena equivocada",
                    "Abre Assets/_Project/Scenes/CombatArena.unity y sal de Play antes de importar.", "Entendido");
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Detén Play", "Debes editar la escena fuera de Play.", "Entendido");
                return;
            }
            GameObject previous = GameObject.Find(RootName);
            if (previous != null && !Application.isBatchMode && !EditorUtility.DisplayDialog("Reconstruir mapa 3D",
                "Se reemplazará únicamente el grupo Mundo3D_EchoScrap. No se tocarán Player, CombatGame ni el resto de la escena.",
                "Reconstruir", "Cancelar")) return;

            if (previous != null) Undo.DestroyObjectImmediate(previous);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Crear mundo 3D EchoScrap");
            var parent = GameObject.Find("Arena_120m");
            if (parent != null) Undo.SetTransformParent(root.transform, parent.transform, "Organizar mundo 3D");
            var sections = new Dictionary<string, Transform>();
            var absent = new HashSet<string>();
            int placed = 0;
            foreach (var p in Layout)
            {
                string path = ModelPath + p.name + ".fbx";
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) { absent.Add(p.name + ".fbx"); continue; }
                if (!sections.TryGetValue(p.section, out Transform section))
                {
                    var container = new GameObject(p.section);
                    Undo.RegisterCreatedObjectUndo(container, "Agrupar mundo 3D");
                    Undo.SetTransformParent(container.transform, root.transform, "Agrupar mundo 3D");
                    section = container.transform;
                    sections.Add(p.section, section);
                }
                if (Place(asset, p, section, scene)) placed++;
            }
            var details = new GameObject("Acumulaciones_dibujadas");
            details.transform.SetParent(root.transform, false);
            foreach (var site in DetailSites)
            {
                Place(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath + "escombros_concreto.fbx"), new Prop("escombros_concreto", site.x, site.y, 3.8f, 18), details.transform, scene);
                Place(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath + "llanta.fbx"), new Prop("llanta", site.x + 2, site.y - 1, 1.4f, 36), details.transform, scene);
                Place(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath + "cajas.fbx"), new Prop("cajas", site.x - 2, site.y + 1, 2.1f, -12), details.transform, scene);
                Place(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath + "bolsa_basura.fbx"), new Prop("bolsa_basura", site.x + 1, site.y + 2, 1.3f, 62), details.transform, scene);
                Place(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath + "tablas.fbx"), new Prop("tablas", site.x - 1, site.y - 2, 2.3f, 28), details.transform, scene);
            }
            var bands = new[] { new Vector4(-53,48,-53,-48), new Vector4(53,45,53,-23),
                new Vector4(-45,54,45,54), new Vector4(-41,-54,24,-54) };
            foreach (var band in bands)
                for (int i = 0; i < 12; i++)
                {
                    float t = i / 11f;
                    string asset = i % 3 == 0 ? "chatarra_baja" : i % 3 == 1 ? "escombros_de_ladrillo" : "cajas";
                    Place(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath + asset + ".fbx"),
                        new Prop(asset, Mathf.Lerp(band.x,band.z,t), Mathf.Lerp(band.y,band.w,t), i % 3 == 0 ? 8f : 4.5f, i * 37f), details.transform, scene);
                }
            // Costuras dibujadas de escombro que delimitan los caminos; sin dispersión aleatoria.
            var seams = new[] { new Vector4(-34,42,-35,-45), new Vector4(35,36,32,-27),
                new Vector4(-32,41,32,39), new Vector4(-30,-47,22,-49) };
            foreach (var seam in seams)
                for (int i = 0; i < 20; i++)
                {
                    float t = i / 19f;
                    float x = Mathf.Lerp(seam.x,seam.z,t) + Mathf.Sin(i * 1.7f) * 1.2f;
                    float z = Mathf.Lerp(seam.y,seam.w,t) + Mathf.Cos(i * 1.3f) * 1.1f;
                    string asset = i % 3 == 0 ? "roca_baja" : i % 3 == 1 ? "escombros_concreto" : "llanta";
                    Place(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath + asset + ".fbx"), new Prop(asset,x,z,1.2f + (i % 3) * 0.45f,i * 28f), details.transform, scene);
                }
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            SceneView.RepaintAll();
            string msg = "Objetos colocados: " + placed + " / " + Layout.Length + ".\n";
            if (absent.Count > 0) msg += "Faltan FBX (se omiten, no se inventan):\n- " + string.Join("\n- ", absent) + "\n";
            msg += "\nRevisa la pestaña Scene y guarda con Ctrl+S solamente si te gusta el resultado.";
            Debug.Log("[EchoScrap Mundo 3D] " + msg);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("Mapa 3D armado", msg, "Aceptar");
        }

        private static bool Place(GameObject prefab, Prop p, Transform section, Scene scene)
        {
            var anchor = new GameObject(p.name + "_" + p.x.ToString("0") + "_" + p.z.ToString("0"));
            Undo.RegisterCreatedObjectUndo(anchor, "Añadir objeto del mundo");
            Undo.SetTransformParent(anchor.transform, section, "Organizar objeto 3D");
            anchor.transform.position = new Vector3(p.x * 1.6f, 0f, p.z * 1.6f);
            anchor.transform.rotation = Quaternion.Euler(0f, p.yaw, 0f);
            var model = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (model == null) { Undo.DestroyObjectImmediate(anchor); return false; }
            Undo.SetTransformParent(model.transform, anchor.transform, "Colocar modelo 3D");
            model.transform.localPosition = Vector3.zero;
            // Conservar la corrección de ejes del FBX. Poner identity acostaba los modelos.
            model.transform.localRotation = prefab.transform.localRotation;
            if (!TryBounds(anchor, out Bounds bounds))
            {
                Undo.DestroyObjectImmediate(anchor);
                return false;
            }
            float widest = Mathf.Max(bounds.size.x, bounds.size.z);
            if (widest < 0.0001f) { Undo.DestroyObjectImmediate(anchor); return false; }
            float width=p.width;
            if(p.name=="arbol_seco") width=Mathf.Min(width,6f);
            if(p.name=="tocon_bajo") width=Mathf.Min(width,3f);
            if(p.name.StartsWith("roca")) width=Mathf.Min(width,3f);
            if(p.name=="bolsa_basura") width=Mathf.Min(width,1.6f);
            if(p.name=="carro") width=Mathf.Min(width,4.5f);
            if(p.name=="lavadora" || p.name=="refrigerador") width=Mathf.Min(width,1.5f);
            if(p.name=="cajas") width=Mathf.Min(width,2.5f);
            if(p.name=="bolsa_basura" || p.name=="llanta" || p.name=="tablas" || p.name=="cajas" || p.name.StartsWith("escombros")) width*=1.35f;
            anchor.transform.localScale = Vector3.one * Mathf.Clamp(width / widest, 0.005f, 500f);
            if (TryBounds(anchor, out bounds))
                anchor.transform.position += Vector3.up * (0f - bounds.min.y);

            foreach (var filter in anchor.GetComponentsInChildren<MeshFilter>())
            {
                var collider = Undo.AddComponent<MeshCollider>(filter.gameObject);
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
            }
            return true;
        }

        private static bool TryBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (!found) { bounds = r.bounds; found = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return found;
        }

        private static bool TryLocalBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            if (!TryBounds(root, out Bounds world)) return false;
            Vector3 min = world.min, max = world.max;
            bool first = true;
            for (int x = 0; x < 2; ++x)
                for (int y = 0; y < 2; ++y)
                    for (int z = 0; z < 2; ++z)
                    {
                        Vector3 corner = new Vector3(x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                        Vector3 local = root.transform.InverseTransformPoint(corner);
                        if (first) { bounds = new Bounds(local, Vector3.zero); first = false; }
                        else bounds.Encapsulate(local);
                    }
            return true;
        }

        [MenuItem(MenuBase + "2 - Suelo arena y ocultar guías")]
        public static void SandyGround()
        {
            Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("EchoScrap", "Abre CombatArena.unity fuera de Play.", "Entendido");
                return;
            }
            var floor = GameObject.Find("Arena_120m/Floor");
            if (floor == null) { EditorUtility.DisplayDialog("EchoScrap", "No se encontró Arena_120m/Floor.", "Aceptar"); return; }
            var render = floor.GetComponent<Renderer>();
            if (render == null) return;
            EnsureFolder("Assets/_Project/Art/Mundo3D/Materiales");
            Material sand = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (sand == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) { Debug.LogError("No se encontró el shader URP/Lit."); return; }
                sand = new Material(shader) { name = "SueloVertedero" };
                sand.SetColor("_BaseColor", new Color(0.44f, 0.32f, 0.21f));
                sand.SetFloat("_Smoothness", 0.03f);
                AssetDatabase.CreateAsset(sand, MaterialPath);
            }
            Undo.RecordObject(render, "Cambiar suelo por arena");
            render.sharedMaterial = sand;
            var guides = GameObject.Find("Arena_120m/FloorGuides_10m");
            if (guides != null) { Undo.RecordObject(guides, "Ocultar guías de prototipo"); guides.SetActive(false); }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[EchoScrap Mundo 3D] Suelo arena aplicado. Las colisiones originales se conservan.");
        }

        private static void EnsureFolder(string path)
        {
            string[] folders = path.Split('/');
            string current = folders[0];
            for (int i = 1; i < folders.Length; i++)
            {
                string next = current + "/" + folders[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, folders[i]);
                current = next;
            }
        }
    }
}

