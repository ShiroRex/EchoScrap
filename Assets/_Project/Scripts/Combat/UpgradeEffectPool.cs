using UnityEngine;
using EchoScrap.Core;
using UnityEngine.Rendering;
using EchoScrap.Player;

namespace EchoScrap.Combat
{
    /// <summary>Fixed visual reserve. Saturating cosmetic views never removes simulated attacks.</summary>
    internal sealed class UpgradeEffectPool
    {
        internal enum Shape { Ring, Dart, Saw, Mine, Turret, Ice, Blade, Decoy }
        internal sealed class View
        {
            public Transform Root;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public bool Used;
            public bool Transient;
            public float Age, Life, Radius;
            public bool Inward;
            public Color Color;
        }
        private readonly View[] views;
        private readonly Mesh[] meshes = new Mesh[8];
        private readonly Material material;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private readonly CollectorSpriteView collector;
        private readonly EffectPreferences preferences;
        private static readonly int Tint = Shader.PropertyToID("_Color");
        public int ActiveCount { get; private set; }
        public UpgradeEffectPool(Transform parent, int capacity, CollectorSpriteView sprite, EffectPreferences settings)
        {
            collector = sprite; preferences = settings;
            material = Resources.Load<Material>("Upgrades/UpgradeEffects");
            if (material == null) material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            meshes[0] = Ring(32, .9f);
            meshes[1] = Polygon(new[] {new Vector3(0,0,.65f),new Vector3(-.18f,0,-.3f),new Vector3(0,.12f,-.1f),new Vector3(.18f,0,-.3f)});
            meshes[2] = Ring(24, .35f, true);
            meshes[3] = Ring(8, .45f, true);
            meshes[4] = Turret();
            meshes[5] = Crystal();
            meshes[6] = Polygon(new[] {new Vector3(-1,0,-.15f),new Vector3(-.8f,0,.25f),new Vector3(0,.1f,.45f),new Vector3(.8f,0,.25f),new Vector3(1,0,-.15f),new Vector3(0,0,0)});
            meshes[7] = meshes[5];
            views = new View[Mathf.Max(16, capacity)];
            for (int i = 0; i < views.Length; i++)
            {
                var go = new GameObject("UpgradeEffect"); go.transform.SetParent(parent, false);
                var v = new View { Root = go.transform, Filter = go.AddComponent<MeshFilter>(), Renderer = go.AddComponent<MeshRenderer>() };
                v.Renderer.sharedMaterial = material; v.Renderer.shadowCastingMode = ShadowCastingMode.Off; v.Renderer.receiveShadows = false;
                go.SetActive(false); views[i] = v;
            }
        }
        public View Acquire(Shape shape, Vector3 position, Color color, float scale = 1f)
        {
            for (int i = 0; i < views.Length; i++)
            {
                var v = views[i]; if (v.Used) continue;
                v.Used = true; v.Transient = false; v.Age = 0f; v.Color = color;
                v.Filter.sharedMesh = meshes[(int)shape]; v.Renderer.sharedMaterial = material;
                v.Root.SetPositionAndRotation(position, Quaternion.identity); v.Root.localScale = Vector3.one * scale;
                if (shape == Shape.Decoy && collector != null && collector.CharacterRenderer != null)
                {
                    v.Filter.sharedMesh = collector.CharacterMesh; v.Renderer.sharedMaterial = collector.AfterimageMaterial;
                    collector.CopyAfterimageTo(v.Root, v.Renderer);
                    v.Root.position += position - collector.transform.position;
                }
                if(shape == Shape.Decoy) color.a = .4f;
                SetColor(v, color); v.Root.gameObject.SetActive(true); ActiveCount++; return v;
            }
            return null;
        }
        public void SetShape(View v, Shape shape) { if(v != null) v.Filter.sharedMesh=meshes[(int)shape]; }
        public void SetColor(View v, Color color)
        {
            if (v == null) return;
            v.Renderer.GetPropertyBlock(block); block.SetColor(Tint, color); block.SetColor("_BaseColor", color); v.Renderer.SetPropertyBlock(block);
        }
        public void Release(View v)
        {
            if (v == null || !v.Used) return;
            v.Used = false; v.Root.gameObject.SetActive(false); ActiveCount--;
        }
        public void Burst(Vector3 p, float radius, Color color, bool inward = false)
        {
            p.y = ArenaSurface.Height(p) + .12f;
            if(preferences.ReducedEffectsMode) color.a *= .7f;
            var v = Acquire(Shape.Ring, p, color);
            if (v == null) return;
            v.Transient = true; v.Life = preferences.ReducedEffectsMode ? .2f : .35f; v.Radius = radius; v.Inward = inward;
        }
        public void Step(float dt)
        {
            for (int i = 0; i < views.Length; i++)
            {
                var v = views[i]; if (!v.Used || !v.Transient) continue;
                v.Age += dt; float t = v.Age / v.Life;
                if (t >= 1) { Release(v); continue; }
                v.Root.localScale = Vector3.one * (v.Radius * (v.Inward ? 1f - t : .1f + .9f * t));
                var c = v.Color; c.a *= 1f - t; SetColor(v, c);
            }
        }
        public void Clear() { for (int i=0;i<views.Length;i++) Release(views[i]); }
        public void Dispose()
        {
            for (int i=0;i<views.Length;i++) if (views[i].Root != null) Object.Destroy(views[i].Root.gameObject);
            for (int i=0;i<meshes.Length-1;i++) Object.Destroy(meshes[i]);
            if (material != null && !UnityEngine.Resources.Load<Material>("Upgrades/UpgradeEffects")) Object.Destroy(material);
        }
        private static Mesh Ring(int count, float inner, bool teeth = false)
        {
            var vertices = new Vector3[count*2]; var triangles = new int[count*6];
            for (int i=0;i<count;i++)
            {
                float a = i * Mathf.PI * 2 / count; var d = new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                vertices[i*2] = d * (teeth && i%2==0 ? .72f : 1f); vertices[i*2+1] = d * inner;
                int n=(i+1)%count*2, j=i*6; triangles[j]=i*2; triangles[j+1]=n; triangles[j+2]=i*2+1; triangles[j+3]=n; triangles[j+4]=n+1; triangles[j+5]=i*2+1;
            }
            var colors = new Color[vertices.Length];
            for(int i=0;i<count;i++) { colors[i*2]=Color.white; colors[i*2+1]=new Color(.48f,.55f,.62f,1); }
            var mesh = new Mesh {name="Upgrade machined ring",vertices=vertices,triangles=triangles,colors=colors}; mesh.RecalculateNormals(); return mesh;
        }
        private static Mesh Polygon(Vector3[] points)
        {
            var triangles = new int[(points.Length-2)*3];
            for(int i=0;i<points.Length-2;i++) {triangles[i*3]=0;triangles[i*3+1]=i+1;triangles[i*3+2]=i+2;}
            var colors = new Color[points.Length]; for(int i=0;i<colors.Length;i++)colors[i]=Color.white;
            var mesh = new Mesh {name="Upgrade mechanical silhouette",vertices=points,triangles=triangles,colors=colors}; mesh.RecalculateNormals(); return mesh;
        }
        private static Mesh Crystal()
        {
            const int sides=6;
            var vertices=new Vector3[sides*9];var colors=new Color[vertices.Length];var triangles=new int[vertices.Length];
            for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;
                var lowA=new Vector3(Mathf.Cos(a)*.82f,0,Mathf.Sin(a)*.82f);
                var lowB=new Vector3(Mathf.Cos(b)*.82f,0,Mathf.Sin(b)*.82f);
                var highA=new Vector3(Mathf.Cos(a)*.72f,1.6f,Mathf.Sin(a)*.72f);
                var highB=new Vector3(Mathf.Cos(b)*.72f,1.6f,Mathf.Sin(b)*.72f);
                int n=i*9;
                vertices[n]=lowA;vertices[n+1]=highA;vertices[n+2]=lowB;
                vertices[n+3]=lowB;vertices[n+4]=highA;vertices[n+5]=highB;
                vertices[n+6]=highA;vertices[n+7]=new Vector3(0,2.3f,0);vertices[n+8]=highB;
                for(int j=0;j<9;j++){triangles[n+j]=n+j;float shade=i%2==0?1:.65f;colors[n+j]=new Color(shade,shade,1,j>=6?.5f:.22f);}
            }
            var mesh=new Mesh{name="Upgrade ice shell",vertices=vertices,triangles=triangles,colors=colors};mesh.RecalculateNormals();return mesh;
        }
        private static Mesh Turret()
        {
            var vertices=new System.Collections.Generic.List<Vector3>(72);
            var colors=new System.Collections.Generic.List<Color>(72);
            var triangles=new System.Collections.Generic.List<int>(108);
            void Box(Vector3 min,Vector3 max,float shade)
            {
                var corners=new[] {new Vector3(min.x,min.y,min.z),new Vector3(max.x,min.y,min.z),new Vector3(max.x,max.y,min.z),new Vector3(min.x,max.y,min.z),new Vector3(min.x,min.y,max.z),new Vector3(max.x,min.y,max.z),new Vector3(max.x,max.y,max.z),new Vector3(min.x,max.y,max.z)};
                int[] faces={0,3,2,1,4,5,6,7,0,4,7,3,1,2,6,5,3,7,6,2,0,1,5,4};
                for(int face=0;face<6;face++)
                {
                    int start=vertices.Count;float light=shade*(face==4?1:face%2==0?.55f:.75f);
                    for(int v=0;v<4;v++){vertices.Add(corners[faces[face*4+v]]);colors.Add(new Color(light,light,light,1));}
                    triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);triangles.Add(start);triangles.Add(start+2);triangles.Add(start+3);
                }
            }
            Box(new Vector3(-.48f,0,-.4f),new Vector3(.48f,.3f,.4f),.65f);
            Box(new Vector3(-.22f,.3f,-.2f),new Vector3(.22f,.65f,.25f),1);
            Box(new Vector3(-.09f,.5f,.2f),new Vector3(.09f,.66f,1),.9f);
            var mesh=new Mesh{name="Upgrade scrap turret"};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();return mesh;
        }
    }
}
