using EchoScrap.Combat;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EchoScrap.Editor
{
    /// <summary>Updates the collector's authored proportions and the saved arena camera together.</summary>
    public static class CollectorCameraAdjustmentAuthoring
    {
        private const float CollectorScale = 0.65f;
        private const float CameraPitch = 50f;

        [MenuItem("EchoScrap/Art/Collector capsule height and camera 50 degrees")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Stop Play Mode before updating the collector and camera.");

            GameObject prefab = PrefabUtility.LoadPrefabContents(CombatArenaAuthoring.PlayerPrefabPath);
            try
            {
                ConfigureView(prefab.GetComponent<CollectorSpriteView>());
                PrefabUtility.SaveAsPrefabAsset(prefab, CombatArenaAuthoring.PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }

            const string scenePath = "Assets/_Project/Scenes/CombatArena.unity";
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != scenePath)
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(scenePath);
            }

            var game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            ConfigureView(game.Motor.GetComponent<CollectorSpriteView>());
            CinemachineCamera rig = game.CameraRig;
            CinemachineFollow follow = rig.GetComponent<CinemachineFollow>();
            float distance = follow.FollowOffset.magnitude;
            Quaternion rotation = Quaternion.Euler(CameraPitch, rig.transform.eulerAngles.y, rig.transform.eulerAngles.z);
            follow.FollowOffset = rotation * Vector3.back * distance;
            Vector3 target = rig.Follow != null ? rig.Follow.position : game.Motor.transform.position;
            rig.transform.SetPositionAndRotation(target + follow.FollowOffset, rotation);
            game.GameplayCamera.transform.SetPositionAndRotation(rig.transform.position, rotation);
            rig.PreviousStateIsValid = false;
            EditorUtility.SetDirty(follow);
            EditorUtility.SetDirty(rig);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] Collector scale 0.65 matches the visible capsule height at camera pitch 50 degrees. Camera distance, FOV and arena lighting preserved.");
        }

        private static void ConfigureView(CollectorSpriteView view)
        {
            if (view == null) throw new System.InvalidOperationException("Combat player is missing its collector sprite view.");
            var serialized = new SerializedObject(view);
            serialized.FindProperty("visualScale").floatValue = CollectorScale;
            float height = serialized.FindProperty("standingHeight").floatValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Vector3 scale = new Vector3(CollectorScale, CollectorScale * height, CollectorScale);
            view.CharacterRenderer.transform.localScale = scale;
            view.ProjectedShadowRenderer.transform.localScale = scale;
            var contact = serialized.FindProperty("shadowRenderer").objectReferenceValue as MeshRenderer;
            if (contact != null) contact.transform.localScale = Vector3.one * CollectorScale;
            EditorUtility.SetDirty(view);
        }
    }
}
