using System.IO;
using EchoScrap.Combat;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace EchoScrap.Editor
{
    public static class MortarSpriteAuthoring
    {
        private const string Art = "Assets/_Project/Art/Characters/Mortero/";
        private const string Data = Art + "Data/";
        private const string Resource = "Assets/_Project/Resources/Characters/MorteroCampana.asset";
        private const int Width = 512, Height = 1280, Cell = 64;
        private static readonly int[] Counts = { 6, 6, 4, 4, 8 };

        [System.Serializable] private sealed class Manifest
        {
            public ExportedFrame[] frames;
            public ManifestMeta meta;
        }
        [System.Serializable] private sealed class ExportedFrame
        {
            public string filename;
            public CellRect frame;
            public int duration;
            public bool rotated, trimmed;
        }
        [System.Serializable] private sealed class CellRect { public int x, y, w, h; }
        [System.Serializable] private sealed class ManifestMeta
        {
            public FrameTag[] frameTags;
            public CellRect size;
        }
        [System.Serializable] private sealed class FrameTag { public string name; public int from, to; }

        [MenuItem("EchoScrap/Art/Connect Mortar Campana And Acid")]
        public static void PrepareAll()
        {
            Prepare();
            MortarAcidAuthoring.Prepare();
        }

        [MenuItem("EchoScrap/Art/Connect Mortar Campana")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Data);
            Directory.CreateDirectory(Path.GetDirectoryName(Resource));
            AssetDatabase.Refresh();
            Import("MorteroCampana_Atlas.png", false, true);
            Import("MorteroCampana_Normal.png", true, false);
            Import("MorteroCampana_Emission.png", false, false);
            string json = File.ReadAllText("ArtSource/Characters/Mortero/MorteroCampana_Atlas.json");
            Manifest manifest = JsonUtility.FromJson<Manifest>(json);
            if (manifest?.frames == null || manifest.frames.Length != 112 || manifest.meta?.frameTags == null
                || manifest.meta.frameTags.Length != 20)
                throw new BuildFailedException("Campana requires 112 real frames and 20 Aseprite tags.");
            if (manifest.meta.size == null || manifest.meta.size.w != Width || manifest.meta.size.h != Height)
                throw new BuildFailedException("Campana manifest atlas size must be 512x1280.");
            var set = AssetDatabase.LoadAssetAtPath<MortarSpriteSet>(Resource);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<MortarSpriteSet>();
                AssetDatabase.CreateAsset(set, Resource);
            }
            set.clips = new CharacterSpriteSet.Clip[20];
            int frame = 0;
            for (int c = 0; c < set.clips.Length; c++)
            {
                MortarMotion motion = (MortarMotion)(c % 5);
                string name = motion + "_" + (CharacterFacing)(c / 5);
                int count = Counts[(int)motion];
                FrameTag tag = manifest.meta.frameTags[c];
                if (tag == null || tag.name != name || tag.from != frame || tag.to != frame + count - 1)
                    throw new BuildFailedException("Invalid Campana tag: expected " + name);
                var clip = new CharacterSpriteSet.Clip { name = name, loop = motion == MortarMotion.Float,
                    frames = new CharacterSpriteSet.Frame[count] };
                for (int k = 0; k < count; k++, frame++)
                {
                    ExportedFrame exported = manifest.frames[frame];
                    CellRect rect = exported?.frame;
                    if (rect == null || rect.x != k * Cell || rect.y != c * Cell || rect.w != Cell || rect.h != Cell)
                        throw new BuildFailedException("Campana frames must occupy one 64px row per tag, without empty cells in the manifest.");
                    if (exported.rotated || exported.trimmed || exported.filename != name + "_" + k.ToString("D2"))
                        throw new BuildFailedException("Campana frames must retain their Aseprite names and untrimmed orientation.");
                    float duration = exported.duration / 1000f;
                    if (duration <= 0f) throw new BuildFailedException("Campana frame durations must be positive.");
                    clip.frames[k] = new CharacterSpriteSet.Frame
                    {
                        uvRect = new Vector4(rect.x / (float)Width, 1f - (rect.y + Cell) / (float)Height, Cell / (float)Width, Cell / (float)Height),
                        duration = duration
                    };
                }
                set.clips[c] = clip;
            }
            set.mesh = Quad(Data + "MorteroCampana_Quad.asset", new Rect(-1.2f, -1.6f, 2.4f, 3.2f));
            set.shadowMesh = Quad(Data + "MorteroCampana_ShadowQuad.asset", new Rect(-0.65f, -0.42f, 1.3f, 0.84f));
            set.material = Material(Data + "MorteroCampana.mat", "EchoScrap/Character Pixel Lit");
            set.material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "MorteroCampana_Atlas.png"));
            set.material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "MorteroCampana_Normal.png"));
            set.material.SetTexture("_GlowMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "MorteroCampana_Emission.png"));
            set.material.SetColor("_GlowColor", new Color(0.42f, 1.7f, 0.08f));
            set.material.SetFloat("_GlowStrength", 0.7f);
            set.material.SetColor("_BaseColor", Color.white);
            set.material.SetColor("_EmissionColor", Color.clear);
            set.material.SetVector("_FrameRect", set.clips[0].frames[0].uvRect);
            set.shadowMaterial = Material(Data + "MorteroCampana_Shadow.mat", "EchoScrap/Character Contact Shadow");
            set.shadowMaterial.SetColor("_BaseColor", new Color(0.005f, 0.006f, 0.009f, 0.6f));
            EditorUtility.SetDirty(set);
            EditorUtility.SetDirty(set.material);
            EditorUtility.SetDirty(set.shadowMaterial);
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] Mortar Campana connected: 112 frames, 20 clips, normals, emission and authored death.");
        }

        private static void Import(string file, bool normal, bool srgb)
        {
            var importer = AssetImporter.GetAtPath(Art + file) as TextureImporter;
            if (importer == null) throw new BuildFailedException("Missing Campana texture: " + file);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = srgb;
            if (normal) importer.flipGreenChannel = false;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + file);
            if (texture == null || texture.width != Width || texture.height != Height)
                throw new BuildFailedException("Campana atlases must all be 512x1280: " + file);
        }

        private static Material Material(string path, string shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader found = Shader.Find(shader);
            if (found == null) throw new BuildFailedException("Missing shader: " + shader);
            material = new Material(found);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Mesh Quad(string path, Rect rect)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear();
            mesh.vertices = new[] { new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMax, rect.yMin), new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMax, rect.yMax) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
