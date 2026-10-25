using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using EchoScrap.Core;
using EchoScrap.Combat;

namespace EchoScrap.Editor
{
    public static class TerrainCollisionAuthoring
    {
        private const string Folder="Assets/_Project/Art/Mundo3D/Materiales/";
        public static void Apply()
        {
            var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/CombatArena.unity");
            var floor=GameObject.Find("Arena_120m/Floor");
            const int cells=400, stride=401;
            var vertices=new Vector3[stride*stride]; var uv=new Vector2[vertices.Length];
            var triangles=new int[cells*cells*6];
            for(int z=0;z<=cells;z++) for(int x=0;x<=cells;x++)
            {
                float wx=x*0.5f-100, wz=z*0.5f-100; int i=z*stride+x;
                vertices[i]=new Vector3(wx/200f,ArenaSurface.Profile(wx,wz),wz/200f);
                uv[i]=new Vector2(wx,wz)/12f;
                if(x==cells || z==cells) continue;
                int t=(z*cells+x)*6;
                triangles[t]=i; triangles[t+1]=i+stride; triangles[t+2]=i+1;
                triangles[t+3]=i+1; triangles[t+4]=i+stride; triangles[t+5]=i+stride+1;
            }
            var mesh=SaveMesh("Relieve",vertices,triangles,uv);
            floor.transform.position=Vector3.zero;
            floor.transform.localScale=new Vector3(200,1,200);
            floor.GetComponent<MeshFilter>().sharedMesh=mesh;
            foreach(var c in floor.GetComponents<Collider>()) Object.DestroyImmediate(c);
            var ground=floor.AddComponent<MeshCollider>(); ground.sharedMesh=mesh;
            if(floor.GetComponent<ArenaSurface>()==null) floor.AddComponent<ArenaSurface>();
            var groundMaterial=AssetDatabase.LoadAssetAtPath<Material>(Folder+"Relieve.mat");
            if(groundMaterial==null)
            {
                groundMaterial=new Material(Shader.Find("EchoScrap/WastelandGround"));
                AssetDatabase.CreateAsset(groundMaterial,Folder+"Relieve.mat");
            }
            floor.GetComponent<Renderer>().sharedMaterial=groundMaterial;
            var bootstrap=Object.FindAnyObjectByType<CombatGameBootstrap>();
            var serialized=new SerializedObject(bootstrap);
            serialized.FindProperty("arenaGround").objectReferenceValue=ground;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var world=GameObject.Find("Mundo3D_EchoScrap");
            // Retirar las cajas aproximadas: el hueco de una puerta o de un árbol queda libre.
            foreach(var c in world.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            int colliders=0;
            foreach(var filter in world.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.name=="PortalVortex" || filter.sharedMesh==null) continue;
                var collision=filter.gameObject.AddComponent<MeshCollider>();
                collision.sharedMesh=filter.sharedMesh; collision.convex=false;
                colliders++;
            }
            foreach(Transform group in world.transform)
            {
                if(group.name=="Emblema_triangular" || group.name=="PortalVortex" || group.name=="PortalGlow") continue;
                foreach(Transform anchor in group)
                {
                    var renderers=anchor.GetComponentsInChildren<Renderer>();
                    if(renderers.Length==0) continue;
                    var bounds=renderers[0].bounds;
                    foreach(var r in renderers) bounds.Encapsulate(r.bounds);
                    // Dejar un paso continuo visible de seis metros junto al perímetro.
                    Vector3 shift=Vector3.zero;
                    if(bounds.max.x>93) shift.x=93-bounds.max.x;
                    else if(bounds.min.x< -93) shift.x=-93-bounds.min.x;
                    if(bounds.max.z>93) shift.z=93-bounds.max.z;
                    else if(bounds.min.z< -93) shift.z=-93-bounds.min.z;
                    anchor.position+=shift; bounds.center+=shift;
                    float y=ArenaSurface.Profile(anchor.position.x,anchor.position.z);
                    for(int i=0;i<4;i++) y=Mathf.Min(y,ArenaSurface.Profile(i%2==0?bounds.min.x:bounds.max.x,i<2?bounds.min.z:bounds.max.z));
                    anchor.position+=Vector3.up*(y-bounds.min.y);
                }
            }
            // Los límites antes invisibles ahora tienen la misma geometría visible y física.
            BuildBoundary("Wall_North",new Vector2(-100,100),new Vector2(100,100));
            BuildBoundary("Wall_South",new Vector2(100,-100),new Vector2(-100,-100));
            BuildBoundary("Wall_East",new Vector2(100,100),new Vector2(100,-100));
            BuildBoundary("Wall_West",new Vector2(-100,-100),new Vector2(-100,100));
            bootstrap.WaveSettings.ArenaHalfExtents=new Vector2(100,100);
            EditorUtility.SetDirty(bootstrap.WaveSettings);
            Physics.SyncTransforms();
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("TERRAIN_COLLISION_OK meshColliders="+colliders+" triangles="+triangles.Length/3);
        }

        private static void BuildBoundary(string name,Vector2 from,Vector2 to)
        {
            var go=GameObject.Find("Arena_120m/"+name);
            go.transform.position=Vector3.zero; go.transform.rotation=Quaternion.identity; go.transform.localScale=Vector3.one;
            var vertices=new List<Vector3>(); var triangles=new List<int>();
            Vector2 inward=new Vector2(-(to-from).y,(to-from).x).normalized*0.35f;
            for(int i=0;i<120;i++)
            {
                Vector2 a=Vector2.Lerp(from,to,i/120f), b=Vector2.Lerp(from,to,(i+1)/120f);
                float ah=ArenaSurface.Profile(a.x,a.y), bh=ArenaSurface.Profile(b.x,b.y);
                int k=vertices.Count;
                vertices.Add(new Vector3(a.x,ah-0.15f,a.y)); vertices.Add(new Vector3(b.x,bh-0.15f,b.y));
                vertices.Add(new Vector3(a.x,ah+2.7f,a.y)); vertices.Add(new Vector3(b.x,bh+2.7f,b.y));
                vertices.Add(new Vector3(a.x+inward.x,ah+2.7f,a.y+inward.y));
                vertices.Add(new Vector3(b.x+inward.x,bh+2.7f,b.y+inward.y));
                triangles.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3,k+2,k+4,k+3,k+3,k+4,k+5});
                // Reverso: la barrera también es sólida desde fuera.
                triangles.AddRange(new[]{k,k+1,k+2,k+1,k+3,k+2});
            }
            var mesh=SaveMesh(name,vertices.ToArray(),triangles.ToArray(),null);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            foreach(var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
            var border=go.AddComponent<BoxCollider>();
            border.center=mesh.bounds.center; border.size=mesh.bounds.size;
            var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"MuroPerimetral.mat");
            if(material==null)
            {
                material=new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color=new Color(0.28f,0.23f,0.17f);
                material.SetFloat("_Smoothness",0.08f); AssetDatabase.CreateAsset(material,Folder+"MuroPerimetral.mat");
            }
            go.GetComponent<Renderer>().sharedMaterial=material; go.GetComponent<Renderer>().enabled=true;
        }

        private static Mesh SaveMesh(string name,Vector3[] vertices,int[] triangles,Vector2[] uv)
        {
            string path=Folder+name+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null) { mesh=new Mesh(); AssetDatabase.CreateAsset(mesh,path); }
            mesh.Clear(); mesh.name=name; mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices=vertices; mesh.triangles=triangles; if(uv!=null) mesh.uv=uv;
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); return mesh;
        }
    }
}
