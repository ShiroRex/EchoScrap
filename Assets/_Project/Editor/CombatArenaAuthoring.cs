using System;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EchoScrap.Editor
{
    /// <summary>Prepares ordinary, editable Unity assets without starting the combat simulation.</summary>
    public static class CombatArenaAuthoring
    {
        public const string PlayerPrefabPath = "Assets/_Project/Prefabs/CombatPlayer.prefab";
        public const string PlayerSettingsPath = "Assets/_Project/Settings/PlayerCombat_Default.asset";

        // Also used by the arena builder so rebuilding does not discard the authored setup.
        public static void Prepare(CombatGameBootstrap game)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Prepare the arena outside Play Mode.");
            var settings = AssetDatabase.LoadAssetAtPath<PlayerCombatSettings>(PlayerSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PlayerCombatSettings>();
                AssetDatabase.CreateAsset(settings, PlayerSettingsPath);
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null) prefab = CreatePlayerPrefab(settings);

            GameObject player = game.Motor.gameObject;
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(player) != PlayerPrefabPath)
            {
                var replacement = (GameObject)PrefabUtility.InstantiatePrefab(prefab, game.gameObject.scene);
                replacement.name = player.name;
                replacement.transform.SetPositionAndRotation(player.transform.position, player.transform.rotation);
                replacement.transform.localScale = player.transform.localScale;
                Set(game, "playerMotor", replacement.GetComponent<PlayerMotor>());
                UnityEngine.Object.DestroyImmediate(player);
                player = replacement;
            }

            Transform systems = game.transform;
            var session = Child<CombatSession>(systems, "CombatSession");
            Ensure<CombatOutcomeResolver>(session.gameObject);
            var progression = Child<CombatProgression>(systems, "Progression");
            var hazards = Child<CorrosivePuddlePool>(systems, "CorrosiveHazards");
            Child<MothExplosionPool>(session.transform, "Moth Explosions");
            var death = Child<EnemyDeathPresentationPool>(systems, "EnemyDeathPresentation");
            var enemies = Child<EnemyPool>(systems, "EnemyPool");
            var pickups = Child<CombatPickupPool>(systems, "Pickups");
            var draft = Child<UpgradeDraftController>(systems, "UpgradeDraft");
            var draftView = Ensure<UpgradeDraftPresentation>(draft.gameObject);
            var director = Child<CombatWaveDirector>(systems, "WaveDirector");
            var encounter = Child<BossEncounter>(systems, "BossEncounter");
            Ensure<GolgoraDeathPresentation>(encounter.gameObject);
            Child<CombatHud>(systems, "CombatHud");
            var numbers = Child<DamageNumberPresenter>(systems, "DamageNumbers");
            Transform effects = ChildTransform(systems, "RuntimeEffects");
            var events = Child<EventSystem>(systems, "CombatEventSystem");
            Ensure<InputSystemUIInputModule>(events.gameObject).AssignDefaultActions();

            var motor = player.GetComponent<PlayerMotor>();
            var input = player.GetComponent<PlayerInputReader>();
            var stats = player.GetComponent<PlayerRuntimeStats>();
            var health = player.GetComponent<PlayerHealth>();
            var weapon = player.GetComponent<FlashlightWeapon>();
            var flashlight = player.GetComponent<FlashlightPresentation>();
            var dash = player.GetComponent<PlayerDashPresentation>();
            var hurt = player.GetComponent<PlayerHurtPresentation>();
            Set(health, "session", session);
            Set(weapon, "session", session);
            Set(flashlight, "session", session); Set(flashlight, "worldEffectsRoot", effects);
            Set(dash, "worldEffectsRoot", effects); Set(dash, "session", session); Set(dash, "motor", motor);
            Set(hurt, "session", session);
            Set(session, "playerHealth", health); Set(session, "playerStats", stats);
            Set(session, "playerMotor", motor); Set(session, "playerInput", input); Set(session, "playerWeapon", weapon);
            Set(progression, "session", session);
            Set(hazards, "session", session); Set(hazards, "targetPlayer", health);
            Set(death, "session", session);
            Set(enemies, "session", session); Set(enemies, "playerHealth", health); Set(enemies, "puddlePool", hazards);
            Set(pickups, "session", session); Set(pickups, "playerHealth", health);
            Set(pickups, "stats", stats); Set(pickups, "progression", progression);
            Set(draft, "session", session); Set(draft, "stats", stats);
            Set(draft, "health", health); Set(draft, "progression", progression);
            Set(draftView, "session", session); Set(draftView, "controller", draft);
            Set(director, "session", session); Set(director, "enemyPool", enemies); Set(director, "player", health);
            Set(director, "gameplayCamera", game.GameplayCamera); Set(director, "cameraRig", game.CameraRig);
            Set(director, "hazards", hazards); Set(director, "settings", game.WaveSettings);
            Set(director, "arenaGround", game.ArenaGround);
            Set(encounter, "session", session); Set(encounter, "enemies", enemies);
            Set(encounter, "hazards", hazards); Set(encounter, "director", director); Set(encounter, "player", health);
            Set(numbers, "session", session); Set(numbers, "worldCamera", game.GameplayCamera);
            var feedback = Ensure<CombatCameraFeedback>(game.CameraRig.gameObject);
            Set(feedback, "session", session); Set(feedback, "trackedTarget", player.transform);
            Set(feedback, "playerMotor", motor); Set(feedback, "inputReader", input); Set(feedback, "flashlightWeapon", weapon);
            game.CameraRig.Follow = player.transform;
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
        }

        // One-time migration of the saved arena, preserving its floor, camera and wave balance.
        public static void PrepareSavedArena()
        {
            var scene = EditorSceneManager.OpenScene(CombatArenaBuilder.ScenePath);
            CombatGameBootstrap game = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out CombatGameBootstrap found)) game = found;
            if (game == null) throw new InvalidOperationException("CombatGame is missing from CombatArena.");
            Prepare(game);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] CombatPlayer, player balance and arena systems are ready for Inspector editing.");
        }

        public static void ValidateSavedArena()
        {
            PrepareSavedArena();
            var settings = AssetDatabase.LoadAssetAtPath<PlayerCombatSettings>(PlayerSettingsPath);
            string original = EditorJsonUtility.ToJson(settings);
            try
            {
                var edit = new SerializedObject(settings);
                edit.FindProperty("maxHealth").floatValue = 137f;
                edit.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                // Preparing twice must preserve designer edits and must not duplicate systems.
                PrepareSavedArena();
                PrepareSavedArena();
                var scene = EditorSceneManager.OpenScene(CombatArenaBuilder.ScenePath);
                int sessions = 0;
                PlayerRuntimeStats stats = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    sessions += root.GetComponentsInChildren<CombatSession>(true).Length;
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                            throw new InvalidOperationException("Missing script on " + child.name);
                    stats ??= root.GetComponent<PlayerRuntimeStats>();
                }
                if (sessions != 1 || stats == null || stats.Settings != settings || settings.MaxHealth != 137f)
                    throw new InvalidOperationException("Authored balance or references were lost after saving and reopening.");
                var flashlight = stats.GetComponent<FlashlightPresentation>();
                var view = new SerializedObject(flashlight).FindProperty("prototypeView").objectReferenceValue;
                if (view == null || stats.transform.Find("Visual/PresentationPivot") == null)
                    throw new InvalidOperationException("The player visual hierarchy was not authored.");
                System.IO.Directory.CreateDirectory("Logs/InspectorFriendlyCombat");
                System.IO.File.WriteAllText("Logs/InspectorFriendlyCombat/AuthoringValidation.json",
                    "{\"passed\":true,\"savedAndReopened\":true,\"nonDefaultHealthPreserved\":137,\"missingScripts\":0,\"sessions\":1}");
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(original, settings);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
        }

        private static GameObject CreatePlayerPrefab(PlayerCombatSettings settings)
        {
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LocomotionSandboxBuilder.PrefabPath);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                var composer = Ensure<ActorVisualComposer>(player);
                composer.EnsureHierarchy();
                var stats = Ensure<PlayerRuntimeStats>(player);
                Set(stats, "settings", settings);
                var health = Ensure<PlayerHealth>(player);
                var motor = player.GetComponent<PlayerMotor>();
                var input = player.GetComponent<PlayerInputReader>();
                var weapon = Ensure<FlashlightWeapon>(player);
                var presentation = Ensure<FlashlightPresentation>(player);
                Ensure<PlayerDashPresentation>(player);
                var hurt = Ensure<PlayerHurtPresentation>(player);
                Ensure<ActorMotionPresentation>(player);
                Transform pivot = ChildTransform(player.transform, "FlashlightPivot");
                Set(composer, "flashlightPivot", pivot);
                Set(motor, "runtimeStats", stats); Set(motor, "visualComposer", composer);
                Set(health, "stats", stats); Set(health, "motor", motor); Set(health, "inputReader", input);
                Set(health, "visualComposer", composer); Set(health, "hurtboxCollider", player.GetComponent<Collider>());
                Set(hurt, "playerHealth", health);
                Set(weapon, "runtimeStats", stats); Set(weapon, "motor", motor);
                Set(weapon, "inputReader", input); Set(weapon, "playerHealth", health); Set(weapon, "aimPivot", pivot);

                var cone = Child<MeshFilter>(pivot, "FlashlightConeVisual");
                var coneRenderer = Ensure<MeshRenderer>(cone.gameObject);
                coneRenderer.enabled = false;
                var emitterObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                emitterObject.name = "FlashlightEmitterIndicator";
                UnityEngine.Object.DestroyImmediate(emitterObject.GetComponent<Collider>());
                emitterObject.transform.SetParent(pivot, false);
                emitterObject.transform.localPosition = new Vector3(0f, 0.08f, 0.35f);
                emitterObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                emitterObject.transform.localScale = Vector3.one * 1.35f;
                var emitter = emitterObject.GetComponent<MeshRenderer>();
                var lens = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/CombatArena/FlashlightLens.mat");
                coneRenderer.sharedMaterial = emitter.sharedMaterial = lens;
                emitter.shadowCastingMode = coneRenderer.shadowCastingMode = ShadowCastingMode.Off;
                emitter.receiveShadows = coneRenderer.receiveShadows = false;
                var light = Child<Light>(pivot, "FlashlightSpotLight");
                light.transform.localPosition = new Vector3(0f, 0.65f, 0.8f);
                light.transform.localRotation = Quaternion.Euler(16f, 0f, 0f);
                light.type = LightType.Spot; light.range = settings.FlashlightRange + 2f;
                light.spotAngle = settings.FlashlightAngle; light.intensity = 20f;
                light.color = new Color(232f / 255f, 193f / 255f, 112f / 255f);
                light.shadows = LightShadows.Soft;
                Ensure<UniversalAdditionalLightData>(light.gameObject).usePipelineSettings = true;
                var outline = Child<LineRenderer>(pivot, "Prototype coverage outline");
                outline.enabled = false;
                var view = Child<Combat.Prototype.PlayerFlashlightView>(player.transform, "Refactor Flashlight View");
                view.Configure(weapon, pivot, light, cone, coneRenderer, outline, emitter);
                Ensure<OilLanternVolume>(view.gameObject);
                Set(presentation, "weapon", weapon); Set(presentation, "flashlightPivot", pivot);
                Set(presentation, "spotLight", light); Set(presentation, "coneMeshFilter", cone);
                Set(presentation, "coneMeshRenderer", coneRenderer); Set(presentation, "emitterRenderer", emitter);
                Set(presentation, "prototypeView", view);
                return PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(player); }
        }

        private static T Ensure<T>(GameObject target) where T : Component =>
            target.TryGetComponent<T>(out var component) ? component : target.AddComponent<T>();

        private static Transform ChildTransform(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static T Child<T>(Transform parent, string name) where T : Component =>
            Ensure<T>(ChildTransform(parent, name).gameObject);

        private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null) throw new InvalidOperationException(target.GetType().Name + "." + field + " is not serialized.");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }
}
