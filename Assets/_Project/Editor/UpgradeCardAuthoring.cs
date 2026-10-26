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
            File.Copy("Assets/_Project/Art/UI/Fonts/ChakraPetch-OFL.txt", "Assets/StreamingAssets/Licenses/ChakraPetch-OFL.txt", true);
            Directory.CreateDirectory(Path.GetDirectoryName(Resource)); AssetDatabase.Refresh();
            UpgradeCardSkin skin = AssetDatabase.LoadAssetAtPath<UpgradeCardSkin>(Resource);
            if (skin == null) { skin = ScriptableObject.CreateInstance<UpgradeCardSkin>(); AssetDatabase.CreateAsset(skin, Resource); }
            skin.frames = new Sprite[4]; skin.icons = new Sprite[System.Enum.GetValues(typeof(UpgradeIcon)).Length]; skin.litFrames = new Material[4];
            Shader frameShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/UpgradeCardFrameLit.shader");
            if (frameShader == null) throw new BuildFailedException("Missing frame lighting shader.");
            string[] rarities = { "Common", "Rare", "Epic", "Legendary" };
            for (int i = 0; i < rarities.Length; i++)
            {
                skin.frames[i] = Import("Frame_" + rarities[i], 12);
                string normalPath = Art + "Frame_" + rarities[i] + "_Normal.png";
                var normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
                if (normalImporter == null) throw new BuildFailedException("Missing frame normal: " + normalPath);
                normalImporter.textureType = TextureImporterType.Default; normalImporter.sRGBTexture = false;
                normalImporter.filterMode = FilterMode.Point; normalImporter.mipmapEnabled = false;
                normalImporter.textureCompression = TextureImporterCompression.Uncompressed;
                normalImporter.npotScale = TextureImporterNPOTScale.None; normalImporter.wrapMode = TextureWrapMode.Clamp;
                normalImporter.SaveAndReimport();
                string path = "Assets/_Project/Resources/UI/Frame_" + rarities[i] + ".mat";
                Material lit = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (lit == null) { lit = new Material(frameShader); AssetDatabase.CreateAsset(lit, path); }
                lit.shader = frameShader; lit.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
                lit.SetColor("_SpecularColor", Color.Lerp(UpgradeCatalog.RarityColor((UpgradeRarity)i), Color.white, 0.5f));
                skin.litFrames[i] = lit; EditorUtility.SetDirty(lit);
            }
            for (int i = 0; i < skin.icons.Length; i++) skin.icons[i] = Import("Grayscale/Icon_" + (UpgradeIcon)i);
            skin.upgradeArrow = Import("UpgradeArrow");
            skin.chooseButton = Import("Grayscale/UpgradeButton", new Vector4(6f, 4f, 6f, 4f));
            skin.regular = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/Art/UI/Fonts/ChakraPetch-Regular.ttf");
            skin.semibold = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/Art/UI/Fonts/ChakraPetch-SemiBold.ttf");
            if (skin.regular == null || skin.semibold == null) throw new BuildFailedException("Chakra Petch font assets are required.");
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
            Debug.Log("[EchoScrap] Upgrade cards: four lit 9-sliced frames, ten grayscale icons and Chakra Petch ready.");
        }
        private static Sprite Import(string name, int border = 0) => Import(name, Vector4.one * border);

        private static Sprite Import(string name, Vector4 border)
        {
            string path = Art + name + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new BuildFailedException("Missing card art: " + path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100; importer.spriteBorder = border;
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
