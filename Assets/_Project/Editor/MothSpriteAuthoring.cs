using System.IO;
using System.Text.RegularExpressions;
using EchoScrap.Combat;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace EchoScrap.Editor
{
    public static class MothSpriteAuthoring
    {
        private const string Art = "Assets/_Project/Art/Characters/PolillaReactor/";
        private const string Data = Art + "Data/";
        private const string ResourcesPath = "Assets/_Project/Resources/Characters/";
        [MenuItem("EchoScrap/Art/Connect Reactor Moth")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Data); Directory.CreateDirectory(ResourcesPath);
            Directory.CreateDirectory("Assets/_Project/Resources/Pickups");
            AssetDatabase.Refresh();
            Import("PolillaReactor_Atlas.png", false, true);
            Import("PolillaReactor_Normal.png", true, false);
            Import("PolillaReactor_Emission.png", false, false);
            var set = Asset<MothSpriteSet>(ResourcesPath + "PolillaReactor.asset");
            string json = File.ReadAllText("ArtSource/Characters/PolillaReactor/PolillaReactor_Atlas.json");
            MatchCollection rects = Regex.Matches(json, "\"frame\"\\s*:\\s*\\{\\s*\"x\"\\s*:\\s*(\\d+),\\s*\"y\"\\s*:\\s*(\\d+),\\s*\"w\"\\s*:\\s*(\\d+),\\s*\"h\"\\s*:\\s*(\\d+)");
            MatchCollection durations = Regex.Matches(json, "\"duration\"\\s*:\\s*(\\d+)");
            if (rects.Count != 62 || durations.Count != 62) throw new BuildFailedException("Moth manifest requires 62 frames and timings.");
            set.clips = new CharacterSpriteSet.Clip[9];
            int frame = 0;
            for (int c = 0; c < 9; c++)
            {
                int count = c == 8 || c % 2 == 1 ? 6 : 8;
                var clip = new CharacterSpriteSet.Clip { name = c == 8 ? "Burst" : (c % 2 == 0 ? "Fly_" : "Prime_") + (CharacterFacing)(c / 2),
                    loop = c != 8 && c % 2 == 0, frames = new CharacterSpriteSet.Frame[count] };
                for (int k = 0; k < count; k++, frame++)
                {
                    Match r = rects[frame];
                    int x = int.Parse(r.Groups[1].Value), y = int.Parse(r.Groups[2].Value);
                    if (int.Parse(r.Groups[3].Value) != 64 || int.Parse(r.Groups[4].Value) != 64)
                        throw new BuildFailedException("Moth cells must remain 64x64.");
                    clip.frames[k] = new CharacterSpriteSet.Frame { uvRect = new Vector4(x / 512f, 1f - (y + 64) / 576f, 64f / 512f, 64f / 576f),
                        duration = int.Parse(durations[frame].Groups[1].Value) / 1000f };
                }
                set.clips[c] = clip;
            }
            set.mesh = Quad(Data + "PolillaReactor_Quad.asset", new Rect(-1.2f, -1.6f, 2.4f, 3.2f));
            set.shadowMesh = Quad(Data + "PolillaReactor_ShadowQuad.asset", new Rect(-0.65f, -0.42f, 1.3f, 0.84f));
            set.material = Material(Data + "PolillaReactor.mat", "EchoScrap/Character Pixel Lit");
            set.material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "PolillaReactor_Atlas.png"));
            set.material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "PolillaReactor_Normal.png"));
            set.material.SetTexture("_GlowMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "PolillaReactor_Emission.png"));
            set.material.SetColor("_GlowColor", new Color(1.8f, 0.42f, 0.05f)); set.material.SetFloat("_GlowStrength", 0.9f);
            set.material.SetColor("_BaseColor", Color.white); set.material.SetColor("_EmissionColor", Color.clear);
            set.material.SetVector("_FrameRect", set.clips[0].frames[0].uvRect);
            set.shadowMaterial = Material(Data + "PolillaReactor_Shadow.mat", "EchoScrap/Character Contact Shadow");
            set.shadowMaterial.SetColor("_BaseColor", new Color(0.005f, 0.006f, 0.009f, 0.6f));
            var beacon = Material("Assets/_Project/Resources/Pickups/MedkitBeacon.mat", "EchoScrap/Pickup Beacon");
            beacon.SetColor("_BaseColor", new Color(0.22f, 1f, 0.38f, 0.65f));
            EditorUtility.SetDirty(set); EditorUtility.SetDirty(set.material); EditorUtility.SetDirty(set.shadowMaterial); EditorUtility.SetDirty(beacon);
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] Reactor moth connected: 62 frames, 9 clips, normals/emission, pooled Burst, and green medkit beacon.");
        }

        private static void Import(string name, bool normal, bool srgb)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Art + name);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb; importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
            if (normal) importer.flipGreenChannel = false;
            importer.SaveAndReimport();
        }
        private static T Asset<T>(string path) where T : ScriptableObject
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null) { value = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(value, path); }
            return value;
        }
        private static Material Material(string path, string shader)
        {
            Material value = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (value == null)
            {
                Shader found = Shader.Find(shader); if (found == null) throw new BuildFailedException("Missing shader: " + shader);
                value = new Material(found); AssetDatabase.CreateAsset(value, path);
            }
            return value;
        }
        private static Mesh Quad(string path, Rect rect)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear(); mesh.vertices = new[] { new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMax, rect.yMin), new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMax, rect.yMax) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one }; mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
            mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); return mesh;
        }
    }
}
