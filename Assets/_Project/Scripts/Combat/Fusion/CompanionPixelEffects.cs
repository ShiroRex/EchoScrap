using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    // A fixed pool of billboard quads; animation advances in twelve pixel-art frames per second.
    public sealed class CompanionPixelEffects : MonoBehaviour
    {
        private sealed class Effect { public Transform Root; public MeshRenderer Renderer; public float Age,Size; public int Kind; public Color Color; public Vector3 Position; }
        private readonly Effect[] pool=new Effect[24];
        private Effect shield,bolt;
        private Material material;
        private Mesh mesh;
        private MaterialPropertyBlock properties;
        private float clock;
        public void Initialize()
        {
            properties=new MaterialPropertyBlock();
            material=new Material(Resources.Load<Shader>("Companions/PixelEffects"));
            mesh=new Mesh {name="Companion pixel quad"};
            mesh.vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(.5f,-.5f),new Vector3(.5f,.5f),new Vector3(-.5f,.5f)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateBounds();
            for(int i=0;i<pool.Length;i++)pool[i]=Create("PoderPixel_"+i);
            shield=Create("EscudoHadaPixel");shield.Kind=0;shield.Size=2.6f;shield.Color=new Color(.68f,.4f,1,.8f);
            bolt=Create("ProyectilMagoPixel");bolt.Kind=2;bolt.Size=.9f;bolt.Color=new Color(.25f,.6f,1);
        }
        private Effect Create(string name)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;go.SetActive(false);
            return new Effect{Root=go.transform,Renderer=renderer};
        }
        public void Emit(int kind,Vector3 position,float size,Color color)
        {
            if(pool[0]==null)return;
            foreach(var fx in pool)if(!fx.Root.gameObject.activeSelf){fx.Kind=kind;fx.Position=position;fx.Size=size;fx.Age=0;fx.Color=color;fx.Root.gameObject.SetActive(true);Draw(fx,0);return;}
        }
        public void SetShield(bool visible)
        {
            if(shield==null)return;shield.Root.gameObject.SetActive(visible);shield.Position=transform.position;
            if(visible)Draw(shield,0);
        }
        public void SetBolt(Vector3 position,bool visible){bolt.Position=position;bolt.Root.gameObject.SetActive(visible);if(visible)Draw(bolt,0);}
        public void Advance(float dt)
        {
            clock+=dt;
            foreach(var fx in pool)if(fx.Root.gameObject.activeSelf){fx.Age+=dt;if(fx.Age>=.7f)fx.Root.gameObject.SetActive(false);else Draw(fx,fx.Age/.7f);}
            if(bolt.Root.gameObject.activeSelf)Draw(bolt,0);
        }
        private void Draw(Effect fx,float age)
        {
            fx.Root.position=fx.Position;
            var camera=Camera.main;if(camera!=null)fx.Root.rotation=camera.transform.rotation;
            float frame=Mathf.Floor(age*8)/8;
            fx.Root.localScale=Vector3.one*fx.Size*(age>0?Mathf.Lerp(.55f,1,frame):1);
            properties.SetFloat("_Kind",fx.Kind);properties.SetFloat("_Frame",Mathf.Floor(clock*12));
            Color c=fx.Color;c.a*=1-frame;properties.SetColor("_Tint",c);fx.Renderer.SetPropertyBlock(properties);
        }
        public void Clear(){foreach(var fx in pool)if(fx!=null)fx.Root.gameObject.SetActive(false);if(bolt!=null)bolt.Root.gameObject.SetActive(false);if(shield!=null)shield.Root.gameObject.SetActive(false);}
        private void OnDestroy(){if(material!=null)Destroy(material);if(mesh!=null)Destroy(mesh);}
    }
}
