using System.Collections.Generic;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Player;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EchoScrap.Editor
{
    public static class CombatArenaBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/CombatArena.unity";
        public const string WaveSettingsPath = "Assets/_Project/Settings/CombatWaves_Default.asset";
        private const string MaterialDirectory = "Assets/_Project/Materials/CombatArena";

        [MenuItem("EchoScrap/Combat/Rebuild Playable Arena")]
        public static void Rebuild()
        {
            Directory.CreateDirectory("Assets/_Project/Scenes");
            Directory.CreateDirectory(MaterialDirectory);
            Directory.CreateDirectory("Assets/_Project/Settings");
            AssetDatabase.Refresh();
            CombatWaveSettings waves = AssetDatabase.LoadAssetAtPath<CombatWaveSettings>(WaveSettingsPath);
            if (waves == null)
            {
                waves = ScriptableObject.CreateInstance<CombatWaveSettings>();
                AssetDatabase.CreateAsset(waves, WaveSettingsPath);
            }
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LocomotionSandboxBuilder.PrefabPath);
            if (playerPrefab == null) throw new BuildFailedException("The player locomotion prefab is missing.");
            var retainedMaterials = new List<Material>(8);
            Material floor = MaterialAsset("Floor", "Universal Render Pipeline/Lit", new Color(0.075f, 0.095f, 0.105f));
            Material walls = MaterialAsset("Walls", "Universal Render Pipeline/Lit", new Color(0.25f, 0.27f, 0.285f));
            Material guides = MaterialAsset("DistanceGuides", "Universal Render Pipeline/Unlit", new Color(0.13f, 0.155f, 0.16f));
            Material boundary = MaterialAsset("BoundaryAccent", "Universal Render Pipeline/Unlit", new Color(0.37f, 0.285f, 0.14f));
            retainedMaterials.Add(floor); retainedMaterials.Add(walls); retainedMaterials.Add(guides); retainedMaterials.Add(boundary);
            retainedMaterials.Add(MaterialAsset("EffectsUnlitTransparent", "Universal Render Pipeline/Unlit", Color.white, true));
            retainedMaterials.Add(MaterialAsset("EffectsLitTransparent", "Universal Render Pipeline/Lit", Color.white, true));
            retainedMaterials.Add(MaterialAsset("FlashlightLens", "EchoScrap/Flashlight Rescue Lens", Color.white, true));
            retainedMaterials.Add(MaterialAsset("Hud", "UI/Default", Color.white));
            retainedMaterials.Add(MaterialAsset("OilVolume", "EchoScrap/Oil Lantern Volume", Color.white, true));
            retainedMaterials.Add(MaterialAsset("PrototypeParticles", "Sprites/Default", Color.white, true));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Transform arena = new GameObject("Arena_120m").transform;
            Collider ground = Cube("Floor", arena, new Vector3(0f, -0.5f, 0f), new Vector3(120f, 1f, 120f), floor);
            Cube("Wall_North", arena, new Vector3(0f, 1.5f, 60.5f), new Vector3(122f, 3f, 1f), walls);
            Cube("Wall_South", arena, new Vector3(0f, 1.5f, -60.5f), new Vector3(122f, 3f, 1f), walls);
            Cube("Wall_East", arena, new Vector3(60.5f, 1.5f, 0f), new Vector3(1f, 3f, 120f), walls);
            Cube("Wall_West", arena, new Vector3(-60.5f, 1.5f, 0f), new Vector3(1f, 3f, 120f), walls);
            Transform guideRoot = new GameObject("FloorGuides_10m").transform;
            guideRoot.SetParent(arena, false);
            for (int i = -5; i <= 5; i++)
            {
                Cube($"GuideX_{i}", guideRoot, new Vector3(i * 10f, 0.004f, 0f), new Vector3(0.025f, 0.006f, 118f), guides, false);
                Cube($"GuideZ_{i}", guideRoot, new Vector3(0f, 0.004f, i * 10f), new Vector3(118f, 0.006f, 0.025f), guides, false);
            }
            Cube("NorthBoundary", guideRoot, new Vector3(0f, 0.012f, 59.2f), new Vector3(118.4f, 0.008f, 0.08f), boundary, false);
            Cube("SouthBoundary", guideRoot, new Vector3(0f, 0.012f, -59.2f), new Vector3(118.4f, 0.008f, 0.08f), boundary, false);
            Cube("EastBoundary", guideRoot, new Vector3(59.2f, 0.012f, 0f), new Vector3(0.08f, 0.008f, 118.4f), boundary, false);
            Cube("WestBoundary", guideRoot, new Vector3(-59.2f, 0.012f, 0f), new Vector3(0.08f, 0.008f, 118.4f), boundary, false);

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            player.name = "Player";
            player.transform.position = Vector3.up;
            CreateCamera(player.transform, out Camera camera, out CinemachineCamera rig);
            var light = new GameObject("ArenaDirectionalLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.22f;
            light.color = new Color(0.72f, 0.82f, 1f);
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.10f, 0.12f, 0.16f);
            RenderSettings.fog = false;
            var bootstrap = new GameObject("CombatGame").AddComponent<CombatGameBootstrap>();
            bootstrap.AssignSceneReferences(player.GetComponent<PlayerMotor>(), camera, rig, retainedMaterials.ToArray(), waves, ground);
            CombatArenaAuthoring.Prepare(bootstrap);
            EditorSceneManager.SaveScene(scene, ScenePath);
            // Los Play Mode tests cargan los dos sandboxes por nombre. La build de entrega sigue
            // usando únicamente ScenePath mediante BuildPlayerOptions.scenes más abajo.
            var editorScenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(LocomotionSandboxBuilder.ScenePath, true),
                new EditorBuildSettingsScene(CombatSandboxBuilder.ScenePath, true)
            };
            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
                if (!editorScenes.Exists(entry => entry.path == existing.path))
                    editorScenes.Add(existing);
            EditorBuildSettings.scenes = editorScenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] CombatArena generated with player, physical boundary, camera and combat composition root.");
        }

        private static void CreateCamera(Transform target, out Camera camera, out CinemachineCamera rig)
        {
            camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = false;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 250f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.03f, 0.045f);
            camera.gameObject.AddComponent<AudioListener>();
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var brain = camera.gameObject.AddComponent<CinemachineBrain>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
            rig = new GameObject("CombatFollowCamera").AddComponent<CinemachineCamera>();
            rig.Follow = target;
            rig.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
            rig.Lens.FieldOfView = 60f;
            rig.Lens.NearClipPlane = 0.1f;
            rig.Lens.FarClipPlane = 250f;
            rig.Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            var follow = rig.gameObject.AddComponent<CinemachineFollow>();
            follow.FollowOffset = new Vector3(0f, 18f, -18f / Mathf.Tan(60f * Mathf.Deg2Rad));
            follow.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            follow.TrackerSettings.PositionDamping = new Vector3(0.25f, 0f, 0.25f);
            rig.transform.position = target.position + follow.FollowOffset;
            camera.transform.SetPositionAndRotation(rig.transform.position, rig.transform.rotation);
        }

        private static Material MaterialAsset(string name, string shaderName, Color color, bool transparent = false)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new BuildFailedException("Missing shader: " + shaderName);
            string path = MaterialDirectory + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            // Rebuilding spawn configuration must preserve the arena's authored presentation.
            if (material != null) return material;
            if (material == null)
            {
                material = new Material(shader) { name = "CombatArena_" + name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.05f);
            if (transparent)
            {
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
                if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
                if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Collider Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider = true)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name; cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position; cube.transform.localScale = scale;
            Renderer renderer = cube.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            if (!collider)
            {
                Object.DestroyImmediate(cube.GetComponent<Collider>());
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return collider ? cube.GetComponent<Collider>() : null;
        }

        public static void BuildWindows() => BuildWindowsInternal(false);
        public static void BuildWindowsDevelopment() => BuildWindowsInternal(true);

        public static void ValidateSpawnerSettingsAndBuildWindows()
        {
            CombatWaveSettings waves = AssetDatabase.LoadAssetAtPath<CombatWaveSettings>(WaveSettingsPath);
            if (waves == null) throw new BuildFailedException("The authored wave settings asset is missing.");
            string guid = AssetDatabase.AssetPathToGUID(WaveSettingsPath);
            float original = waves.OffscreenMargin;
            try
            {
                waves.OffscreenMargin = original + 0.125f;
                EditorUtility.SetDirty(waves);
                AssetDatabase.SaveAssets();
                Rebuild();
                var rebuilt = AssetDatabase.LoadAssetAtPath<CombatWaveSettings>(WaveSettingsPath);
                var bootstrap = Object.FindAnyObjectByType<CombatGameBootstrap>();
                if (AssetDatabase.AssetPathToGUID(WaveSettingsPath) != guid
                    || rebuilt.OffscreenMargin != original + 0.125f || bootstrap.WaveSettings != rebuilt
                    || bootstrap.ArenaGround == null)
                    throw new BuildFailedException("Rebuilding the arena did not preserve its authored spawn context.");
            }
            finally
            {
                waves = AssetDatabase.LoadAssetAtPath<CombatWaveSettings>(WaveSettingsPath);
                waves.OffscreenMargin = original;
                EditorUtility.SetDirty(waves);
                AssetDatabase.SaveAssets();
                Rebuild();
            }
            Directory.CreateDirectory("Logs/SpawnerPlayableRun");
            File.WriteAllText("Logs/SpawnerPlayableRun/SettingsPersistence.json",
                "{\"passed\":true,\"guid\":\"" + guid + "\",\"testedNonDefaultMargin\":true,\"restored\":true}");
            BuildWindows();
        }
        private static void BuildWindowsInternal(bool development)
        {
            PlayerSettings.enableFrameTimingStats = true;
            if (!File.Exists(ScenePath)) Rebuild();
            string directory = development ? "Builds/WindowsDevelopment" : "Builds/Windows";
            Directory.CreateDirectory(directory);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = BuildTarget.StandaloneWindows64,
                locationPathName = directory + "/EchoScrap.exe",
                options = development ? BuildOptions.Development : BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Combat Windows build failed: " + report.summary.result);
            Debug.Log("[EchoScrap] Combat Windows x86_64 build succeeded.");
        }
    }
}
