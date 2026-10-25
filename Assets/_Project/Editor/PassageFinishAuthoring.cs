using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EchoScrap.Editor
{
    /// <summary>Revestimiento modular del acceso sin cambiar su carril ni sus triggers.</summary>
    public static class PassageFinishAuthoring
    {
        private const string Folder="Assets/_Project/Art/Boveda/";
        private static Mesh block;
        private static Material[] stones;
        private static Material cap,iron,lamp;
        public static void Apply()
        {
            var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/CombatArena.unity");
            Decorate(); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("PASSAGE_FINISH_OK stone_walls caps pillars lanterns landing");
        }
        public static void Decorate()
        {
            var passage=GameObject.Find("WarehouseConnection/PasilloDescendente").transform;
            var old=passage.Find("AcabadoPiedra"); if(old!=null) Object.DestroyImmediate(old.gameObject);
            var root=new GameObject("AcabadoPiedra").transform; root.SetParent(passage,false);
            block=BlockMesh(); stones=new Material[5];
            for(int i=0;i<stones.Length;i++) stones[i]=Material("PiedraGris_"+i,new Color(0.30f+i*0.025f,0.32f+i*0.025f,0.35f+i*0.025f));
            cap=Material("RemateCaliza",new Color(0.53f,0.49f,0.40f));
            iron=Material("RefuerzoHierro",new Color(0.11f,0.13f,0.15f));
            lamp=Material("LuzAmbar",new Color(1,0.55f,0.10f)); lamp.EnableKeyword("_EMISSION");
            lamp.SetColor("_EmissionColor",new Color(4,1.3f,0.15f)); lamp.globalIlluminationFlags=MaterialGlobalIlluminationFlags.RealtimeEmissive; EditorUtility.SetDirty(lamp);
            foreach(string name in new[]{"MuroOeste","MuroEste","MuroFondo","Rellano","BordeEscalera"})
                foreach(Transform child in passage)
                    if(child.name==name && child.GetComponent<Renderer>()!=null) child.GetComponent<Renderer>().enabled=false;
            // Cimientos existentes: sus cajas coinciden con el volumen macizo revestido.
            foreach(float x in new[]{63.6f,76.4f})
            {
                for(int row=0;row<7;row++)
                for(int column=0;column<17;column++)
                {
                    float z=-45-(column+0.5f)*39/17;
                    Stone(root,"SillarLateral",new Vector3(x,-5.4f+(row+0.5f)*6/7,z),new Vector3(0.8f,6/7f-0.035f,39/17f-0.035f),stones[(column+row*3)%5]);
                }
                for(int i=0;i<13;i++)
                    Stone(root,"RemateLateral",new Vector3(x,0.72f,-46.5f-i*3),new Vector3(1.08f,0.32f,2.97f),cap,true);
                for(int i=0;i<7;i++) Pillar(root,x,-46.5f-i*6,i==0 || i==6);
            }
            var back=passage.Find("MuroFondo"); back.localPosition=new Vector3(70,-0.55f,-84); back.localScale=new Vector3(13.6f,9.7f,0.8f);
            for(int row=0;row<11;row++)
            for(int column=0;column<6;column++)
                Stone(root,"SillarFondo",new Vector3(63.2f+(column+0.5f)*13.6f/6,-5.4f+(row+0.5f)*9.7f/11,-84),
                    new Vector3(13.6f/6-0.035f,9.7f/11-0.035f,0.8f),stones[(row+column*2)%5]);
            for(int i=0;i<6;i++) Stone(root,"RemateFondo",new Vector3(63.2f+(i+0.5f)*13.6f/6,4.42f,-84),new Vector3(13.6f/6-0.02f,0.34f,1.1f),cap,true);
            // Losas planas: juntas poco profundas, sin obstáculos añadidos al recorrido.
            for(int row=0;row<8;row++)
            for(int column=0;column<4;column++)
                Stone(root,"LosaRellano",new Vector3(64+(column+0.5f)*3,-4.25f,-64-(row+0.5f)*2.5f),new Vector3(2.95f,0.16f,2.45f),stones[(row*3+column)%5]);
            // Las cajas laterales de la escalera se revisten; no se ocultan paredes físicas.
            foreach(float x in new[]{64.8f,75.2f})
                for(int row=0;row<7;row++)
                for(int i=0;i<8;i++) Stone(root,"BordeEscaleraPiedra",new Vector3(x,-4.25f+(row+0.5f)*5.5f/7,-45-(i+0.5f)*19/8),new Vector3(0.35f,5.5f/7-0.025f,19/8f-0.025f),stones[(i+row*3)%5]);
            Physics.SyncTransforms();
        }
        private static void Pillar(Transform root,float x,float z,bool illuminated)
        {
            for(int row=0;row<7;row++) Stone(root,"Pilar",new Vector3(x,-5.4f+(row+0.5f)*0.9f,z),new Vector3(1.15f,0.87f,1.15f),stones[row%5],true);
            Stone(root,"CoronaPilar",new Vector3(x,1.04f,z),new Vector3(1.38f,0.32f,1.38f),cap,true);
            Stone(root,"BaseFarol",new Vector3(x,1.29f,z),new Vector3(0.55f,0.2f,0.55f),iron);
            Stone(root,"FarolAmbar",new Vector3(x,1.57f,z),new Vector3(0.30f,0.42f,0.30f),lamp);
            Stone(root,"TechoFarol",new Vector3(x,1.84f,z),new Vector3(0.55f,0.18f,0.55f),iron);
            float inner=x<70?x+0.59f:x-0.59f;
            Stone(root,"FlejeHierro",new Vector3(inner,-1.8f,z),new Vector3(0.08f,4.8f,0.24f),iron);
            if(illuminated)
            {
                var light=new GameObject("LuzFarol").AddComponent<Light>(); light.transform.SetParent(root,false);
                light.transform.position=new Vector3(x,1.6f,z); light.type=LightType.Point; light.range=8;
                light.color=new Color(1,0.48f,0.12f); light.intensity=2; light.shadows=LightShadows.None;
            }
        }
        private static void Stone(Transform root,string name,Vector3 position,Vector3 size,Material material,bool solid=false)
        {
            var go=new GameObject(name); go.transform.SetParent(root,false); go.transform.position=position; go.transform.localScale=size;
            go.AddComponent<MeshFilter>().sharedMesh=block; go.AddComponent<MeshRenderer>().sharedMaterial=material;
            if(solid) go.AddComponent<BoxCollider>();
        }
        private static Material Material(string name,Color color)
        {
            string path=Folder+name+".mat"; var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,path); }
            material.color=color; material.SetFloat("_Smoothness",0.12f); EditorUtility.SetDirty(material); return material;
        }
        private static Mesh BlockMesh()
        {
            string path=Folder+"SillarBiselado.asset"; var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh!=null) return mesh;
            var vertices=new List<Vector3>(); var triangles=new List<int>();
            void Polygon(Vector3[] points,Vector3 normal)
            {
                if(Vector3.Dot(Vector3.Cross(points[1]-points[0],points[2]-points[0]),normal)<0) System.Array.Reverse(points);
                int start=vertices.Count; vertices.AddRange(points);
                for(int i=1;i<points.Length-1;i++) { triangles.Add(start); triangles.Add(start+i); triangles.Add(start+i+1); }
            }
            for(int axis=0;axis<3;axis++) foreach(int sign in new[]{-1,1})
            {
                int a=(axis+1)%3,b=(axis+2)%3; var points=new Vector3[4];
                int[] sa={-1,-1,1,1},sb={-1,1,1,-1};
                for(int i=0;i<4;i++) { points[i][axis]=sign*0.5f; points[i][a]=sa[i]*0.44f; points[i][b]=sb[i]*0.44f; }
                Vector3 normal=Vector3.zero; normal[axis]=sign; Polygon(points,normal);
            }
            for(int a=0;a<3;a++) for(int b=a+1;b<3;b++) foreach(int sa in new[]{-1,1}) foreach(int sb in new[]{-1,1})
            {
                int c=3-a-b; var points=new Vector3[4];
                for(int i=0;i<4;i++) { bool faceA=i==0 || i==3; points[i][a]=sa*(faceA?0.5f:0.44f); points[i][b]=sb*(faceA?0.44f:0.5f); points[i][c]=(i<2?-1:1)*0.44f; }
                var normal=Vector3.zero; normal[a]=sa; normal[b]=sb; Polygon(points,normal);
            }
            foreach(int x in new[]{-1,1}) foreach(int y in new[]{-1,1}) foreach(int z in new[]{-1,1})
                Polygon(new[]{new Vector3(x*0.5f,y*0.44f,z*0.44f),new Vector3(x*0.44f,y*0.5f,z*0.44f),new Vector3(x*0.44f,y*0.44f,z*0.5f)},new Vector3(x,y,z));
            mesh=new Mesh {name="SillarBiselado"}; mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,path); return mesh;
        }
    }
}
