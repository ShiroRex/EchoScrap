using System;
using System.IO;
using EchoScrap.Combat;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace EchoScrap.Editor
{
    /// <summary>Reimport approved native enemy atlases without changing their pixels or timing.</summary>
    public static class EnemySpriteAuthoring
    {
        [Serializable] private sealed class Manifest { public ExportedFrame[] frames; public Meta meta; }
        [Serializable] private sealed class Meta { public RectData size; public Tag[] frameTags; }
        [Serializable] private sealed class Tag { public string name; public int from, to; }
        [Serializable] private sealed class RectData { public int x, y, w, h; }
        [Serializable] private sealed class ExportedFrame { public RectData frame; public int duration; public bool rotated, trimmed; }

        [MenuItem("EchoScrap/Art/Connect Skeleton And Wendigo")]
        public static void Prepare()
        {
            Build("Esqueleto", 144);
            Build("Wendigo", 200);
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] Skeleton and Wendigo connected with native frame durations and four directions.");
        }

        [MenuItem("EchoScrap/Art/Connect Interceptor Galgo")]
        public static void PrepareInterceptor()
        {
            Build("Interceptor", 152);
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] Interceptor Galgo connected with 20 clips and four directions.");
        }

        private static void Build(string name, int expectedFrames)
        {
            string art = "Assets/_Project/Art/Characters/" + name + "/";
            string dataPath = art + "Data/";
            string resource = "Assets/_Project/Resources/Characters/" + name + ".asset";
            Directory.CreateDirectory(dataPath);
            AssetDatabase.Refresh();
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText("ArtSource/Characters/" + name + "/" + name + "_Atlas.json"));
            if (manifest?.frames == null || manifest.frames.Length != expectedFrames || manifest.meta?.frameTags == null)
                throw new BuildFailedException("Missing native frames or Aseprite tags for " + name);
            int width = manifest.meta.size.w, height = manifest.meta.size.h;
            var importer = (TextureImporter)AssetImporter.GetAtPath(art + name + "_Atlas.png");
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(art + name + "_Atlas.png", ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(art + name + "_Atlas.png");
            if (atlas == null) throw new BuildFailedException("Could not load imported enemy atlas: " + art + name + "_Atlas.png");
            if (atlas.width != width || atlas.height != height) throw new BuildFailedException("Atlas was resized: " + name);
            var set = AssetDatabase.LoadAssetAtPath<EnemySpriteSet>(resource);
            if (set == null) { set = ScriptableObject.CreateInstance<EnemySpriteSet>(); AssetDatabase.CreateAsset(set, resource); }
            set.clips = new CharacterSpriteSet.Clip[32];
            foreach (var tag in manifest.meta.frameTags)
            {
                string[] words = tag.name.Split('_');
                if (words.Length != 2 || !Enum.TryParse(words[0], out EnemySpriteMotion motion) || !Enum.TryParse(words[1], out CharacterFacing facing))
                    throw new BuildFailedException("Unrecognized enemy animation: " + tag.name);
                var clip = new CharacterSpriteSet.Clip { name = tag.name, loop = motion == EnemySpriteMotion.Idle || motion == EnemySpriteMotion.Walk || motion == EnemySpriteMotion.Charge,
                    frames = new CharacterSpriteSet.Frame[tag.to - tag.from + 1] };
                for (int i = 0; i < clip.frames.Length; i++)
                {
                    var frame = manifest.frames[tag.from + i]; var r = frame.frame;
                    if (frame.rotated || frame.trimmed || r.w != 96 || r.h != 96 || frame.duration <= 0)
                        throw new BuildFailedException("Enemy frames require untrimmed 96px cells and positive durations.");
                    clip.frames[i] = new CharacterSpriteSet.Frame { duration = frame.duration / 1000f,
                        uvRect = new Vector4(r.x / (float)width, 1f - (r.y + r.h) / (float)height, r.w / (float)width, r.h / (float)height) };
                }
                set.clips[(int)facing * 8 + (int)motion] = clip;
            }
            for (int i = 0; i < 4; i++)
                foreach (var motion in name != "Wendigo" ? new[] { EnemySpriteMotion.Idle, EnemySpriteMotion.Walk, EnemySpriteMotion.Attack, EnemySpriteMotion.Hit, EnemySpriteMotion.Death }
                    : new[] { EnemySpriteMotion.Idle, EnemySpriteMotion.Walk, EnemySpriteMotion.Windup, EnemySpriteMotion.Charge, EnemySpriteMotion.Recovery, EnemySpriteMotion.Hit, EnemySpriteMotion.Death })
                    if (set.clips[i * 8 + (int)motion] == null) throw new BuildFailedException("Missing " + motion + " for " + name);
            set.pixelsPerUnit = 30f;
            set.contactFrame = 3;
            set.mesh = PixelCard(dataPath + name + "_Quad.asset", set.pixelsPerUnit);
            set.shadowMesh = Quad(dataPath + name + "_ShadowQuad.asset", new Rect(-.5f, -.28f, 1f, .56f));
            set.material = Material(dataPath + name + ".mat", "EchoScrap/Character Pixel Lit");
            set.material.SetTexture("_BaseMap", atlas);
            set.material.SetColor("_BaseColor", Color.white);
            set.material.SetColor("_EmissionColor", Color.clear);
            set.material.SetFloat("_NormalStrength", 0f);
            set.material.SetFloat("_GlowStrength", 0f);
            if (name == "Interceptor")
            {
                set.material.SetTexture("_BumpMap", ImportCompanionTexture(art + name + "_Normal.png", true));
                set.material.SetFloat("_NormalStrength", .55f);
                set.material.SetTexture("_GlowMap", ImportCompanionTexture(art + name + "_Emission.png", false));
                set.material.SetColor("_GlowColor", new Color(1.2f, .56f, .12f));
                set.material.SetFloat("_GlowStrength", .55f);
            }
            set.material.SetVector("_FrameRect", set.GetClip(EnemySpriteMotion.Idle, CharacterFacing.Down).frames[0].uvRect);
            set.shadowMaterial = Material(dataPath + name + "_Shadow.mat", "EchoScrap/Character Contact Shadow");
            set.shadowMaterial.SetColor("_BaseColor", new Color(.005f, .006f, .009f, .6f));
            EditorUtility.SetDirty(set); EditorUtility.SetDirty(set.material); EditorUtility.SetDirty(set.shadowMaterial);
        }

        private static Texture2D ImportCompanionTexture(string path, bool normal)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            if (normal) importer.flipGreenChannel = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material Material(string path, string shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var found = Shader.Find(shader);
            if (found == null) throw new BuildFailedException("Missing shader " + shader);
            material = new Material(found); AssetDatabase.CreateAsset(material, path); return material;
        }
        private static Mesh LoadMesh(string path)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear(); return mesh;
        }
        private static Mesh Quad(string path, Rect r)
        {
            var mesh = LoadMesh(path);
            mesh.vertices = new[] { new Vector3(r.xMin, r.yMin), new Vector3(r.xMax, r.yMin), new Vector3(r.xMin, r.yMax), new Vector3(r.xMax, r.yMax) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); return mesh;
        }
        private static Mesh PixelCard(string path, float ppu)
        {
            var mesh = LoadMesh(path);
            // Fixed proportions for the arena's authored 50-degree view: keep the 1.8m body
            // and capsule width while its projected pixel aspect matches the source drawing.
            float x = 48f / ppu * .64f, height = 80f / ppu, toe = 16f / ppu;
            // Below the foot pivot the drawing extends toward the viewer along the floor:
            // sword follow-throughs remain visible instead of disappearing under the terrain.
            mesh.vertices = new[] { new Vector3(-x, .015f, -toe * .84f), new Vector3(x, .015f, -toe * .84f),
                new Vector3(-x, .015f, 0f), new Vector3(x, .015f, 0f), new Vector3(-x, height), new Vector3(x, height) };
            float v = 16f / 96f;
            mesh.uv = new[] { Vector2.zero, Vector2.right, new Vector2(0f, v), new Vector2(1f, v), Vector2.up, Vector2.one };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1, 2, 4, 3, 4, 5, 3 };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
            mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); return mesh;
        }
    }
}
