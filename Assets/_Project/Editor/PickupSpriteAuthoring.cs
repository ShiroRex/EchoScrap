using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace EchoScrap.Editor
{
    public static class PickupSpriteAuthoring
    {
        private const string ArtPath = "Assets/_Project/Art/Items/Botiquin/";
        private const string ResourcePath = "Assets/_Project/Resources/Pickups/";

        [MenuItem("EchoScrap/Art/Connect Medkit Sprite")]
        public static void Prepare()
        {
            Directory.CreateDirectory(ResourcePath);
            AssetDatabase.Refresh();
            Import(ArtPath + "Botiquin_Apollo.png", false);
            Import(ArtPath + "Botiquin_Apollo_Normal.png", true);
            Material sprite = Material("Botiquin", "EchoScrap/Character Pixel Lit");
            sprite.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath + "Botiquin_Apollo.png"));
            sprite.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath + "Botiquin_Apollo_Normal.png"));
            sprite.SetVector("_FrameRect", new Vector4(0, 0, 1, 1));
            sprite.SetColor("_BaseColor", Color.white);
            sprite.SetColor("_EmissionColor", Color.clear);
            sprite.SetFloat("_NormalStrength", 1f);
            Material shadow = Material("BotiquinShadow", "EchoScrap/Character Contact Shadow");
            shadow.SetColor("_BaseColor", new Color(0.005f, 0.006f, 0.009f, 0.7f));
            EditorUtility.SetDirty(sprite); EditorUtility.SetDirty(shadow);
            AssetDatabase.SaveAssets();
            Debug.Log("[EchoScrap] Apollo medkit ready: 32x32 color, OpenGL normals, lit material and contact shadow.");
        }

        private static void Import(string path, bool normal)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new BuildFailedException("Missing medkit texture: " + path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Sprite;
            importer.sRGBTexture = !normal;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            if (normal) importer.flipGreenChannel = false;
            else { importer.spriteImportMode = SpriteImportMode.Single; importer.spritePixelsPerUnit = 32; }
            importer.SaveAndReimport();
        }

        private static Material Material(string name, string shaderName)
        {
            string path = ResourcePath + name + ".mat";
            Material result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result != null) return result;
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new BuildFailedException("Missing pickup shader: " + shaderName);
            result = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(result, path);
            return result;
        }
    }
}
