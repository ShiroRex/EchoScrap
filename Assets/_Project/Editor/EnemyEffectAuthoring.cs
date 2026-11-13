using System;
using System.IO;
using EchoScrap.Combat;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace EchoScrap.Editor
{
    public static class EnemyEffectAuthoring
    {
        private const string Art = "Assets/_Project/Art/VFX/Enemies/";
        [Serializable] private sealed class Manifest { public ExportFrame[] frames; public Meta meta; }
        [Serializable] private sealed class ExportFrame { public RectData frame; public int duration; public bool rotated, trimmed; }
        [Serializable] private sealed class RectData { public int x, y, w, h; }
        [Serializable] private sealed class Meta { public Tag[] frameTags; public RectData size; }
        [Serializable] private sealed class Tag { public string name; public int from, to; }

        [MenuItem("EchoScrap/Art/Connect Enemy Effect Sprites")]
        public static void Prepare()
        {
            AssetDatabase.Refresh();
            const string path = "Assets/_Project/Resources/Enemies/EnemyEffects.asset";
            Directory.CreateDirectory("Assets/_Project/Resources/Enemies");
            Directory.CreateDirectory(Art + "Data"); AssetDatabase.Refresh();
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText("ArtSource/VFX/Enemies/EnemyEffects_Atlas.json"));
            if (manifest.frames.Length != 120 || manifest.meta.frameTags.Length != 15 ||
                manifest.meta.size.w != 512 || manifest.meta.size.h != 960)
                throw new BuildFailedException("Enemy effects require 120 cells of 64x64, 15 tags, and a 512x960 atlas.");
            Import("EnemyEffects_Atlas.png", true); Import("EnemyEffects_Emission.png", false);
            var set = AssetDatabase.LoadAssetAtPath<EnemyEffectLibrary>(path);
            if (set == null) { set = ScriptableObject.CreateInstance<EnemyEffectLibrary>(); AssetDatabase.CreateAsset(set, path); }
            set.clips = new EnemyEffectClip[15];
            var sizes = new[] { 1.4f, 1.3f, 1.5f, 2f, 2.6f, 3f, 9f, 1.2f, 1.6f, 2f, 4f, 2.3f, 6f, 5f, 12f };
            for (int c = 0; c < 15; c++)
            {
                string name = ((EnemyEffectId)c).ToString(); Tag tag = manifest.meta.frameTags[c];
                if (tag.name != name || tag.from != c * 8 || tag.to != c * 8 + 7)
                    throw new BuildFailedException("Unexpected enemy effect tag " + tag.name);
                bool loop = c == 3 || c == 7 || c == 11 || c == 13;
                var clip = new EnemyEffectClip { name = name, size = Vector2.one * sizes[c], ground = c == 5 || c == 6 || c == 13 || c == 14,
                    glow = c == 4 || c == 10 ? 0f : c == 7 || c >= 11 ? .9f : .35f,
                    animation = new CharacterSpriteSet.Clip { name = name, loop = loop, frames = new CharacterSpriteSet.Frame[8] } };
                if (c == 7 || c == 11) clip.pivot = new Vector2(43f / 64f, .5f);
                for (int f = 0; f < 8; f++)
                {
                    var item = manifest.frames[c * 8 + f]; var r = item.frame;
                    if (r.w != 64 || r.h != 64 || r.x != f * 64 || r.y != c * 64 || item.rotated || item.trimmed || item.duration <= 0)
                        throw new BuildFailedException("Enemy effect cells must retain Aseprite geometry and positive timings.");
                    clip.animation.frames[f] = new CharacterSpriteSet.Frame { duration = item.duration / 1000f,
                        uvRect = new Vector4(r.x / 512f, 1f - (r.y + 64) / 960f, 64f / 512f, 64f / 960f) };
                }
                set.clips[c] = clip;
            }
            string matPath = Art + "Data/EnemyEffects.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null) { material = new Material(Shader.Find("EchoScrap/Mortar Acid")); AssetDatabase.CreateAsset(material, matPath); }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "EnemyEffects_Atlas.png"));
            material.SetTexture("_GlowMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "EnemyEffects_Emission.png"));
            material.SetColor("_GlowColor", new Color(.5f, .5f, .5f));
            material.SetColor("_BaseColor", Color.white); material.SetFloat("_GlowStrength", .4f);
            set.material = material;
            string meshPath = Art + "Data/EnemyEffects_Quad.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null) { mesh = new Mesh { name = "Enemy effect centered quad" }; AssetDatabase.CreateAsset(mesh, meshPath); }
            mesh.Clear(); mesh.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(-.5f, .5f), new Vector3(.5f, .5f) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 }; mesh.RecalculateBounds(); set.quad = mesh;
            EditorUtility.SetDirty(set); EditorUtility.SetDirty(material); EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] Enemy effects connected: 15 clips, 120 Aseprite frames, emission, terrain projection and copper missile lighting.");
        }
        private static void Import(string name, bool srgb)
        {
            if (!(AssetImporter.GetAtPath(Art + name) is TextureImporter importer)) throw new BuildFailedException("Missing enemy atlas " + name);
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = srgb;
            importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = 2048;
            importer.alphaIsTransparency = srgb; importer.SaveAndReimport();
        }
    }
}
