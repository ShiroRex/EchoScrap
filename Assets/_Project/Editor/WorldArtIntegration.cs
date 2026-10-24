using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using System.Linq;
using UnityEngine;

namespace EchoScrap.Editor
{
    /// <summary>Asigna los mapas originales de Blender a materiales URP persistentes.</summary>
    public static class WorldArtIntegration
    {
        public static void Integrate()
        {
            Materials();
            BuildBoss();
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/CombatArena.unity");
            EchoScrapWorldBuilder.Build();
            EchoScrapWorldBuilder.SandyGround();
            DecorateGround();
            var sun = GameObject.Find("ArenaDirectionalLight").GetComponent<Light>();
            sun.color = new Color(1f, 0.86f, 0.66f);
            sun.intensity = 1.55f;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.44f, 0.48f, 0.56f);
            RenderSettings.ambientEquatorColor = new Color(0.35f, 0.29f, 0.22f);
            RenderSettings.ambientGroundColor = new Color(0.19f, 0.14f, 0.10f);
            foreach (var r in GameObject.Find("Arena_120m").GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Wall_") || r.name.EndsWith("Boundary")) r.enabled = false;
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            TerrainCollisionAuthoring.Apply();
            Debug.Log("ECHOSCRAP_INTEGRATION_OK");
        }

