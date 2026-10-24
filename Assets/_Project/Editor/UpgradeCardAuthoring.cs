using System.IO;
using EchoScrap.Combat;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace EchoScrap.Editor
{
    public static class UpgradeCardAuthoring
    {
        private const string Art = "Assets/_Project/Art/UI/UpgradeCards/";
        private const string Resource = "Assets/_Project/Resources/UI/UpgradeCardSkin.asset";
        [MenuItem("EchoScrap/Art/Prepare Upgrade Cards")]
        public static void Prepare()
        {
            Directory.CreateDirectory("Assets/StreamingAssets/Licenses");
            File.Copy("Assets/_Project/Art/UI/Fonts/OFL.txt", "Assets/StreamingAssets/Licenses/PixelifySans-OFL.txt", true);
            Directory.CreateDirectory(Path.GetDirectoryName(Resource)); AssetDatabase.Refresh();
            UpgradeCardSkin skin = AssetDatabase.LoadAssetAtPath<UpgradeCardSkin>(Resource);
            if (skin == null) { skin = ScriptableObject.CreateInstance<UpgradeCardSkin>(); AssetDatabase.CreateAsset(skin, Resource); }
            skin.frames = new Sprite[4]; skin.icons = new Sprite[8];
            string[] rarities = { "Common", "Rare", "Epic", "Legendary" };
            for (int i = 0; i < rarities.Length; i++)
            {
                skin.frames[i] = Import("Frame_" + rarities[i], 12);
            }
            for (int i = 0; i < skin.icons.Length; i++) skin.icons[i] = Import("Icon_" + (UpgradeIcon)i);
            skin.upgradeArrow = Import("UpgradeArrow");
            skin.regular = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/Art/UI/Fonts/PixelifySans-Regular.ttf");
            skin.semibold = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/Art/UI/Fonts/PixelifySans-SemiBold.ttf");
            if (skin.regular == null || skin.semibold == null) throw new BuildFailedException("Pixelify Sans font assets are required.");
            const string sheenPath = "Assets/_Project/Resources/UI/UpgradeCardSheen.mat";
            Shader sheenShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/UpgradeCardSheen.shader");
            if (sheenShader == null) throw new BuildFailedException("Missing card surface sheen shader.");
            skin.surfaceSheen = AssetDatabase.LoadAssetAtPath<Material>(sheenPath);
            if (skin.surfaceSheen == null)
            {
                skin.surfaceSheen = new Material(sheenShader);
                AssetDatabase.CreateAsset(skin.surfaceSheen, sheenPath);
            }
            skin.surfaceSheen.shader = sheenShader;
            EditorUtility.SetDirty(skin.surfaceSheen);
            EditorUtility.SetDirty(skin); AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] Upgrade cards: four 9-sliced frames, eight Apollo icons and Pixelify Sans ready.");
        }
        private static Sprite Import(string name, int border = 0)
        {
            string path = Art + name + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new BuildFailedException("Missing card art: " + path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100; importer.spriteBorder = Vector4.one * border;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
            importer.alphaIsTransparency = true; importer.sRGBTexture = true;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
