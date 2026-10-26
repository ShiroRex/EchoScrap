using System.Linq;
using EchoScrap.Combat;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace EchoScrap.Editor {
public static class VaultInteriorAuthoring {
const string Folder="Assets/_Project/Art/Boveda/Interior/";
public static void Apply() {
 var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/CombatArena.unity");
 var travel=Object.FindAnyObjectByType<WarehouseTravel>();
 var room=GameObject.Find("WarehouseConnection/Almacen").transform;
 var frame=room.Find("PortalRegreso"); var vortex=room.Find("PortalVortex");
 var current=VaultRestorationAuthoring.GeometryBounds(frame); var delta=new Vector3(180-current.center.x,-current.min.y,15.6f-current.center.z); frame.position+=delta; vortex.position+=delta;
 foreach(Transform child in room.Cast<Transform>().ToArray())
  if(child!=frame && child!=vortex) Object.DestroyImmediate(child.gameObject);
 var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"Boveda.fbx"));
 model.name="BovedaInterior"; model.transform.SetParent(room,false);
 var mount=model.GetComponentsInChildren<Transform>().First(t=>t.name=="PortalMount");
 if(mount.position.z<0) model.transform.Rotate(Vector3.up,180,Space.World);
 var origin=model.GetComponentsInChildren<Transform>().First(t=>t.name=="FloorOrigin");
 model.transform.position+=new Vector3(180,0,0)-origin.position;
 foreach(var r in model.GetComponentsInChildren<Renderer>()) {
 var mats=r.sharedMaterials;
 for(int i=0;i<mats.Length;i++) {
 string name=mats[i].name; string path=Folder+name+".mat";
 var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
 if(mat==null) {
 mat=new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.name=name;
 mat.SetFloat("_Cull",0);
 mat.color=name.Contains("Hierro")?new Color(.12f,.18f,.2f):name.Contains("Remate")?new Color(.53f,.36f,.17f):new Color(.42f,.43f,.42f);
 if(name=="Material_0") {mat.color=Color.white;mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"Texturas/2_base.jpg"));}
 if(name.Contains("Cristal")) {mat.color=new Color(.3f,.65f,.75f,.12f);mat.SetFloat("_Surface",1);mat.SetFloat("_ZWrite",0);mat.SetFloat("_SrcBlend",5);mat.SetFloat("_DstBlend",10);mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");mat.renderQueue=3000;}
 AssetDatabase.CreateAsset(mat,path);
 }
 mats[i]=mat;
 } r.sharedMaterials=mats;
 }
 // Exact mesh surfaces retain the portal opening and the empty display interiors.
 foreach(var f in model.GetComponentsInChildren<MeshFilter>()) {
 if(f.name.Contains("Nombre") || f.name=="TechoInterior" || f.name.StartsWith("LosaInterior")) continue;
 f.gameObject.AddComponent<MeshCollider>().sharedMesh=f.sharedMesh;
 }
 travel.WarehouseArrival.position=new Vector3(180,1,9);
 var gate=Object.FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None).First(g=>g.FromWarehouse);
 gate.transform.position=new Vector3(180,2,12.8f);
 var bounds=new GameObject("LimitesCamaraInterior");bounds.transform.SetParent(room,false);bounds.transform.position=new Vector3(180,4.5f,0);
 var box=bounds.AddComponent<BoxCollider>();box.size=new Vector3(25,7,32);box.isTrigger=true;travel.InteriorCameraBounds=box;
 foreach(float z in new[]{-12f,-4f,4f,12f}) {var light=new GameObject("LuzInterior").AddComponent<Light>();light.transform.SetParent(room,false);light.transform.position=new Vector3(180,6,z);light.type=LightType.Point;light.range=14;light.intensity=15;light.color=new Color(1,.8f,.6f);}
 var glow=new GameObject("LuzPortal").AddComponent<Light>();glow.transform.SetParent(room,false);glow.transform.position=new Vector3(180,3,14);glow.type=LightType.Point;glow.range=12;glow.intensity=5;glow.color=new Color(.5f,.1f,1);
 Physics.SyncTransforms();EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();Debug.Log("VAULT_INTERIOR_OK slots=8 mount="+mount.position);
}
public static void RestoreAll() {VaultRestorationAuthoring.RepairMap();Apply();}
}}


