using System.IO;
using EchoScrap.Player;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EchoScrap.Editor
{
    public static class LocomotionSandboxBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/LocomotionSandbox.unity";
        public const string PrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        private const string SettingsPath = "Assets/_Project/Settings/PlayerLocomotion.asset";
        private const string InputPath = "Assets/_Project/Settings/PlayerControls.inputactions";

        [MenuItem("EchoScrap/Locomotion/Rebuild Sandbox")]
        public static void Rebuild()
        {
            foreach (string folder in new[] { "Scenes", "Prefabs", "Settings", "Materials" })
                Directory.CreateDirectory("Assets/_Project/" + folder);
            AssetDatabase.Refresh();

            var settings = AssetDatabase.LoadAssetAtPath<LocomotionSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LocomotionSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            InputActionAsset actions = GetControls();
            var friction = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(
                "Assets/_Project/Settings/PlayerFriction.physicMaterial");
            if (friction == null)
            {
                friction = new PhysicsMaterial("Player Friction")
                {
                    staticFriction = 0f, dynamicFriction = 0f, bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
                AssetDatabase.CreateAsset(friction, "Assets/_Project/Settings/PlayerFriction.physicMaterial");
            }

            Material floor = Material("Floor", new Color(0.18f, 0.23f, 0.26f));
            Material grid = Material("Grid", new Color(0.27f, 0.33f, 0.35f));
            Material wall = Material("Walls", new Color(0.56f, 0.61f, 0.62f));
            Material obstacle = Material("Obstacles", new Color(0.38f, 0.44f, 0.46f));
            Material player = Material("Player", new Color(1f, 0.65f, 0.13f));
            Material marker = Material("Facing Marker", new Color(0.97f, 0.94f, 0.80f));
            BuildPrefab(settings, actions, friction, player, marker);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var arena = new GameObject("Arena");
            Cube("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(120f, 1f, 120f), floor, arena.transform);
            Cube("Wall North", new Vector3(0f, 1.5f, 60.5f), new Vector3(122f, 3f, 1f), wall, arena.transform);
            Cube("Wall South", new Vector3(0f, 1.5f, -60.5f), new Vector3(122f, 3f, 1f), wall, arena.transform);
            Cube("Wall East", new Vector3(60.5f, 1.5f, 0f), new Vector3(1f, 3f, 120f), wall, arena.transform);
            Cube("Wall West", new Vector3(-60.5f, 1.5f, 0f), new Vector3(1f, 3f, 120f), wall, arena.transform);
            Cube("Obstacle A", new Vector3(-8f, 1.25f, 3f), new Vector3(3f, 2.5f, 3f), obstacle, arena.transform);
            Cube("Obstacle B", new Vector3(7f, 1.25f, 5f), new Vector3(6f, 2.5f, 2f), obstacle, arena.transform);
            Cube("Obstacle C", new Vector3(0f, 1.25f, -10f), new Vector3(3f, 2.5f, 5f), obstacle, arena.transform);
            var gridRoot = new GameObject("Distance Grid (10 m)");
            gridRoot.transform.SetParent(arena.transform);
            for (int i = -5; i <= 5; i++)
            {
                Cube("Grid X " + i, new Vector3(i * 10f, 0.006f, 0f),
                    new Vector3(0.035f, 0.01f, 120f), grid, gridRoot.transform, false);
                Cube("Grid Z " + i, new Vector3(0f, 0.006f, i * 10f),
                    new Vector3(120f, 0.01f, 0.035f), grid, gridRoot.transform, false);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            instance.transform.position = Vector3.up;
            BuildCamera(instance.transform);
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.60f, 0.66f);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] Locomotion sandbox generated.");
        }

        private static InputActionAsset GetControls()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
            if (actions != null)
                return actions;
            actions = ScriptableObject.CreateInstance<InputActionAsset>();
            actions.name = "PlayerControls";
            InputActionMap map = actions.AddActionMap("Player");
            InputAction move = map.AddAction("Move", InputActionType.Value);
            move.expectedControlType = "Vector2";
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            move.AddBinding("<Gamepad>/leftStick");
            move.AddBinding("<Gamepad>/dpad");
            File.WriteAllText(InputPath, actions.ToJson());
            Object.DestroyImmediate(actions);
            AssetDatabase.ImportAsset(InputPath);
            return AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
        }

        private static void BuildPrefab(LocomotionSettings settings, InputActionAsset actions,
            PhysicsMaterial friction, Material bodyMaterial, Material markerMaterial)
        {
            var root = new GameObject("Player");
            var body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = 2f;
            capsule.radius = 0.5f;
            capsule.sharedMaterial = friction;
            var input = root.AddComponent<PlayerInputReader>();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            mesh.name = "Placeholder Body";
            mesh.transform.SetParent(visual.transform, false);
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            mesh.GetComponent<Renderer>().sharedMaterial = bodyMaterial;
            Cube("Facing Marker", new Vector3(0f, 0.1f, 0.7f),
                new Vector3(0.22f, 0.14f, 0.8f), markerMaterial, visual.transform, false);
            var motor = root.AddComponent<PlayerMotor>();
            SetReference(input, "actions", actions);
            SetReference(motor, "settings", settings);
            SetReference(motor, "visual", visual.transform);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
        }

        private static void BuildCamera(Transform target)
        {
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = false;
            camera.fieldOfView = 60f;
            camera.farClipPlane = 250f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.16f, 0.19f);
            camera.gameObject.AddComponent<AudioListener>();
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var brain = camera.gameObject.AddComponent<CinemachineBrain>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
            var rig = new GameObject("Player Camera").AddComponent<CinemachineCamera>();
            rig.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
            rig.Follow = target;
            rig.Lens.FieldOfView = 60f;
            rig.Lens.FarClipPlane = 250f;
            rig.Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            var follow = rig.gameObject.AddComponent<CinemachineFollow>();
            follow.FollowOffset = new Vector3(0f, 18f, -18f / Mathf.Tan(60f * Mathf.Deg2Rad));
            follow.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            follow.TrackerSettings.PositionDamping = new Vector3(0.25f, 0f, 0.25f);
            rig.transform.position = target.position + follow.FollowOffset;
            camera.transform.SetPositionAndRotation(rig.transform.position, rig.transform.rotation);
        }

        private static Material Material(string name, Color color)
        {
            string path = "Assets/_Project/Materials/" + name.Replace(" ", "") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject Cube(string name, Vector3 position, Vector3 scale,
            Material material, Transform parent, bool collider = true)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider)
                Object.DestroyImmediate(cube.GetComponent<Collider>());
            return cube;
        }

        private static void SetReference(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void BuildWindows()
        {
            Directory.CreateDirectory("Builds/Windows");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = BuildTarget.StandaloneWindows64,
                locationPathName = "Builds/Windows/EchoScrap.exe",
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Windows build failed: " + report.summary.result);
            Debug.Log("[EchoScrap] Windows x86_64 build succeeded.");
        }

    }
}
