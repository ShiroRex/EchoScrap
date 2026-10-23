using System;
using System.IO;
using System.Text.RegularExpressions;
using EchoScrap.Combat;
using EchoScrap.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Editor
{
    /// <summary>Imports the approved atlas and updates the combat prefab without rebuilding the authored arena.</summary>
    public static class CollectorSpriteAuthoring
    {
        public const string ArtPath = "Assets/_Project/Art/Characters/Coleccionista/";
        public const string SetPath = ArtPath + "Coleccionista_Sprites.asset";
        private const string MaterialPath = "Assets/_Project/Materials/Characters/";

        [MenuItem("EchoScrap/Art/Connect Collector To Player")]
        public static void Prepare()
        {
            Directory.CreateDirectory(MaterialPath);
            AssetDatabase.Refresh();
            ImportAtlas(ArtPath + "Coleccionista_Atlas.png", false);
            ImportAtlas(ArtPath + "Coleccionista_Normal.png", true);
            CharacterSpriteSet set = AssetDatabase.LoadAssetAtPath<CharacterSpriteSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<CharacterSpriteSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }
            ReadManifest(set);
            Material color = Material("Coleccionista", "EchoScrap/Character Pixel Lit");
            color.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath + "Coleccionista_Atlas.png"));
            color.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath + "Coleccionista_Normal.png"));
            color.SetVector("_FrameRect", set.clips[0].frames[0].uvRect);
            color.SetColor("_BaseColor", Color.white);
            Material ghost = Material("Coleccionista_Afterimage", "EchoScrap/Character Afterimage");
            ghost.SetTexture("_BaseMap", color.GetTexture("_BaseMap"));
            Material shadow = Material("Coleccionista_Shadow", "EchoScrap/Character Contact Shadow");
            shadow.SetColor("_BaseColor", new Color(0.005f, 0.006f, 0.009f, 0.85f));
            Material cast = Material("Coleccionista_ProjectedShadow", "EchoScrap/Character Projected Shadow");
            cast.SetTexture("_BaseMap", color.GetTexture("_BaseMap"));
            Mesh quad = Quad(ArtPath + "Coleccionista_Quad.asset", new Rect(-32f / set.pixelsPerUnit, -2f / set.pixelsPerUnit, 64f / set.pixelsPerUnit, 64f / set.pixelsPerUnit));
            Mesh shadowQuad = Quad(ArtPath + "Coleccionista_ShadowQuad.asset", new Rect(-0.72f, -0.45f, 1.44f, 0.9f));

            GameObject root = PrefabUtility.LoadPrefabContents(CombatArenaAuthoring.PlayerPrefabPath);
            try
            {
                ActorVisualComposer composer = root.GetComponent<ActorVisualComposer>();
                composer.EnsureHierarchy();
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == "Placeholder Body" || child.name == "Facing Marker") child.gameObject.SetActive(false);
                MeshRenderer renderer = Renderer(composer.PresentationPivot, "Coleccionista Pixels", quad, color);
                CapsuleCollider collider = root.GetComponent<CapsuleCollider>();
                Vector3 foot = Vector3.up * (collider.center.y - collider.height * 0.5f);
                renderer.transform.localPosition = foot + Vector3.up * 0.025f;
                renderer.transform.localRotation = Quaternion.identity;
                MeshRenderer contact = Renderer(root.transform, "Coleccionista Contact Shadow", shadowQuad, shadow);
                contact.transform.localPosition = foot + Vector3.up * 0.025f;
                contact.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                MeshRenderer projected = Renderer(root.transform, "Coleccionista Projected Shadow", quad, cast);
                projected.transform.localPosition = foot + Vector3.up * 0.025f;
                CollectorSpriteView view = root.GetComponent<CollectorSpriteView>();
                if (view == null) view = root.AddComponent<CollectorSpriteView>();
                Set(view, "sprites", set); Set(view, "characterRenderer", renderer); Set(view, "shadowRenderer", contact);
                Set(view, "projectedShadowRenderer", projected);
                Set(view, "afterimageMaterial", ghost); Set(view, "motor", root.GetComponent<EchoScrap.Player.PlayerMotor>());
                Set(view, "health", root.GetComponent<PlayerHealth>()); Set(view, "composer", composer);
                Set(view, "pulseIndicator", root.GetComponent<FlashlightPresentation>().EmitterRenderer.transform);
                var serialized = new SerializedObject(view);
                serialized.FindProperty("footOffset").vector3Value = foot;
                // Match the arena's 60-degree reference camera before Play Mode starts too.
                float previewScale = serialized.FindProperty("visualScale").floatValue;
                renderer.transform.localScale = new Vector3(previewScale, previewScale * 2f, previewScale);
                contact.transform.localScale = Vector3.one * previewScale;
                projected.transform.localScale = renderer.transform.localScale;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                serialized = new SerializedObject(composer);
                serialized.FindProperty("authoredSpritePoses").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                serialized = new SerializedObject(root.GetComponentInChildren<OilLanternVolume>(true));
                serialized.FindProperty("drawFlameMesh").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, CombatArenaAuthoring.PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            EditorUtility.SetDirty(set); EditorUtility.SetDirty(color); EditorUtility.SetDirty(ghost); EditorUtility.SetDirty(shadow); EditorUtility.SetDirty(cast);
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] Collector connected: 108 frames, 16 clips, upright lit sprite, grounded shadows and sprite dash pool. Arena unchanged.");
        }

        private static void ReadManifest(CharacterSpriteSet set)
        {
            string json = File.ReadAllText(ArtPath + "Coleccionista_Atlas.json");
            MatchCollection rects = Regex.Matches(json, "\"frame\"\\s*:\\s*\\{\\s*\"x\"\\s*:\\s*(\\d+),\\s*\"y\"\\s*:\\s*(\\d+),\\s*\"w\"\\s*:\\s*(\\d+),\\s*\"h\"\\s*:\\s*(\\d+)");
            MatchCollection durations = Regex.Matches(json, "\"duration\"\\s*:\\s*(\\d+)");
            if (rects.Count != 108 || durations.Count != 108) throw new BuildFailedException("Collector manifest must contain 108 frames and timings.");
            set.clips = new CharacterSpriteSet.Clip[16];
            int frame = 0;
            for (int d = 0; d < 4; d++)
                for (int c = 0; c < 4; c++)
                {
                    int count = c == 2 ? 3 : 8;
                    var clip = new CharacterSpriteSet.Clip { name = ((CharacterMotion)c) + "_" + (CharacterFacing)d,
                        loop = c < 2, frames = new CharacterSpriteSet.Frame[count] };
                    for (int k = 0; k < count; k++, frame++)
                    {
                        Match r = rects[frame];
                        int x = int.Parse(r.Groups[1].Value), y = int.Parse(r.Groups[2].Value);
                        int w = int.Parse(r.Groups[3].Value), h = int.Parse(r.Groups[4].Value);
                        if (w != 64 || h != 64) throw new BuildFailedException("Collector cells must remain 64x64.");
                        clip.frames[k] = new CharacterSpriteSet.Frame {
                            uvRect = new Vector4(x / 512f, 1f - (y + h) / 1024f, w / 512f, h / 1024f),
                            duration = int.Parse(durations[frame].Groups[1].Value) / 1000f };
                    }
                    set.clips[d * 4 + c] = clip;
                }
        }

        private static void ImportAtlas(string path, bool normal)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            if (normal) importer.flipGreenChannel = false;
            importer.SaveAndReimport();
        }

        private static Material Material(string name, string shader)
        {
            string path = MaterialPath + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader found = Shader.Find(shader);
                if (found == null) throw new BuildFailedException("Missing character shader: " + shader);
                material = new Material(found) { name = name }; AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }

        private static Mesh Quad(string path, Rect rect)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear();
            mesh.vertices = new[] { new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMax, rect.yMin),
                new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMax, rect.yMax) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
            mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); return mesh;
        }

        private static MeshRenderer Renderer(Transform parent, string name, Mesh mesh, Material material)
        {
            Transform child = parent.Find(name);
            if (child == null) { child = new GameObject(name).transform; child.SetParent(parent, false); }
            MeshFilter filter = child.GetComponent<MeshFilter>();
            if (filter == null) filter = child.gameObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = child.gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh; renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true; return renderer;
        }

        private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
