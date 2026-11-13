using System.IO;
using EchoScrap.Combat;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace EchoScrap.Editor
{
    public static class PauseMenuAuthoring
    {
        [MenuItem("EchoScrap/Art/Prepare Pause Menu")]
        public static void Prepare()
        {
            const string resource = "Assets/_Project/Resources/UI/PauseMenuSkin.asset";
            Directory.CreateDirectory(Path.GetDirectoryName(resource));
            AssetDatabase.Refresh();
            var skin = AssetDatabase.LoadAssetAtPath<PauseMenuSkin>(resource);
            if (skin == null)
            {
                skin = ScriptableObject.CreateInstance<PauseMenuSkin>();
                AssetDatabase.CreateAsset(skin, resource);
            }
            skin.frame = Import("PauseFrame_Sliced", new Vector4(40,14,18,24));
            skin.topCrest = Import("PauseCrestTop",Vector4.zero);
            skin.bottomCrest = Import("PauseCrestBottom",Vector4.zero);
            skin.sideOrnament = Import("PauseSideOrnament",Vector4.zero);
            skin.blueButton = Import("Button_Blue", Vector4.one * 14f);
            skin.redButton = Import("Button_Red", Vector4.one * 14f);
            skin.greenButton = Import("Button_Green", Vector4.one * 14f);
            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] Delivered pause frame and three navigation button colors prepared.");
        }

        private static Sprite Import(string name, Vector4 border)
        {
            string path = "Assets/_Project/Art/UI/PauseMenu/" + name + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new BuildFailedException("Missing pause artwork: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = border;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
