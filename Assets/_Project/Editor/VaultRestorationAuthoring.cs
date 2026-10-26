using System.Collections.Generic;
using System.IO;
using EchoScrap.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace EchoScrap.Editor
{
    public static class VaultRestorationAuthoring
    {
        public static Bounds GeometryBounds(Transform root)
        {
            Bounds result=default; bool found=false;
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.sharedMesh==null) continue;
                foreach(var vertex in filter.sharedMesh.vertices)
                {
                    var p=filter.transform.TransformPoint(vertex);
                    if(!found) { result=new Bounds(p,Vector3.zero); found=true; } else result.Encapsulate(p);
                }
            }
            return result;
        }
        public static void RepairMap()
        {
            var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/CombatArena.unity");
            ArenaReliefRepairAuthoring.RebuildGround();
            int moved=0;
            foreach(Transform group in GameObject.Find("Mundo3D_EchoScrap").transform)
            {
                if(group.name=="Portal" || group.name=="Emblema_triangular" || group.name.StartsWith("Portal")) continue;
                foreach(Transform anchor in group)
                {
                    if(anchor.GetComponentsInChildren<MeshFilter>().Length==0) continue;
                    Bounds b=GeometryBounds(anchor);
                    if(anchor.name=="arbol_seco_0_30") { anchor.localScale*=12f/b.size.y; b=GeometryBounds(anchor); }
                    float support=ArenaSurface.Profile(b.center.x,b.center.z,200);
                    for(int i=0;i<4;i++) support=Mathf.Min(support,ArenaSurface.Profile(i%2==0?b.min.x:b.max.x,i<2?b.min.z:b.max.z,200));
                    float delta=support-b.min.y;
                    if(Mathf.Abs(delta)>0.01f) { anchor.position+=Vector3.up*delta; moved++; }
                    foreach(var c in anchor.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                    var old=anchor.Find("CollisionBoxes"); if(old!=null) Object.DestroyImmediate(old.gameObject);
                    if(!(anchor.name.StartsWith("roca_baja") && Mathf.Max(b.size.x,b.size.z)<=1.65f && b.size.y<=1f)) ArenaReliefRepairAuthoring.FitBoxes(anchor);
                }
            }
            // Conservar el acceso y sus colisiones probadas: solo se descubre al excavar.
            Physics.SyncTransforms(); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("RESTORE_MAP_OK grounded="+moved);
        }
        public static void Audit()
        {
            var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/CombatArena.unity");
            var lines=new List<string>();
            foreach(var root in scene.GetRootGameObjects()) lines.Add("ROOT "+root.name+" "+root.transform.position);
            var passage=GameObject.Find("WarehouseConnection");
            if(passage!=null) foreach(var t in passage.GetComponentsInChildren<Transform>(true))
                if(t.parent==passage.transform || t.name=="EscaleraImportada") lines.Add("ACCESS "+t.name+" "+t.position+" active="+t.gameObject.activeSelf);
            var world=GameObject.Find("Mundo3D_EchoScrap");
            foreach(Transform group in world.transform) foreach(Transform anchor in group)
            {
                var renderers=anchor.GetComponentsInChildren<MeshRenderer>(); if(renderers.Length==0) continue;
                var b=GeometryBounds(anchor);
                float h=ArenaSurface.Profile(b.center.x,b.center.z,200);
                lines.Add("PROP "+group.name+"/"+anchor.name+" pos="+anchor.position+" bottom="+b.min.y+" floor="+h+" gap="+(b.min.y-h));
            }
            Directory.CreateDirectory("Logs/VaultRestoration"); File.WriteAllLines("Logs/VaultRestoration/Audit.txt",lines);
        }
    }
}


