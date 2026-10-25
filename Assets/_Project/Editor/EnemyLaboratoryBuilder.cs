using System.Collections.Generic;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Player;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EchoScrap.Editor
{
    public static class EnemyLaboratoryBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/EnemyLaboratory.unity";
        private const string Materials = "Assets/_Project/Materials/EnemyLaboratory";

        [MenuItem("EchoScrap/Laboratorio/Abrir laboratorio de enemigos")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            else Rebuild();
        }

        [MenuItem("EchoScrap/Laboratorio/Crear o reconstruir laboratorio")]
        public static void Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Detén Play Mode.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Materials);
            AssetDatabase.Refresh();
            Material floor = Material("Floor", "Universal Render Pipeline/Lit", new Color(0.11f, 0.125f, 0.15f));
            Material guides = Material("Grid", "Universal Render Pipeline/Unlit", new Color(0.19f, 0.23f, 0.27f));
            Material boundary = Material("Boundary", "Universal Render Pipeline/Unlit", new Color(0.45f, 0.36f, 0.2f));
            Material boxes = Material("TestBox", "Universal Render Pipeline/Lit", new Color(0.32f, 0.36f, 0.4f));
            Material marker = Material("Marker", "Sprites/Default", Color.white);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Transform arena = new GameObject("Laboratorio · 100 x 100 m").transform;
            Collider ground = Cube("Suelo plano", arena, new Vector3(0, -0.5f, 0), new Vector3(100, 1, 100), floor, true);
            Cube("Límite norte", arena, new Vector3(0, 2, 50.5f), new Vector3(102, 4, 1), null, true);
            Cube("Límite sur", arena, new Vector3(0, 2, -50.5f), new Vector3(102, 4, 1), null, true);
            Cube("Límite este", arena, new Vector3(50.5f, 2, 0), new Vector3(1, 4, 100), null, true);
            Cube("Límite oeste", arena, new Vector3(-50.5f, 2, 0), new Vector3(1, 4, 100), null, true);
            for (int i = -9; i <= 9; i++)
            {
                float width = i == 0 ? 0.055f : 0.025f;
                Cube("Grid X " + i, arena, new Vector3(i * 5, 0.006f, 0), new Vector3(width, 0.006f, 99), guides, false);
                Cube("Grid Z " + i, arena, new Vector3(0, 0.006f, i * 5), new Vector3(99, 0.006f, width), guides, false);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                Cube("Borde X", arena, new Vector3(side * 49.8f, 0.012f, 0), new Vector3(0.08f, 0.01f, 99.6f), boundary, false);
                Cube("Borde Z", arena, new Vector3(0, 0.012f, side * 49.8f), new Vector3(99.6f, 0.01f, 0.08f), boundary, false);
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CombatArenaAuthoring.PlayerPrefabPath);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            player.name = "Player";
            player.transform.position = Vector3.up;
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 60;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.065f);
            camera.gameObject.AddComponent<AudioListener>();
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var brain = camera.gameObject.AddComponent<CinemachineBrain>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            var rig = new GameObject("Laboratory Follow Camera").AddComponent<CinemachineCamera>();
            rig.Follow = player.transform;
            rig.Lens.FieldOfView = 60;
            rig.Lens.NearClipPlane = 0.1f;
            rig.Lens.FarClipPlane = 200;
            rig.Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            rig.transform.rotation = Quaternion.Euler(50, 0, 0);
            var follow = rig.gameObject.AddComponent<CinemachineFollow>();
            follow.FollowOffset = rig.transform.rotation * Vector3.back * 23f;
            follow.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            follow.TrackerSettings.PositionDamping = new Vector3(0.15f, 0, 0.15f);
            rig.transform.position = player.transform.position + follow.FollowOffset;
            camera.transform.SetPositionAndRotation(rig.transform.position, rig.transform.rotation);
            var light = new GameObject("Laboratory Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.55f;
            light.color = new Color(0.85f, 0.89f, 1f);
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48, -25, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.24f, 0.25f, 0.3f);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            var lab = new GameObject("Enemy Laboratory").AddComponent<EnemyLaboratory>();
            lab.gameObject.AddComponent<EnemyLaboratoryPanel>();
            var serialized = new SerializedObject(lab);
            serialized.FindProperty("playerMotor").objectReferenceValue = player.GetComponent<PlayerMotor>();
            serialized.FindProperty("gameplayCamera").objectReferenceValue = camera;
            serialized.FindProperty("arenaFloor").objectReferenceValue = ground;
            serialized.FindProperty("obstacleMaterial").objectReferenceValue = boxes;
            serialized.FindProperty("markerMaterial").objectReferenceValue = marker;
            var retained = new List<Material> { floor, guides, boundary, boxes, marker };
            foreach (string path in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project/Materials/CombatArena" }))
                retained.Add(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(path)));
            var references = serialized.FindProperty("shaderReferences");
            references.arraySize = retained.Count;
            for (int i = 0; i < retained.Count; i++) references.GetArrayElementAtIndex(i).objectReferenceValue = retained[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(entry => entry.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] EnemyLaboratory listo: arena plana, seis enemigos, controles de pruebas y aislamiento de la partida normal.");
        }

        private static Collider Cube(string name, Transform parent, Vector3 position, Vector3 size, Material material, bool collider)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.position = position;
            cube.transform.localScale = size;
            if (!collider) Object.DestroyImmediate(cube.GetComponent<Collider>());
            var renderer = cube.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material; else renderer.enabled = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return cube.GetComponent<Collider>();
        }
        private static Material Material(string name, string shader, Color color)
        {
            string path = Materials + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find(shader)) { name = "Laboratory " + name };
            material.color = color;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.05f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
