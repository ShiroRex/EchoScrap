using System.Linq;
using EchoScrap.Combat;
using EchoScrap.Core;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace EchoScrap.Editor
{
    public static class FusionAuthoring
    {
        const string Folder="Assets/_Project/Art/Fusion/";
        public static void Apply()
        {
            foreach(string path in System.IO.Directory.GetFiles("Assets/_Project/Resources/Collectibles","*.png",System.IO.SearchOption.AllDirectories)){
                var importer=(TextureImporter)AssetImporter.GetAtPath(path.Replace('\\','/'));
                importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.filterMode=FilterMode.Point;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.npotScale=TextureImporterNPOTScale.None;importer.spritePixelsPerUnit=100;
                var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);settings.spriteAlignment=(int)SpriteAlignment.Custom;settings.spritePivot=new Vector2(.5f,0);importer.SetTextureSettings(settings);importer.SaveAndReimport();
            }
            foreach(string sceneName in new[]{"CombatArena","CombatArena_3Min","CombatArena_8Min"}){
            var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/"+sceneName+".unity");
            var old=GameObject.Find("FusionMachine");if(old!=null)Object.DestroyImmediate(old);
            var root=new GameObject("FusionMachine");var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"MaquinaFusion.fbx"));model.transform.SetParent(root.transform,false);
            var all=model.GetComponentsInChildren<Transform>();
            var interaction=all.First(t=>t.name=="Interaccion");if(interaction.position.z>0)model.transform.Rotate(Vector3.up,180,Space.World);
            root.transform.position=new Vector3(48,ArenaSurface.Profile(48,-32,200),-32);
            foreach(var r in model.GetComponentsInChildren<Renderer>()) {var materials=r.sharedMaterials;for(int i=0;i<materials.Length;i++)materials[i]=Mat(materials[i].name);r.sharedMaterials=materials;}
            foreach(var filter in model.GetComponentsInChildren<MeshFilter>())if(!filter.name.StartsWith("LamaPersiana")&&!filter.name.Contains("Guia"))filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
            ReserveSpace();
            root.AddComponent<FusionAura>();
            var header=new GameObject("RotuloSuperior").transform;header.SetParent(root.transform,false);header.localPosition=new Vector3(0,10,0);
            var station=root.AddComponent<FusionStation>();station.HeaderPoint=header;station.InteractionPoint=interaction;station.ResultPoint=all.First(t=>t.name=="Resultado");station.Door=all.First(t=>t.name=="PersianaMovil");
            station.DisplaySlots=GameObject.Find("BovedaInterior").GetComponentsInChildren<Transform>().Where(t=>t.name.EndsWith("_Coleccionable")).OrderBy(t=>t.name).ToArray();
            var light=new GameObject("LuzCamaraFusion").AddComponent<Light>();light.transform.SetParent(root.transform,false);light.transform.position=all.First(t=>t.name=="LuzResultado").position;light.type=LightType.Point;light.range=10;light.intensity=4;light.color=new Color(.6f,.2f,1);
            Physics.SyncTransforms();EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();Debug.Log("FUSION_AUTHORING_OK machine="+root.transform.position+" interaction="+interaction.position+" result="+station.ResultPoint.position+" slots="+station.DisplaySlots.Length);
        }
        }
        static void ReserveSpace()
        {
            var world=GameObject.Find("Mundo3D_EchoScrap").transform;
            var props=new System.Collections.Generic.List<Transform>();
            foreach(Transform group in world){if(group.name.StartsWith("Portal")||group.name=="Emblema_triangular")continue;foreach(Transform anchor in group)if(anchor.GetComponentsInChildren<MeshFilter>().Length>0)props.Add(anchor);}
            var reserved=new Bounds(new Vector3(48,5,-33),new Vector3(20,40,23));int moved=0;
            foreach(var prop in props){
                var b=VaultRestorationAuthoring.GeometryBounds(prop);if(!b.Intersects(reserved))continue;bool placed=false;
                for(int z=-15;z>=-80&&!placed;z-=13)for(int x=28;x>=-85&&!placed;x-=13){
                    var candidate=new Bounds(new Vector3(x,b.center.y,z),b.size+new Vector3(2,0,2));
                    if(candidate.Intersects(reserved)||props.Any(other=>other!=prop&&candidate.Intersects(VaultRestorationAuthoring.GeometryBounds(other))))continue;
                    prop.position+=new Vector3(x-b.center.x,0,z-b.center.z);
                    b=VaultRestorationAuthoring.GeometryBounds(prop);float ground=ArenaSurface.Profile(b.center.x,b.center.z,200);
                    for(int i=0;i<4;i++)ground=Mathf.Min(ground,ArenaSurface.Profile(i%2==0?b.min.x:b.max.x,i<2?b.min.z:b.max.z,200));
                    prop.position+=Vector3.up*(ground-b.min.y);placed=true;moved++;
                }
                if(!placed)throw new System.InvalidOperationException("No clear relocation found for "+prop.name);
            }
            Debug.Log("FUSION_SPACE_OK relocated="+moved);
        }
        static Material Mat(string name)
        {
            string path=Folder+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);bool created=mat==null;if(created)mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.name=name;
            mat.color=name=="AceroPersiana"?new Color(.5f,.53f,.57f):name=="InteriorFusion"?new Color(.045f,.055f,.08f):name=="LuzFusion"?new Color(.6f,.15f,1):Color.white;
            if(name=="Material_0")mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"Texturas/3_base.jpg"));
            if(name=="LuzFusion"){mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",mat.color*3);}
            mat.SetFloat("_Cull",0);mat.SetFloat("_Smoothness",0f);if(created)AssetDatabase.CreateAsset(mat,path);else EditorUtility.SetDirty(mat);return mat;
        }
    }
}

