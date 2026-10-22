using System.IO;
using EchoScrap.Combat;
using EchoScrap.Player;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Editor
{
    public static class CombatSandboxBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/CombatSandbox.unity";
        public const string WindowsBuildPath = "Builds/WindowsMoth/EchoScrap.exe";
        private const string MaterialFolder = "Assets/_Project/Materials/CombatSandbox";

        [MenuItem("EchoScrap/Combat/Rebuild Five Enemy Sandbox")]
        public static void Rebuild()
        {
            if (!File.Exists(LocomotionSandboxBuilder.ScenePath))
                throw new BuildFailedException("Falta la escena base de locomoción: " + LocomotionSandboxBuilder.ScenePath);

            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.OpenScene(LocomotionSandboxBuilder.ScenePath, OpenSceneMode.Single);
            // Save under the new path before editing: the original scene and player prefab stay byte-for-byte intact.
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new BuildFailedException("No se pudo crear la copia CombatSandbox.");
            foreach (var root in scene.GetRootGameObjects())
            {
                Transform arena = root.name == "Arena" ? root.transform : null;
                if (arena == null) continue;
                for (int i = arena.childCount - 1; i >= 0; i--)
                    if (arena.GetChild(i).name.StartsWith("Obstacle ")) Object.DestroyImmediate(arena.GetChild(i).gameObject);
            }
            var player = Object.FindAnyObjectByType<PlayerMotor>();
            if (player == null) throw new BuildFailedException("La escena base no contiene PlayerMotor.");
            var setup = new GameObject("Combat Sandbox Setup").AddComponent<CombatSandboxSetup>();
            var serialized = new SerializedObject(setup);
            serialized.FindProperty("playerMotor").objectReferenceValue = player;
            var references = serialized.FindProperty("shaderReferences");
            references.arraySize = 3;
            references.GetArrayElementAtIndex(0).objectReferenceValue = ShaderMaterial("UnlitOpaque", "Universal Render Pipeline/Unlit", false);
            references.GetArrayElementAtIndex(1).objectReferenceValue = ShaderMaterial("UnlitTransparent", "Universal Render Pipeline/Unlit", true);
            references.GetArrayElementAtIndex(2).objectReferenceValue = ShaderMaterial("RescueLens", "EchoScrap/Flashlight Rescue Lens", true);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new BuildFailedException("No se pudo guardar CombatSandbox.");

            var buildScenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!buildScenes.Exists(entry => entry.path == ScenePath))
                buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] CombatSandbox generado: cinco arquetipos, linterna, dash y reinicio; sin oleadas ni progresión.");
        }

        [MenuItem("EchoScrap/Combat/Build Windows Five Enemy Sandbox")]
        public static void BuildWindows()
        {
            Rebuild();
            Directory.CreateDirectory(Path.GetDirectoryName(WindowsBuildPath));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = BuildTarget.StandaloneWindows64,
                locationPathName = WindowsBuildPath,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Falló la build Windows de combate: " + report.summary.result);
            Debug.Log("[EchoScrap] Build Windows x86_64 de cinco enemigos completada: " + WindowsBuildPath);
        }

        private static Material ShaderMaterial(string name, string shaderName, bool transparent)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new BuildFailedException("No se encuentra el shader de combate: " + shaderName);
            material = new Material(shader) { name = "Combat Sandbox " + name };
            if (transparent && material.HasProperty("_Surface"))
            {
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
