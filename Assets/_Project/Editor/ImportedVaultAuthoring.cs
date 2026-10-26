using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EchoScrap.Editor
{
    public static class ImportedVaultAuthoring
    {
        const string Folder="Assets/_Project/Art/Boveda/Piezas/";
        [System.Serializable] class Maps { public Map[] materials; }
        [System.Serializable] class Map { public string name, @base, normal, emission, metallic; }
        public static void Apply() { WarehouseAuthoring.Apply(); }
        public static void Decorate()
        {
            var passage=GameObject.Find("WarehouseConnection/PasilloDescendente").transform;
            foreach(string name in new[]{"AcabadoPiedra","PiezasImportadas"})
            { var previous=passage.Find(name); if(previous!=null) Object.DestroyImmediate(previous.gameObject); }
            foreach(Transform child in passage.Cast<Transform>().ToArray())
            {
                if(child.name=="BordeEscalera") Object.DestroyImmediate(child.gameObject);
                else if(child.name.StartsWith("Muro") || child.name=="Rellano") child.GetComponent<Renderer>().enabled=false;
            }
            var root=new GameObject("PiezasImportadas").transform; root.SetParent(passage,false);
            var stairs=passage.Find("EscaleraImportada");
            var stairBounds=BoundsOf(stairs.gameObject);
            stairs.localScale=Vector3.Scale(stairs.localScale,new Vector3(14f/stairBounds.size.x,1,1));
            stairBounds=BoundsOf(stairs.gameObject); stairs.position+=Vector3.right*(70-stairBounds.center.x);
            FitStairCollision(stairs);
            FitStairRails(stairs);
            // Reflejar solo la geometría para alinear también pilares y faroles.
            foreach(int side in new[]{-1,1})
                for(int i=0;i<4;i++)
                    Piece("Muro",root,new Vector3(70+side*6.7f,-8.8f,-45-(i+0.5f)*5.75f),new Vector3(2.6f,9.8f,5.9f),-90,side>0);
            foreach(int side in new[]{-1,1})
            {
                var wall=passage.Find(side<0?"MuroOeste":"MuroEste");
                wall.localPosition=new Vector3(70+side*6.7f,-3.9f,-56.5f);
                wall.localScale=new Vector3(2.6f,9.8f,23);
            }
            for(int row=0;row<2;row++)
                for(int column=0;column<3;column++)
                    Piece("Losas",root,new Vector3(66+column*4,-8.18f,-62.75f-row*3.5f),new Vector3(4.08f,0.20f,3.58f),0);
            var backdrop=Piece("Fondo",root,new Vector3(70,-8.2f,-67.1f),new Vector3(15.2f,8.3f,1.3f),180);
            foreach(var filter in backdrop.GetComponentsInChildren<MeshFilter>()) filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
            // El fondo sólido queda detrás del hueco y del disparador de viaje.
            var back=passage.Find("MuroFondo"); back.localPosition=new Vector3(70,-3.5f,-68); back.localScale=new Vector3(15.2f,9.4f,0.8f);
            BuildCave(root);
            Physics.SyncTransforms();
        }
        static void BuildCave(Transform root)
        {
            // Cubierta alta sobre el fondo: entrada y escalera abiertas a la cámara.
            foreach(int side in new[]{-1,1})
                for(int i=0;i<3;i++) CaveProp(root,"chatarra_alta",new Vector3(70+side*8.7f,0,-53-i*5),new Vector3(7,5,7),side*25);
            CaveProp(root,"vigas",new Vector3(70,0.8f,-65),new Vector3(17,3,8),0);
            CaveProp(root,"chatarra_baja",new Vector3(70,2.5f,-66),new Vector3(16,3,7),15);
            CaveProp(root,"roca_baja",new Vector3(62,0,-47),new Vector3(5,3,5),20);
            CaveProp(root,"roca_baja",new Vector3(78,0,-47),new Vector3(5,3,5),-20);
        }
        static void CaveProp(Transform parent,string name,Vector3 bottom,Vector3 size,float yaw)
        {
            var anchor=new GameObject("Cueva_"+name); anchor.transform.SetParent(parent,false);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Mundo3D/Modelos/"+name+".fbx"));
            model.transform.SetParent(anchor.transform,false); model.transform.rotation=Quaternion.Euler(0,yaw,0)*model.transform.rotation;
            var b=BoundsOf(anchor); anchor.transform.localScale=new Vector3(size.x/b.size.x,size.y/b.size.y,size.z/b.size.z);
            b=BoundsOf(anchor); anchor.transform.position+=new Vector3(bottom.x-b.center.x,bottom.y-b.min.y,bottom.z-b.center.z);
            foreach(var filter in model.GetComponentsInChildren<MeshFilter>()) filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
        }
        static void FitStairRails(Transform stairs)
        {
            var filter=stairs.GetComponentInChildren<MeshFilter>();
            // La mayor profundidad no debe convertir las barandillas en torres.
            var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Boveda/Escaleras.fbx").GetComponentInChildren<MeshFilter>().sharedMesh;
            var mesh=Object.Instantiate(original); mesh.name="EscaleraProfunda";
            var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++)
            {
                var p=filter.transform.TransformPoint(vertices[i]);
                // Deformación continua: no cortar las caras que cruzan el borde del carril.
                {
                    float floor=-8*Mathf.Clamp01((-p.z-45)/16);
                    if(p.y>floor+1.3f) p.y=floor+1.3f+(p.y-floor-1.3f)*.18f;
                    vertices[i]=filter.transform.InverseTransformPoint(p);
                }
            }
            mesh.vertices=vertices; mesh.RecalculateBounds(); mesh.RecalculateNormals();
            string path=Folder+"EscaleraProfunda.asset"; var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved==null) { AssetDatabase.CreateAsset(mesh,path); saved=mesh; }
            else { EditorUtility.CopySerialized(mesh,saved); Object.DestroyImmediate(mesh); }
            filter.sharedMesh=saved;
        }
        static void FitStairCollision(Transform stairs)
        {
            var collider=stairs.GetComponentInChildren<MeshCollider>();
            // Muestrear siempre la fuente visual, evitando acumular aproximaciones al reaplicar.
            collider.sharedMesh=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Boveda/Escaleras.fbx").GetComponentInChildren<MeshFilter>().sharedMesh;
            Physics.SyncTransforms();
            var original=collider.sharedMesh;
            var vertices=new List<Vector3>(original.vertices); var triangles=new List<int>();
            for(int i=0;i<vertices.Count;i++)
            {
                var p=collider.transform.TransformPoint(vertices[i]);
                // Deformación continua: no cortar las caras que cruzan el borde del carril.
                {
                    float floor=-8*Mathf.Clamp01((-p.z-45)/16);
                    if(p.y>floor+1.3f) p.y=floor+1.3f+(p.y-floor-1.3f)*.18f;
                    vertices[i]=collider.transform.InverseTransformPoint(p);
                }
            }
            var indices=original.triangles;
            for(int i=0;i<indices.Length;i+=3)
            {
                float minX=float.PositiveInfinity,maxX=float.NegativeInfinity;
                for(int j=0;j<3;j++)
                {
                    var p=collider.transform.TransformPoint(vertices[indices[i+j]]);
                    minX=Mathf.Min(minX,p.x); maxX=Mathf.Max(maxX,p.x);
                }
                if(maxX<=65.4f || minX>=74.6f) { triangles.Add(indices[i]); triangles.Add(indices[i+1]); triangles.Add(indices[i+2]); }
            }
            for(int i=0;i<=160;i++)
            {
                float z=-45-i*.1f;
                // El escaneo contiene caras inferiores: el apoyo nunca debe bajar
                // bajo el terreno. Regularizar los doce peldaños entre 0 y -8 m.
                float y=-Mathf.Floor(i/160f*12)*(8f/12f);
                vertices.Add(collider.transform.InverseTransformPoint(new Vector3(65.4f,y,z)));
                vertices.Add(collider.transform.InverseTransformPoint(new Vector3(74.6f,y,z)));
                if(i==0) continue;
                int a=vertices.Count-4;
                triangles.Add(a); triangles.Add(a+1); triangles.Add(a+2);
                triangles.Add(a+1); triangles.Add(a+3); triangles.Add(a+2);
            }
            var mesh=new Mesh {name="PeldañosTransitables",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds(); mesh.RecalculateNormals();
            string path=Folder+"PeldanosCollision.asset";
            var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved==null) { AssetDatabase.CreateAsset(mesh,path); saved=mesh; }
            else { EditorUtility.CopySerialized(mesh,saved); Object.DestroyImmediate(mesh); }
            collider.sharedMesh=saved;
        }
        static GameObject Piece(string name,Transform parent,Vector3 bottom,Vector3 size,float yaw,bool mirror=false)
        {
            string folder=Folder+name+"/";
            var anchor=new GameObject(name); anchor.transform.SetParent(parent,false);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(folder+name+".fbx"));
            model.transform.SetParent(anchor.transform,false);
            model.transform.rotation=Quaternion.Euler(0,yaw,0)*model.transform.rotation;
            var maps=JsonUtility.FromJson<Maps>(System.IO.File.ReadAllText(folder+"Texturas/materials.json"));
            var mats=maps.materials.Select((map,index)=>Material(folder,map,index)).ToArray();
            foreach(var renderer in model.GetComponentsInChildren<Renderer>()) renderer.sharedMaterials=mats;
            var bounds=BoundsOf(anchor);
            anchor.transform.localScale=new Vector3(size.x/bounds.size.x,size.y/bounds.size.y,size.z/bounds.size.z);
            if(mirror) anchor.transform.localScale=Vector3.Scale(anchor.transform.localScale,new Vector3(-1,1,1));
            bounds=BoundsOf(anchor);
            anchor.transform.position+=new Vector3(bottom.x-bounds.center.x,bottom.y-bounds.min.y,bottom.z-bounds.center.z);
            return anchor;
        }
        static Material Material(string folder,Map map,int index)
        {
            string path=folder+"Material_"+index+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,path); }
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(map.@base));
            if(!string.IsNullOrEmpty(map.normal))
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(map.normal);
                if(importer.textureType!=TextureImporterType.NormalMap) { importer.textureType=TextureImporterType.NormalMap; importer.SaveAndReimport(); }
                material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(map.normal)); material.EnableKeyword("_NORMALMAP");
            }
            material.SetFloat("_Smoothness",0.15f); EditorUtility.SetDirty(material); return material;
        }
        static Bounds BoundsOf(GameObject go)
        {
            var renderers=go.GetComponentsInChildren<Renderer>(); var bounds=renderers[0].bounds;
            foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds); return bounds;
        }
    }
}
