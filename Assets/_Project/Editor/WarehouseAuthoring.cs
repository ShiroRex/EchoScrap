using EchoScrap.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EchoScrap.Editor
{
    public static class WarehouseAuthoring
    {
        private const string Art="Assets/_Project/Art/Mundo3D/";
        private static Material stone, steel;
        public static void Apply()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/CombatArena.unity");
            EchoScrapWorldBuilder.Build();
            WorldArtIntegration.DecorateGround();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            TerrainCollisionAuthoring.Apply();
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach(string name in new[]{"WarehouseConnection","PortalVortex","PortalGlow"})
            { var old=GameObject.Find(name); if(old!=null) Object.DestroyImmediate(old); }
            stone=Material("PassageStone",new Color(0.25f,0.22f,0.19f));
            steel=Material("WarehouseSteel",new Color(0.17f,0.24f,0.26f));
            var root=new GameObject("WarehouseConnection");
            var travel=root.AddComponent<WarehouseTravel>();
            var world=GameObject.Find("Mundo3D_EchoScrap").transform;
            var previous=world.Find("Portal/portal_43_-40");
            if(previous!=null) Object.DestroyImmediate(previous.gameObject);
            // Despejar solo el acceso: los modelos desplazados siguen siendo visibles y sólidos.
            Bounds corridor=new Bounds(new Vector3(70,0,-64),new Vector3(16,30,42));
            foreach(Transform group in world)
            foreach(Transform anchor in group)
            {
                if(group.name=="Emblema_triangular") continue;
                var renderers=anchor.GetComponentsInChildren<Renderer>();
                if(renderers.Length==0) continue;
                Bounds bounds=BoundsOf(anchor.gameObject);
                if(!corridor.Intersects(bounds)) continue;
                bool left=bounds.center.x<70 || bounds.size.x>7;
                anchor.position+=Vector3.right*(left ? 62f-bounds.max.x : 78f-bounds.min.x);
            }
            var passage=new GameObject("PasilloDescendente"); passage.transform.SetParent(root.transform,false);
            BuildStairs(passage.transform);
            Box("Rellano",passage.transform,new Vector3(70,-4.32f,-74),new Vector3(12,0.24f,20),stone);
            Box("MuroOeste",passage.transform,new Vector3(63.6f,-2.4f,-64.5f),new Vector3(0.8f,6,39),stone);
            Box("MuroEste",passage.transform,new Vector3(76.4f,-2.4f,-64.5f),new Vector3(0.8f,6,39),stone);
            Box("MuroFondo",passage.transform,new Vector3(70,-2.4f,-84),new Vector3(13.6f,6,0.8f),stone);
            var combatPortal=Portal(world.Find("Portal"),"portal_43_-40",new Vector3(70,-4.2f,-81),9f);
            var combatBounds=BoundsOf(combatPortal);
            Gateway(root.transform,travel,false,new Vector3(70,-2.2f,combatBounds.max.z+0.6f));
            travel.CombatArrival=Marker(root.transform,"RegresoCombate",new Vector3(70,-3.2f,-75));

            var warehouse=new GameObject("Almacen"); warehouse.transform.SetParent(root.transform,false);
            Box("SueloAlmacen",warehouse.transform,new Vector3(180,-0.25f,0),new Vector3(24,0.5f,26),stone);
            Box("MuroNorte",warehouse.transform,new Vector3(180,1.5f,13),new Vector3(24,3,0.6f),steel);
            Box("MuroSur",warehouse.transform,new Vector3(180,0.7f,-13),new Vector3(24,1.4f,0.6f),steel);
            foreach(int side in new[]{-1,1})
            {
                Box("MuroLateral",warehouse.transform,new Vector3(180+side*12,1.5f,0),new Vector3(0.6f,3,26),steel);
                for(int row=0;row<3;row++)
                {
                    float x=180+side*8.6f,z=-3+row*6;
                    Box("EstanteBase",warehouse.transform,new Vector3(x,0.35f,z),new Vector3(3,0.25f,4),steel);
                    Box("EstanteSuperior",warehouse.transform,new Vector3(x,2.5f,z),new Vector3(3,0.2f,4),steel);
                    foreach(float offset in new[]{-1.8f,1.8f})
                        Box("Soporte",warehouse.transform,new Vector3(x+side*1.3f,1.5f,z+offset),new Vector3(0.16f,3,0.16f),steel);
                    Prop("cajas",warehouse.transform,new Vector3(x,0.5f,z),2.5f);
                    Prop("cajas",warehouse.transform,new Vector3(x,2.65f,z),2f);
                }
            }
            Prop("pale",warehouse.transform,new Vector3(176,0,9),3f);
            Prop("contenedor",warehouse.transform,new Vector3(183,0,9),4f);
            var returnPortal=Portal(warehouse.transform,"PortalRegreso",new Vector3(180,0,-9),7f);
            var returnBounds=BoundsOf(returnPortal);
            Gateway(root.transform,travel,true,new Vector3(180,2,returnBounds.max.z+0.6f));
            travel.WarehouseArrival=Marker(root.transform,"LlegadaAlmacen",new Vector3(180,1,-2.5f));
            Light(warehouse.transform,new Vector3(180,6,1),new Color(0.5f,0.75f,1f),18,4);
            Physics.SyncTransforms(); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("WAREHOUSE_OK stairs=12 portals=2 return=preserved");
        }
        private static GameObject Portal(Transform parent,string name,Vector3 position,float width)
        {
            var frame=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Boveda/PortalFrame.fbx"));
            frame.name=name; frame.transform.SetParent(parent,false);
            // El FBX ya contiene la conversión Z-up a Y-up: conservarla.
            frame.transform.rotation=Quaternion.Euler(0,180,0)*frame.transform.rotation;
            var original=AssetDatabase.LoadAssetAtPath<GameObject>(Art+"Modelos/portal.fbx");
            foreach(var r in frame.GetComponentsInChildren<Renderer>()) r.sharedMaterial=original.GetComponentInChildren<Renderer>().sharedMaterial;
            Bounds bounds=BoundsOf(frame); frame.transform.localScale*=width/bounds.size.x;
            bounds=BoundsOf(frame);
            frame.transform.position+=new Vector3(position.x-bounds.center.x,position.y-bounds.min.y,position.z-bounds.center.z);
            foreach(var filter in frame.GetComponentsInChildren<MeshFilter>()) filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
            bounds=BoundsOf(frame);
            var vortex=GameObject.CreatePrimitive(PrimitiveType.Quad); vortex.name="PortalVortex";
            Object.DestroyImmediate(vortex.GetComponent<Collider>());
            vortex.transform.SetParent(parent,false);
            Transform center=null,radius=null;
            foreach(var marker in frame.GetComponentsInChildren<Transform>())
            { if(marker.name=="ApertureCenter") center=marker; if(marker.name=="ApertureRadius") radius=marker; }
            vortex.transform.position=center.position;
            vortex.transform.localScale=Vector3.one*Vector3.Distance(center.position,radius.position)*2.01f;
            vortex.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Art+"Materiales/PortalVortex.mat");
            var sparks=vortex.AddComponent<GalaxyPortalVfx>();
            sparks.SparkMaterial=SparkMaterial();
            Light(parent,vortex.transform.position+Vector3.forward,new Color(0.5f,0.12f,1),10,5);
            return frame;
        }
        [System.Serializable] private class Maps { public Map[] materials; }
        [System.Serializable] private class Map { public string @base,normal,emission,metallic; }
        private static void BuildStairs(Transform root)
        {
            const string folder="Assets/_Project/Art/Boveda/";
            var maps=JsonUtility.FromJson<Maps>(System.IO.File.ReadAllText(folder+"Texturas/materials.json")).materials[0];
            var material=AssetDatabase.LoadAssetAtPath<Material>(folder+"Escaleras.mat");
            if(material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,folder+"Escaleras.mat"); }
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(maps.@base));
            var normalImporter=(TextureImporter)AssetImporter.GetAtPath(maps.normal);
            if(normalImporter!=null) { normalImporter.textureType=TextureImporterType.NormalMap; normalImporter.maxTextureSize=2048; normalImporter.SaveAndReimport(); }
            material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(maps.normal)); material.EnableKeyword("_NORMALMAP"); EditorUtility.SetDirty(material);
            var anchor=new GameObject("EscaleraImportada"); anchor.transform.SetParent(root,false);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"Escaleras.fbx"));
            model.transform.SetParent(anchor.transform,false);
            var renderer=model.GetComponentInChildren<Renderer>(); renderer.sharedMaterial=material;
            Bounds b=BoundsOf(model);
            anchor.transform.localScale=new Vector3(10f/b.size.x,1f,19f/b.size.z);
            b=BoundsOf(model); anchor.transform.position=new Vector3(70-b.center.x,-b.center.y,-54.5f-b.center.z);
            var collider=model.GetComponentInChildren<MeshFilter>().gameObject.AddComponent<MeshCollider>(); collider.sharedMesh=model.GetComponentInChildren<MeshFilter>().sharedMesh;
            Physics.SyncTransforms();
            float north=StairHeight(collider,-45.5f),south=StairHeight(collider,-63.5f);
            if(north<south) { anchor.transform.Rotate(0,180,0,Space.World); Physics.SyncTransforms(); north=StairHeight(collider,-45.5f); south=StairHeight(collider,-63.5f); }
            StairRange(collider,out float low,out float high);
            float scale=4.2f/Mathf.Max(0.05f,high-low);
            anchor.transform.localScale=Vector3.Scale(anchor.transform.localScale,new Vector3(1,scale,1)); Physics.SyncTransforms();
            StairRange(collider,out low,out high);
            anchor.transform.position-=Vector3.up*high;
            var walkable=anchor.AddComponent<EchoScrap.Core.WalkableStairs>(); walkable.Surface=collider;
            // Bordes sólidos, solo donde se ven las barandillas. El carril central queda libre.
            foreach(float x in new[]{64.8f,75.2f})
                Box("BordeEscalera",root,new Vector3(x,-1.5f,-54.5f),new Vector3(0.35f,5.5f,19),stone);
        }
        private static float StairHeight(MeshCollider collider,float z)
        { if(collider.Raycast(new Ray(new Vector3(70,30,z),Vector3.down),out var hit,60)) return hit.point.y; throw new System.Exception("La escalera no cubre su carril en z="+z); }
        private static void StairRange(MeshCollider collider,out float low,out float high)
        {
            low=float.PositiveInfinity; high=float.NegativeInfinity;
            for(float z=-63.8f;z< -45.2f;z+=0.1f)
            { float y=StairHeight(collider,z); low=Mathf.Min(low,y); high=Mathf.Max(high,y); }
        }
        private static Material SparkMaterial()
        {
            string path=Art+"Materiales/PortalSparks.mat"; var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null) { material=new Material(Shader.Find("EchoScrap/PortalSparks")); AssetDatabase.CreateAsset(material,path); }
            return material;
        }
        private static void Gateway(Transform root,WarehouseTravel travel,bool fromWarehouse,Vector3 position)
        {
            var gate=new GameObject(fromWarehouse?"EntradaRegreso":"EntradaAlmacen"); gate.transform.SetParent(root,false); gate.transform.position=position;
            var collider=gate.AddComponent<BoxCollider>(); collider.isTrigger=true; collider.size=new Vector3(3.8f,4f,1.4f);
            var gateway=gate.AddComponent<WarehouseGateway>(); gateway.Travel=travel; gateway.FromWarehouse=fromWarehouse;
        }
        private static Transform Marker(Transform root,string name,Vector3 position)
        { var marker=new GameObject(name).transform; marker.SetParent(root,false); marker.position=position; return marker; }
        private static void Light(Transform root,Vector3 position,Color color,float range,float intensity)
        { var light=new GameObject("LuzPortal").AddComponent<UnityEngine.Light>(); light.transform.SetParent(root,false); light.transform.position=position; light.type=LightType.Point; light.color=color; light.range=range; light.intensity=intensity; }
        private static void Box(string name,Transform root,Vector3 position,Vector3 size,Material material)
        { var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.SetParent(root,false); go.transform.position=position; go.transform.localScale=size; go.GetComponent<Renderer>().sharedMaterial=material; }
        private static void Prop(string name,Transform root,Vector3 position,float width)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Art+"Modelos/"+name+".fbx"));
            go.transform.SetParent(root,false); Bounds bounds=BoundsOf(go);
            go.transform.localScale*=width/Mathf.Max(bounds.size.x,bounds.size.z); bounds=BoundsOf(go);
            go.transform.position+=new Vector3(position.x-bounds.center.x,position.y-bounds.min.y,position.z-bounds.center.z);
            foreach(var filter in go.GetComponentsInChildren<MeshFilter>()) filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
        }
        private static Bounds BoundsOf(GameObject go)
        { var renderers=go.GetComponentsInChildren<Renderer>(); var bounds=renderers[0].bounds; foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds); return bounds; }
        private static Material Material(string name,Color color)
        {
            string path=Art+"Materiales/"+name+".mat"; var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color=color; material.SetFloat("_Smoothness",0.12f); AssetDatabase.CreateAsset(material,path); }
            return material;
        }
    }
}
