using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace EchoScrap.Editor
{
    public static class ArenaSkyboxAuthoring
    {
        private const string Folder = "Assets/_Project/Art/Environment/Sky17/";
        private const string ScenePath = "Assets/_Project/Scenes/CombatArena.unity";

        [MenuItem("EchoScrap/Arte 3D/Aplicar cielo Sky17 y luz violeta tenue")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Stop Play Mode before editing the arena sky.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath);
            }
            AssetDatabase.ImportAsset(Folder + "sky_17_2k.png", ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(Folder + "sky_17_2k.png");
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.isReadable = false;
            importer.SaveAndReimport();

            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "Sky17.mat");
            if (material == null)
            {
                material = new Material(Shader.Find("Skybox/Panoramic")) { name = "Sky17" };
                AssetDatabase.CreateAsset(material, Folder + "Sky17.mat");
            }
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "sky_17_2k.png"));
            material.SetColor("_Tint", Color.gray); // Preserve the source's violet night palette.
            material.SetFloat("_Exposure", 0.8f);
            material.SetFloat("_Rotation", 0f);
            material.SetFloat("_Mapping", 1f); // Latitude-longitude panorama.
            material.SetFloat("_ImageType", 0f); // Full 360 degrees.
            material.SetFloat("_Layout", 0f);
            EditorUtility.SetDirty(material);
            RenderSettings.skybox = material;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientIntensity = 0.22f;
            // Trilight colors provide the sky's soft lavender fill to URP meshes and lit sprites.
            RenderSettings.ambientSkyColor = new Color(0.50f, 0.38f, 0.69f) * 0.22f;
            RenderSettings.ambientEquatorColor = new Color(0.38f, 0.28f, 0.52f) * 0.22f;
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.13f, 0.26f) * 0.22f;
            var sun = GameObject.Find("ArenaDirectionalLight").GetComponent<Light>();
            sun.color = new Color(0.80f, 0.68f, 1f);
            sun.intensity = 0.22f;
            GameObject.Find("Main Camera").GetComponent<Camera>().clearFlags = CameraClearFlags.Skybox;
            DynamicGI.UpdateEnvironment();
            AssetDatabase.SaveAssets();
            CapturePreview();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("ARENA_SKY17_OK panorama=2048x1024 exposure=0.8 environment=0.22 lavender");
        }

        public static void CapturePreview()
        {
            var camera = new GameObject("Sky17PreviewCamera").AddComponent<Camera>();
            camera.CopyFrom(GameObject.Find("Main Camera").GetComponent<Camera>());
            camera.enabled = false;
            camera.orthographic = false;
            camera.fieldOfView = 65f;
            camera.farClipPlane = 500f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            var target = new RenderTexture(1600, 900, 24);
            var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.transform.position = new Vector3(0f, 6f, -24f);
                camera.transform.LookAt(new Vector3(0f, 12f, 80f));
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                texture.Apply();
                Directory.CreateDirectory("Logs/Sky17");
                File.WriteAllBytes("Logs/Sky17/ArenaSky17.png", texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(texture);
                target.Release(); Object.DestroyImmediate(target);
                Object.DestroyImmediate(camera.gameObject);
            }
        }
    }
}
