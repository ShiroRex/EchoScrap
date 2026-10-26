using System.Linq;
using EchoScrap.Combat;
using EchoScrap.Core;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace EchoScrap.Editor {
public static class GarageExpansionAuthoring {
const string Folder="Assets/_Project/Art/Boveda/Garage/";
public static void Apply() {
 var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/CombatArena.unity");
 var travel=Object.FindAnyObjectByType<WarehouseTravel>();
 var room=GameObject.Find("WarehouseConnection/Almacen").transform;
 var frame=room.Find("PortalRegreso"); var vortex=room.Find("PortalVortex");
 var b=VaultRestorationAuthoring.GeometryBounds(frame);float factor=9f/b.size.x;
 var relative=vortex.position-frame.position;frame.localScale*=factor;vortex.localScale*=factor;vortex.position=frame.position+relative*factor;
 b=VaultRestorationAuthoring.GeometryBounds(frame);var delta=new Vector3(180-b.center.x,-b.min.y,29.5f-b.center.z);frame.position+=delta;vortex.position+=delta;
 foreach(Transform child in room.Cast<Transform>().ToArray()) if(child!=frame && child!=vortex) Object.DestroyImmediate(child.gameObject);
 var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"Garage.fbx"));
 model.name="BovedaInterior";model.transform.SetParent(room,false);
 var markers=model.GetComponentsInChildren<Transform>();var mount=markers.First(t=>t.name=="PortalMount");
 if(mount.position.z<0) model.transform.Rotate(Vector3.up,180,Space.World);
 model.transform.position+=new Vector3(180,0,0)-markers.First(t=>t.name=="FloorOrigin").position;
 foreach(var r in model.GetComponentsInChildren<Renderer>()) {var mats=r.sharedMaterials;
 for(int i=0;i<mats.Length;i++) mats[i]=MaterialFor(mats[i].name);r.sharedMaterials=mats;}
 foreach(var f in model.GetComponentsInChildren<MeshFilter>()) {
 if(f.name.StartsWith("LosaInterior") || f.name.Contains("Guia") || f.name.Contains("Luz") || f.name.Contains("Marca") || f.name.Contains("Nombre") || f.name.Contains("Acceso")) continue;
 f.gameObject.AddComponent<MeshCollider>().sharedMesh=f.sharedMesh;
 }
 var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/Art/UI/Fonts/ChakraPetch-SemiBold.ttf");
 int index=0;
 foreach(var marker in markers.Where(t=>t.name.EndsWith("_Coleccionable")).OrderBy(t=>t.name)) {
 index++;var sign=new GameObject("NombreColeccionable_"+index).AddComponent<TextMesh>();sign.transform.SetParent(room,false);
 float side=marker.position.x<180?-1:1;
 sign.transform.position=new Vector3(180+side*22.4f,9,marker.position.z);sign.transform.rotation=Quaternion.Euler(0,side<0?-90:90,0);
 sign.font=font;sign.GetComponent<MeshRenderer>().sharedMaterial=font.material;sign.fontSize=64;sign.characterSize=.18f;sign.anchor=TextAnchor.MiddleCenter;sign.alignment=TextAlignment.Center;sign.text="ESPACIO "+index.ToString("00");sign.color=new Color(.6f,.9f,1);
 var light=new GameObject("IluminacionExpositor_"+index).AddComponent<Light>();light.transform.SetParent(room,false);light.transform.position=new Vector3(marker.position.x,6,marker.position.z);light.type=LightType.Point;light.range=15;light.intensity=7;light.color=new Color(.65f,.85f,1);
 }
 travel.WarehouseArrival.position=new Vector3(180,1,18);
 var stairs=Object.FindAnyObjectByType<WalkableStairs>();var back=new Vector3(70,0,-59.5f);float h;
 back.y=(stairs!=null && stairs.TryHeight(back,out h)?h:ArenaSurface.Profile(back.x,back.z,200))+1;travel.CombatArrival.position=back;
 var gate=Object.FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None).First(g=>g.FromWarehouse);gate.transform.position=new Vector3(180,2,26.5f);
 var bounds=new GameObject("LimitesCamaraInterior");bounds.transform.SetParent(room,false);bounds.transform.position=new Vector3(180,6,0);var volume=bounds.AddComponent<BoxCollider>();volume.isTrigger=true;volume.size=new Vector3(43,10,58);travel.InteriorCameraBounds=volume;
 foreach(float z in new[]{-24f,-8f,8f,24f}) {var light=new GameObject("LuzInterior").AddComponent<Light>();light.transform.SetParent(room,false);light.transform.position=new Vector3(180,10,z);light.type=LightType.Point;light.range=25;light.intensity=14;light.color=new Color(.9f,.94f,1);}
 var glow=new GameObject("LuzPortal").AddComponent<Light>();glow.transform.SetParent(room,false);glow.transform.position=new Vector3(180,4,28);glow.type=LightType.Point;glow.range=12;glow.intensity=8;glow.color=new Color(.55f,.15f,1);
 Physics.SyncTransforms();EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();Debug.Log("GARAGE_OK area=48x64 slots="+index+" return="+back);
}
static Material MaterialFor(string name) {
 string path=Folder+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);bool created=mat==null;
 if(created) mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.name=name;
 Color color=name=="Hormigon"?new Color(.23f,.27f,.31f):name=="Panel"?new Color(.105f,.16f,.2f):name=="Borde"?new Color(.45f,.32f,.12f):name=="Senal"?new Color(.95f,.56f,.08f):name=="Luz"?new Color(.48f,.85f,1):name=="Violeta"?new Color(.42f,.12f,.85f):name=="Suelo"?new Color(.13f,.17f,.2f):new Color(.055f,.075f,.1f);
 mat.color=color;mat.SetFloat("_Cull",0);mat.SetFloat("_Smoothness",.35f);
 if(name=="Luz"||name=="Violeta"){mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",color*3);}
 if(created) AssetDatabase.CreateAsset(mat,path);else EditorUtility.SetDirty(mat);return mat;
}
}}