        public static void BuildBoss()
        {
            const string modelPath = "Assets/_Project/Art/MataColeccionista/Modelos/MataColeccionista.fbx";
            const string output = "Assets/_Project/Resources/MataColeccionista";
            Directory.CreateDirectory(output);
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = clip.name.Split('|').Last();
                clip.loopTime = clip.name.StartsWith("Walk") || clip.name == "Run";
                clip.lockRootPositionXZ = true;
                clip.lockRootHeightY = true;
                clip.lockRootRotation = true;
                Debug.Log("BOSS_CLIP " + clip.name + " " + clip.firstFrame + ".." + clip.lastFrame);
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            string controllerPath = output + "/MataColeccionista.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var child in machine.states) machine.RemoveState(child.state);
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")))
            {
                var state = machine.AddState(clip.name);
                state.motion = clip;
            }
            var walk = machine.states.First(s => s.state.name == "Walk").state;
            var idle = machine.AddState("Idle"); idle.motion = walk.motion; idle.speed = 0f;
            machine.defaultState = idle;
            EditorUtility.SetDirty(controller);
            var root = new GameObject("MataColeccionista");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            model.transform.SetParent(root.transform, false);
            var renderer = model.GetComponentInChildren<SkinnedMeshRenderer>();
            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            Bounds bounds = renderer.bounds;
            model.transform.localScale *= 1.85f / bounds.size.y;
            // El origen de los GLB está en el suelo. No elevar por el volumen de
            // culling del importador: incluye poses de salto y manos bajo el suelo.
            model.transform.localPosition = Vector3.zero;
            var hand = model.GetComponentsInChildren<Transform>().First(t => t.name.EndsWith("RightHand"));
            var socket = new GameObject("HammerGrip");
            socket.transform.SetParent(hand, false);
            var hammer = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/MataColeccionista/Modelos/Martillo.fbx"));
            hammer.transform.SetParent(socket.transform, false);
            // Normalizar el arma en espacio mundial antes de compensar la escala del esqueleto.
            Bounds weaponBounds = hammer.GetComponentsInChildren<Renderer>()[0].bounds;
            foreach (var r in hammer.GetComponentsInChildren<Renderer>()) weaponBounds.Encapsulate(r.bounds);
            float longest = Mathf.Max(weaponBounds.size.x, weaponBounds.size.y, weaponBounds.size.z);
            hammer.transform.localScale *= 1.05f / longest;
            hammer.transform.localPosition = Vector3.zero;
            socket.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
            PrefabUtility.SaveAsPrefabAsset(root, output + "/MataColeccionista.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log("BOSS_PREFAB_OK");
        }

        private static void DecorateGround()
        {
            const string folder = "Assets/_Project/Art/Mundo3D/Materiales";
            var sand = AssetDatabase.LoadAssetAtPath<Material>(folder + "/SueloVertedero.mat");
            var texture = new Texture2D(512, 512, TextureFormat.RGB24, true);
            for (int y = 0; y < 512; y++)
                for (int x = 0; x < 512; x++)
                {
                    float grain = Mathf.PerlinNoise(x * 0.43f, y * 0.43f);
                    float patches = Mathf.PerlinNoise(x * 0.025f, y * 0.025f);
                    texture.SetPixel(x, y, Color.Lerp(new Color(0.55f, 0.40f, 0.24f), new Color(0.65f, 0.50f, 0.31f), patches * 0.65f + grain * 0.35f));
                }
            texture.Apply();
            File.WriteAllBytes(folder + "/Arena.png", texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(folder + "/Arena.png");
            sand.SetColor("_BaseColor", Color.white);
            sand.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/Arena.png"));
            sand.SetTextureScale("_BaseMap", Vector2.one * 8f);
            EditorUtility.SetDirty(sand);
            var root = new GameObject("Emblema_triangular");
            root.transform.SetParent(GameObject.Find("Mundo3D_EchoScrap").transform, false);
            root.transform.localPosition = new Vector3(-2f, 0.035f, -12f);
            var stone = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            stone.SetColor("_BaseColor", new Color(0.33f, 0.29f, 0.22f));
            stone.SetFloat("_Smoothness", 0.15f);
            string stonePath = folder + "/Emblema.mat";
            var existingStone = AssetDatabase.LoadAssetAtPath<Material>(stonePath);
            if (existingStone == null) AssetDatabase.CreateAsset(stone, stonePath);
            else { UnityEngine.Object.DestroyImmediate(stone); stone = existingStone; }
            var vertices = new[] { new Vector3(0,0,6), new Vector3(5.2f,0,-3), new Vector3(-5.2f,0,-3) };
            var mesh = new Mesh { name = "PlataformaTriangular", vertices = vertices, triangles = new[] { 0,1,2 } };
            mesh.RecalculateNormals();
            string meshPath = folder + "/Emblema.asset";
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existingMesh == null) AssetDatabase.CreateAsset(mesh, meshPath);
            else { UnityEngine.Object.DestroyImmediate(mesh); mesh = existingMesh; }
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial = stone;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f;
                var tooth = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tooth.name = "Diente_" + i;
                UnityEngine.Object.DestroyImmediate(tooth.GetComponent<Collider>());
                tooth.transform.SetParent(root.transform, false);
                tooth.transform.localRotation = Quaternion.Euler(0, angle, 0);
                tooth.transform.localPosition = Quaternion.Euler(0, angle, 0) * Vector3.forward * 2.4f + Vector3.up * 0.08f;
                tooth.transform.localScale = new Vector3(0.65f, 0.12f, 1.15f);
                tooth.GetComponent<Renderer>().sharedMaterial = stone;
            }
            var bronze = AssetDatabase.LoadAssetAtPath<Material>(folder + "/Engranaje.mat");
            if (bronze == null)
            {
                bronze = new Material(stone); bronze.color = new Color(0.22f, 0.16f, 0.09f);
                bronze.SetFloat("_Metallic", 0.6f);
                AssetDatabase.CreateAsset(bronze, folder + "/Engranaje.mat");
            }
            var ringVertices = new Vector3[128]; var ringTriangles = new int[384];
            for (int i = 0; i < 64; i++)
            {
                Vector3 v = new Vector3(Mathf.Sin(i * Mathf.PI / 32), 0, Mathf.Cos(i * Mathf.PI / 32));
                ringVertices[i*2] = v * 2.3f + Vector3.up * 0.16f;
                ringVertices[i*2+1] = v * 1.6f + Vector3.up * 0.16f;
                int n = ((i+1)%64)*2, k=i*6;
                ringTriangles[k]=i*2; ringTriangles[k+1]=n; ringTriangles[k+2]=i*2+1;
                ringTriangles[k+3]=i*2+1; ringTriangles[k+4]=n; ringTriangles[k+5]=n+1;
            }
            var gearMesh = AssetDatabase.LoadAssetAtPath<Mesh>(folder + "/Engranaje.asset");
            if (gearMesh == null)
            {
                gearMesh = new Mesh { vertices = ringVertices, triangles = ringTriangles };
                gearMesh.RecalculateNormals(); AssetDatabase.CreateAsset(gearMesh, folder + "/Engranaje.asset");
            }
            var gear = new GameObject("Aro"); gear.transform.SetParent(root.transform,false);
            gear.AddComponent<MeshFilter>().sharedMesh = gearMesh; gear.AddComponent<MeshRenderer>().sharedMaterial = bronze;
            for (int edge = 0; edge < 3; edge++)
                for (int i = 0; i < 9; i++)
                {
                    Vector3 from=vertices[edge], to=vertices[(edge+1)%3];
                    var block=GameObject.CreatePrimitive(PrimitiveType.Cube); block.name="PiedraBorde";
                    UnityEngine.Object.DestroyImmediate(block.GetComponent<Collider>());
                    block.transform.SetParent(root.transform,false);
                    block.transform.localPosition=Vector3.Lerp(from,to,(i+0.5f)/9f)+Vector3.up*0.1f;
                    block.transform.localRotation=Quaternion.LookRotation(to-from);
                    block.transform.localScale=new Vector3(0.55f,0.22f,1.05f);
                    block.GetComponent<Renderer>().sharedMaterial=stone;
                }
            for (int i=0;i<3;i++)
            {
                var spoke=GameObject.CreatePrimitive(PrimitiveType.Cube); spoke.name="Radio";
                UnityEngine.Object.DestroyImmediate(spoke.GetComponent<Collider>());
                spoke.transform.SetParent(root.transform,false);
                spoke.transform.localRotation=Quaternion.Euler(0,i*120,0);
                spoke.transform.localPosition=spoke.transform.localRotation*Vector3.forward*0.8f+Vector3.up*0.15f;
                spoke.transform.localScale=new Vector3(0.42f,0.18f,1.65f);
                spoke.GetComponent<Renderer>().sharedMaterial=bronze;
            }
            var portalGlow = new GameObject("PortalGlow");
            portalGlow.transform.SetParent(GameObject.Find("Mundo3D_EchoScrap").transform, false);
            portalGlow.transform.position = new Vector3(43f, 3f, -40f);
            var glow = portalGlow.AddComponent<Light>();
            glow.color = new Color(0.50f, 0.12f, 1f); glow.range = 16f; glow.intensity = 7f;
            var vortex = GameObject.CreatePrimitive(PrimitiveType.Quad); vortex.name="PortalVortex";
            UnityEngine.Object.DestroyImmediate(vortex.GetComponent<Collider>());
            vortex.transform.SetParent(portalGlow.transform.parent,false);
            var portal = GameObject.Find("Mundo3D_EchoScrap").transform.Find("Portal/portal_43_-40");
            var filter = portal.GetComponentInChildren<MeshFilter>();
            var collision = filter.gameObject.AddComponent<MeshCollider>();
            collision.sharedMesh = filter.sharedMesh;
            Physics.SyncTransforms();
            Vector3 portalCenter = new Vector3(43,0,-38.5f);
            if (collision.Raycast(new Ray(portalCenter + Vector3.up * 100f, Vector3.down), out RaycastHit hit, 110f))
                portalCenter.y = hit.point.y;
            Debug.Log("PORTAL_SURFACE " + portalCenter);
            UnityEngine.Object.DestroyImmediate(collision);
            vortex.transform.position=portalCenter + Vector3.up * 0.06f;
            vortex.transform.rotation=Quaternion.Euler(90,0,0); vortex.transform.localScale=Vector3.one*10;
            var vortexMaterial=AssetDatabase.LoadAssetAtPath<Material>(folder+"/PortalVortex.mat");
            if(vortexMaterial==null)
            {
                vortexMaterial=new Material(Shader.Find("EchoScrap/PortalVortex"));
                AssetDatabase.CreateAsset(vortexMaterial,folder+"/PortalVortex.mat");
            }
            vortex.GetComponent<Renderer>().sharedMaterial=vortexMaterial;
        }
        [Serializable] private sealed class MaterialList { public MaterialRecord[] materials; }
        [Serializable] private sealed class MaterialRecord
        {
            public string name, @base, normal, emission, metallic;
        }

