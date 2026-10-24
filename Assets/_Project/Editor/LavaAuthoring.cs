using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EchoScrap.Editor
{
    public static class LavaAuthoring
    {
        private const string Folder="Assets/_Project/Art/Lava/";
        [System.Serializable] private class Maps { public Entry[] materials; }
        [System.Serializable] private class Entry { public string name, @base, normal, emission, metallic; }
        public static void Apply()
        {
            var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/CombatArena.unity");
            var maps=JsonUtility.FromJson<Maps>(File.ReadAllText(Folder+"Texturas/materials.json")).materials[0];
            foreach(string path in new[]{maps.@base,maps.normal,maps.emission,maps.metallic})
            {
                if(string.IsNullOrEmpty(path)) continue;
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.maxTextureSize=2048; importer.mipmapEnabled=true;
                if(path==maps.normal) importer.textureType=TextureImporterType.NormalMap;
                if(path==maps.metallic) importer.sRGBTexture=false;
                importer.SaveAndReimport();
            }
            var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"Lava.mat");
            if(material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,Folder+"Lava.mat"); }
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(maps.@base));
            material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(maps.normal));
            material.SetTexture("_EmissionMap",AssetDatabase.LoadAssetAtPath<Texture2D>(maps.emission));
            material.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(maps.metallic));
            material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_EMISSION"); material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetColor("_EmissionColor",new Color(2.8f,1.1f,0.35f)); material.SetFloat("_Smoothness",0.28f);
            material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            var old=GameObject.Find("LavaZones"); if(old!=null) Object.DestroyImmediate(old);
            var root=new GameObject("LavaZones");
            Vector3[] centers={new Vector3(24,0,-24),new Vector3(-21,0,26),new Vector3(8,0,43)};
            for(int i=0;i<centers.Length;i++)
            {
                var zone=new GameObject("Lava_"+(i+1)); zone.transform.SetParent(root.transform,false);
                zone.transform.position=ArenaSurface.OnGround(centers[i],0.035f);
                var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"Lava.fbx"));
                model.transform.SetParent(zone.transform,false);
                var renderer=model.GetComponentInChildren<Renderer>(); var bounds=renderer.bounds;
                float size= i==2 ? 7f : 6f;
                model.transform.localScale*=size/Mathf.Max(bounds.size.x,bounds.size.z);
                model.transform.localScale=Vector3.Scale(model.transform.localScale,new Vector3(1,0.25f,1));
                bounds=renderer.bounds;
                model.transform.position+=new Vector3(zone.transform.position.x-bounds.center.x,
                    zone.transform.position.y-bounds.min.y,zone.transform.position.z-bounds.center.z);
                foreach(var r in model.GetComponentsInChildren<Renderer>()) r.sharedMaterial=material;
                // El jugador puede pisar el magma: sin un collider sólido que lo bloquee.
                var hazard=zone.AddComponent<LavaHazard>();
                hazard.Radii=new Vector2(renderer.bounds.extents.x*0.82f,renderer.bounds.extents.z*0.82f);
                var light=zone.AddComponent<Light>(); light.type=LightType.Point; light.color=new Color(1f,0.19f,0.025f); light.range=8; light.intensity=2;
            }
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("LAVA_OK zones=3 damage=5 interval=1s");
        }
    }
}
