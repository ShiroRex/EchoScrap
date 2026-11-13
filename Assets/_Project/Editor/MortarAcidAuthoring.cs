using System.IO;
using EchoScrap.Combat;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace EchoScrap.Editor
{
    public static class MortarAcidAuthoring
    {
        private const string Art = "Assets/_Project/Art/Characters/Mortero/";
        private const string Data = Art + "Data/";
        private const string SetPath = "Assets/_Project/Resources/Characters/MorteroAcid.asset";

        [MenuItem("EchoScrap/Art/Connect Mortar Acid")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Data);
            Directory.CreateDirectory("Assets/_Project/Resources/Characters");
            AssetDatabase.Refresh();
            Import("AcidProjectile.png", 64, 16, true);
            Import("AcidProjectile_Emission.png", 64, 16, false);
            Import("AcidImpact.png", 192, 32, true);
            Import("AcidImpact_Emission.png", 192, 32, false);
            Import("AcidPuddle.png", 256, 64, true);
            Import("AcidPuddle_Emission.png", 256, 64, false);
            var set = AssetDatabase.LoadAssetAtPath<MortarAcidSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<MortarAcidSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }
            set.projectileMaterial = Material("AcidProjectile", MortarAcidSet.ProjectileFrames, 0.85f);
            set.impactMaterial = Material("AcidImpact", MortarAcidSet.ImpactFrames, 0.7f);
            set.puddleMaterial = Material("AcidPuddle", MortarAcidSet.PuddleFrames, 0.45f);
            set.projectileSize = 0.64f;
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] Mortar acid connected: drawn projectile, impact and puddle, linear emission masks, procedural danger borders.");
        }

        private static void Import(string name, int width, int height, bool srgb)
        {
            string path = Art + name;
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                throw new BuildFailedException("Missing Aseprite acid export: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = srgb;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture.width != width || texture.height != height)
                throw new BuildFailedException($"Acid export {name} must be {width}x{height}, got {texture.width}x{texture.height}.");
        }

        private static Material Material(string name, int frames, float glow)
        {
            string path = Data + name + ".mat";
            Material value = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("EchoScrap/Mortar Acid");
            if (shader == null) throw new BuildFailedException("Missing Mortar Acid shader.");
            if (value == null)
            {
                value = new Material(shader);
                AssetDatabase.CreateAsset(value, path);
            }
            value.shader = shader;
            value.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + name + ".png"));
            value.SetTexture("_GlowMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + name + "_Emission.png"));
            value.SetColor("_BaseColor", Color.white);
            value.SetColor("_GlowColor", new Color(0.25f, 0.7f, 0.08f));
            value.SetFloat("_GlowStrength", glow);
            value.SetVector("_FrameRect", MortarAcidSet.FrameRect(0, frames));
            EditorUtility.SetDirty(value);
            return value;
        }
    }
}