        public static void Materials()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ApplyMaterials("Assets/_Project/Art/Mundo3D/Texturas", "Assets/_Project/Art/Mundo3D/Modelos");
            ApplyMaterials("Assets/_Project/Art/MataColeccionista/Texturas", "Assets/_Project/Art/MataColeccionista/Modelos");
            AssetDatabase.SaveAssets();
            Debug.Log("ECHOSCRAP_MATERIALS_OK");
        }

        private static void ApplyMaterials(string textureRoot, string modelRoot)
        {
            if (!Directory.Exists(textureRoot)) return;
            foreach (string manifest in Directory.GetFiles(textureRoot, "materials.json", SearchOption.AllDirectories))
            {
                string folder = Path.GetDirectoryName(manifest).Replace('\\', '/');
                string modelPath = modelRoot + "/" + Path.GetFileName(folder) + ".fbx";
                var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (importer == null) throw new InvalidOperationException("Modelo ausente: " + modelPath);
                foreach (var record in JsonUtility.FromJson<MaterialList>(File.ReadAllText(manifest)).materials)
                {
                    string path = folder + "/" + record.name.Replace(':', '_').Replace('/', '_') + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                    {
                        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        AssetDatabase.CreateAsset(material, path);
                    }
                    material.SetColor("_BaseColor", Color.white);
                    material.SetTexture("_BaseMap", LoadTexture(record.@base, false, true));
                    material.SetTexture("_BumpMap", LoadTexture(record.normal, true, false));
                    if (!string.IsNullOrEmpty(record.normal)) material.EnableKeyword("_NORMALMAP");
                    material.SetTexture("_MetallicGlossMap", LoadTexture(record.metallic, false, false));
                    material.SetFloat("_Smoothness", string.IsNullOrEmpty(record.metallic) ? 0.45f : 1f);
                    if (!string.IsNullOrEmpty(record.metallic)) material.EnableKeyword("_METALLICSPECGLOSSMAP");
                    material.SetTexture("_EmissionMap", LoadTexture(record.emission, false, true));
                    material.SetColor("_EmissionColor", Color.white);
                    if (!string.IsNullOrEmpty(record.emission)) material.EnableKeyword("_EMISSION");
                    material.enableInstancing = true;
                    EditorUtility.SetDirty(material);
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), record.name), material);
                }
                importer.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                        if (material == null || material.shader.name != "Universal Render Pipeline/Lit" || material.GetTexture("_BaseMap") == null)
                            throw new InvalidOperationException("Material incompleto en " + modelPath);
                Debug.Log("MATERIAL_VALIDATED " + modelPath);
            }
        }

        private static Texture2D LoadTexture(string path, bool normal, bool srgb)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Textura ausente: " + path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
