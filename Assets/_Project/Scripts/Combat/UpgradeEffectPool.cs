using UnityEngine;
using EchoScrap.Core;
using UnityEngine.Rendering;
using EchoScrap.Player;

namespace EchoScrap.Combat
{
    /// <summary>Fixed reserve of sprites, manually simulated Unity particles and short-lived lights.
    /// Simulation remains independent: cosmetic saturation never discards a projectile or an attack.</summary>
    internal sealed class UpgradeEffectPool
    {
        internal enum Shape { Ring, Dart, Saw, Mine, Turret, Ice, Blade, Decoy, Shield, Wave }
        internal enum BurstKind { Auto, Explosion, Frost, Repair, Collection, Shock, Implosion, Impact }
        internal sealed class View
        {
            public Transform Root;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public bool Used, Transient, Inward;
            public float Age, Life, Radius;
            public Color Color;
            internal Shape Shape;
            internal BurstKind Kind;
            internal ParticleRig Rig;
        }
        internal sealed class ParticleRig
        {
            public Transform Root;
            public readonly ParticleSystem[] Systems = new ParticleSystem[3];
            public Light Light;
            public View Owner;
            public float Accumulator, LightLife;
            public uint Seed;
        }
        public const int ParticleRigCapacity = 32;
        public const int LightCapacity = 6;
        private readonly View[] views;
        private readonly ParticleRig[] rigs = new ParticleRig[ParticleRigCapacity];
        private readonly Mesh groundQuad, uprightQuad, turretQuad, shieldMesh;
        private readonly Material flatMaterial, shieldMaterial, sawMaterial;
        private readonly Material[] spriteMaterials = new Material[11];
        private readonly Material[] particleMaterials = new Material[11];
        private readonly Material smokeMaterial;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private readonly CollectorSpriteView collector;
        private readonly EffectPreferences preferences;
        private static readonly int Tint = Shader.PropertyToID("_Color");
        private static readonly int Phase = Shader.PropertyToID("_Phase");
        private static readonly int Glow = Shader.PropertyToID("_Glow");
        private static readonly string[] TextureNames = { "Saw", "Mine", "Turret", "Spark", "Frost", "Smoke", "Shockwave", "Shield", "Wind", "Explosion", "Frost" };
        public int ActiveCount { get; private set; }
        public int ActiveParticleRigCount { get; private set; }
        public int ActiveLightCount { get; private set; }
        public int ActiveParticleCount
        {
            get { int count=0; for(int i=0;i<rigs.Length;i++) if(rigs[i].Owner!=null) for(int j=0;j<3;j++) count+=rigs[i].Systems[j].particleCount; return count; }
        }
        private bool Decorative => preferences != null && !preferences.ReducedEffectsMode && preferences.DecorativeIntensity > .001f;
        private float Intensity => Decorative ? preferences.DecorativeIntensity : 0f;

        public UpgradeEffectPool(Transform parent, int capacity, CollectorSpriteView sprite, EffectPreferences settings)
        {
            collector=sprite; preferences=settings;
            sawMaterial=Resources.Load<Material>("Upgrades/Vfx/SawMatte");
            groundQuad=Quad(false, false); uprightQuad=Quad(true, false); turretQuad=Quad(true, true); shieldMesh=Sphere();
            flatMaterial=Resources.Load<Material>("Upgrades/UpgradeEffects");
            if(flatMaterial==null) flatMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var spriteTemplate=Resources.Load<Material>("Upgrades/Vfx/SpriteMaterial");
            var spriteShader=spriteTemplate!=null?spriteTemplate.shader:Shader.Find("EchoScrap/UpgradeSpriteVfx");
            var shieldTemplate=Resources.Load<Material>("Upgrades/Vfx/ShieldMaterial");
            var energyShader=shieldTemplate!=null?shieldTemplate.shader:Shader.Find("EchoScrap/UpgradeEnergyShield");
            for(int i=0;i<TextureNames.Length;i++)
            {
                var texture=Resources.Load<Texture2D>("Upgrades/Vfx/"+TextureNames[i]);
                spriteMaterials[i]=MakeMaterial(spriteShader,texture,false);
                particleMaterials[i]=MakeMaterial(spriteShader,texture,true);
            }
            smokeMaterial=MakeMaterial(spriteShader,Resources.Load<Texture2D>("Upgrades/Vfx/Smoke"),false);
            shieldMaterial=new Material(energyShader!=null?energyShader:spriteShader) {name="Upgrade energy barrier"};
            shieldMaterial.SetTexture("_MainTex",Resources.Load<Texture2D>("Upgrades/Vfx/Shield"));
            views=new View[Mathf.Max(16,capacity)];
            for(int i=0;i<views.Length;i++)
            {
                var go=new GameObject("UpgradeEffect");go.transform.SetParent(parent,false);
                var v=new View {Root=go.transform,Filter=go.AddComponent<MeshFilter>(),Renderer=go.AddComponent<MeshRenderer>()};
                v.Renderer.shadowCastingMode=ShadowCastingMode.Off;v.Renderer.receiveShadows=false;
                go.SetActive(false); views[i]=v;
            }
            var particleRoot=new GameObject("Upgrade particle reserve").transform;particleRoot.SetParent(parent,false);
            for(int i=0;i<rigs.Length;i++)
            {
                var go=new GameObject("Upgrade particle rig "+i);go.transform.SetParent(particleRoot,false);
                var rig=new ParticleRig {Root=go.transform,Seed=(uint)(0x98f321+i*977)};
                for(int j=0;j<3;j++)
                {
                    var layer=new GameObject(j==0?"Flash and frost":j==1?"Smoke and wind":"Sparks and fragments");layer.transform.SetParent(go.transform,false);
                    var ps=layer.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main=ps.main;main.playOnAwake=false;main.loop=true;main.duration=3;main.startLifetime=.5f;main.startSpeed=0;
                    main.maxParticles=96;main.simulationSpace=ParticleSystemSimulationSpace.World;main.scalingMode=ParticleSystemScalingMode.Shape;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
                    var emission=ps.emission;emission.enabled=false;
                    var shape=ps.shape;shape.enabled=false;
                    var color=ps.colorOverLifetime;color.enabled=true;color.color=FadeGradient();
                    var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1f,new AnimationCurve(new Keyframe(0,.7f),new Keyframe(.3f,1f),new Keyframe(1,.15f)));
                    var rotation=ps.rotationOverLifetime;rotation.enabled=true;rotation.z=new ParticleSystem.MinMaxCurve(-1.2f,1.2f);
                    var renderer=layer.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=particleMaterials[3];renderer.renderMode=ParticleSystemRenderMode.Billboard;
                    renderer.alignment=ParticleSystemRenderSpace.View;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.sortMode=ParticleSystemSortMode.Distance;
                    rig.Systems[j]=ps;
                }
                rig.Light=go.AddComponent<Light>();rig.Light.type=LightType.Point;rig.Light.shadows=LightShadows.None;rig.Light.enabled=false;rig.Light.renderMode=LightRenderMode.ForceVertex;
                rigs[i]=rig;
            }
        }
        private static Material MakeMaterial(Shader shader,Texture texture,bool additive)
        {
            var material=new Material(shader!=null?shader:Shader.Find("Universal Render Pipeline/Unlit")) {name="Upgrade "+(texture!=null?texture.name:"fallback")+(additive?" glow":" sprite")};
            material.SetTexture("_MainTex",texture!=null?texture:Texture2D.whiteTexture);
            material.SetColor("_Color",Color.white);material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",additive?(float)BlendMode.One:(float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_Glow",additive?1.3f:1f);return material;
        }
        private static Gradient FadeGradient()
        {
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.9f,.08f),new GradientAlphaKey(.65f,.5f),new GradientAlphaKey(0,1)});return gradient;
        }
        public View Acquire(Shape shape,Vector3 position,Color color,float scale=1f)
        {
            for(int i=0;i<views.Length;i++)
            {
                var v=views[i];if(v.Used)continue;
                v.Used=true;v.Transient=false;v.Age=0;v.Color=color;v.Shape=shape;v.Kind=BurstKind.Auto;v.Rig=null;
                v.Root.SetPositionAndRotation(position,Quaternion.identity);v.Root.localScale=Vector3.one*scale;
                ConfigureRenderer(v);
                if(shape==Shape.Decoy && collector!=null && collector.CharacterRenderer!=null)
                {
                    v.Filter.sharedMesh=collector.CharacterMesh;v.Renderer.sharedMaterial=collector.AfterimageMaterial;
                    collector.CopyAfterimageTo(v.Root,v.Renderer);v.Root.position+=position-collector.transform.position;
                    color.a=.4f;
                }
                SetColor(v,color);v.Root.gameObject.SetActive(true);ActiveCount++;
                if(Decorative && (shape==Shape.Dart || shape==Shape.Blade || shape==Shape.Saw || shape==Shape.Ice || shape==Shape.Shield || shape==Shape.Ring))
                {v.Rig=AcquireRig(v);ConfigureLoop(v);}
                return v;
            }
            return null;
        }
        private void ConfigureRenderer(View v)
        {
            v.Renderer.enabled=true;v.Filter.sharedMesh=groundQuad;
            int material=6;
            switch(v.Shape)
            {
                case Shape.Dart: material=3;break;
                case Shape.Saw: if(sawMaterial!=null){v.Renderer.sharedMaterial=sawMaterial;return;} material=0;break;
                case Shape.Mine: material=1;break;
                case Shape.Turret: material=2;v.Filter.sharedMesh=turretQuad;break;
                case Shape.Ice: material=4;break;
                case Shape.Blade: material=8;break;
                case Shape.Shield:v.Filter.sharedMesh=shieldMesh;v.Renderer.sharedMaterial=shieldMaterial;return;
                case Shape.Decoy:v.Filter.sharedMesh=uprightQuad;v.Renderer.sharedMaterial=flatMaterial;return;
            }
            v.Renderer.sharedMaterial=v.Shape==Shape.Dart||v.Shape==Shape.Blade?particleMaterials[material]:spriteMaterials[material];
        }
        public void SetShape(View v,Shape shape)
        {
            if(v==null || v.Shape==shape)return;
            ReleaseRig(v.Rig);v.Rig=null;v.Shape=shape;v.Age=0;ConfigureRenderer(v);SetColor(v,v.Color);
            if(Decorative){v.Rig=AcquireRig(v);ConfigureLoop(v);}
        }
        public void SetColor(View v,Color color)
        {
            if(v==null)return;v.Color=color;
            v.Renderer.GetPropertyBlock(block);
            bool art=v.Shape==Shape.Saw||v.Shape==Shape.Mine||v.Shape==Shape.Turret;
            Color renderColor=v.Shape==Shape.Saw&&sawMaterial!=null?sawMaterial.GetColor("_BaseColor"):art?Color.white:color;
            if(v.Shape==Shape.Ring || v.Shape==Shape.Ice)renderColor.a*=.62f;
            block.SetColor(Tint,renderColor);block.SetColor("_BaseColor",renderColor);block.SetFloat(Phase,v.Age);
            block.SetFloat("_Billboard",v.Shape==Shape.Turret?1f:0f);block.SetFloat(Glow,v.Shape==Shape.Shield?1.4f:1f);
            v.Renderer.SetPropertyBlock(block);
        }
        public void Release(View v)
        {
            if(v==null||!v.Used)return;
            ReleaseRig(v.Rig);v.Rig=null;v.Used=false;v.Root.gameObject.SetActive(false);ActiveCount--;
        }
        public void Burst(Vector3 p,float radius,Color color,bool inward=false,BurstKind kind=BurstKind.Auto)
        {
            if(kind!=BurstKind.Impact)p.y=ArenaSurface.Height(p)+.13f;
            if(kind==BurstKind.Auto)kind=inward?BurstKind.Implosion:color.g>color.r*1.5f?BurstKind.Repair:color.b>color.r*1.2f?BurstKind.Frost:BurstKind.Explosion;
            var v=Acquire(Shape.Wave,p,color);if(v==null)return;
            v.Transient=true;v.Kind=kind;v.Inward=inward||kind==BurstKind.Collection||kind==BurstKind.Implosion;
            v.Life=kind==BurstKind.Impact?.3f:kind==BurstKind.Collection?.75f:kind==BurstKind.Repair?.65f:.6f;
            if(preferences.ReducedEffectsMode)v.Life=Mathf.Min(v.Life,.25f);
            v.Radius=radius;v.Root.localScale=Vector3.one*Mathf.Max(.12f,radius*(v.Inward?1f:.12f));
            if(kind==BurstKind.Impact)v.Renderer.enabled=false;
            if(Decorative){v.Rig=AcquireRig(v);SeedBurst(v);}
        }
        public void Impact(Vector3 p,Color color,bool icy=false) => Burst(p,icy?.55f:.4f,color,false,BurstKind.Impact);
        public void Step(float dt)
        {
            if(dt<=0)return;
            for(int i=0;i<views.Length;i++)
            {
                var v=views[i];if(!v.Used)continue;v.Age+=dt;
                if(!Decorative&&v.Rig!=null){ReleaseRig(v.Rig);v.Rig=null;}
                if(Decorative&&!v.Transient&&v.Rig==null&&(v.Shape==Shape.Dart||v.Shape==Shape.Blade||v.Shape==Shape.Saw||v.Shape==Shape.Ice||v.Shape==Shape.Shield||v.Shape==Shape.Ring))
                {v.Rig=AcquireRig(v);ConfigureLoop(v);}
                if(v.Transient)
                {
                    float t=v.Age/v.Life;if(t>=1){Release(v);continue;}
                    float eased=1f-Mathf.Pow(1f-t,3f);
                    float radius=v.Inward?Mathf.Lerp(v.Radius,.12f,eased):Mathf.Lerp(v.Radius*.12f,v.Radius,eased);
                    v.Root.localScale=Vector3.one*Mathf.Max(.05f,radius);
                    var original=v.Color;var c=original;c.a*=Mathf.Pow(1f-t,1.4f)*.8f;SetColor(v,c);v.Color=original;
                }
                else
                {
                    if(v.Shape==Shape.Shield)SetColor(v,v.Color);
                    if(v.Shape==Shape.Mine)
                    {v.Renderer.GetPropertyBlock(block);block.SetFloat(Glow,1f+Mathf.Pow(Mathf.Max(0,Mathf.Sin(v.Age*8f)),12f)*.5f);v.Renderer.SetPropertyBlock(block);}
                    if(v.Rig!=null)StepLoop(v,dt);
                }
                if(v.Rig!=null)
                {
                    var rig=v.Rig;rig.Root.position=v.Root.position;
                    for(int j=0;j<3;j++){rig.Systems[j].Simulate(dt,false,false,false);rig.Systems[j].Pause(false);}
                    if(rig.Light.enabled)
                    {
                        rig.LightLife-=dt;
                        if(rig.LightLife<=0){rig.Light.enabled=false;ActiveLightCount--;}
                        else rig.Light.intensity=Mathf.Max(0,rig.LightLife)*18f*Intensity;
                    }
                }
            }
        }
        private ParticleRig AcquireRig(View owner)
        {
            if(!owner.Transient && owner.Shape!=Shape.Shield && ActiveParticleRigCount>=ParticleRigCapacity-8)return null;
            for(int i=0;i<rigs.Length;i++)if(rigs[i].Owner==null)
            {
                var rig=rigs[i];rig.Owner=owner;rig.Accumulator=0;rig.LightLife=0;rig.Root.position=owner.Root.position;
                for(int j=0;j<3;j++){rig.Systems[j].Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);rig.Systems[j].Pause(false);}
                ActiveParticleRigCount++;return rig;
            }
            return null;
        }
        private void ReleaseRig(ParticleRig rig)
        {
            if(rig==null||rig.Owner==null)return;
            for(int i=0;i<3;i++)rig.Systems[i].Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(rig.Light.enabled){rig.Light.enabled=false;ActiveLightCount--;}
            rig.Owner=null;ActiveParticleRigCount--;
        }
        private void Layer(ParticleRig rig,int index,int texture,bool ground=false,bool smoke=false)
        {
            var renderer=rig.Systems[index].GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial=smoke?smokeMaterial:particleMaterials[texture];
            renderer.renderMode=ground?ParticleSystemRenderMode.HorizontalBillboard:ParticleSystemRenderMode.Billboard;
        }
        private void ConfigureLoop(View v)
        {
            if(v.Rig==null)return;var rig=v.Rig;
            if(v.Shape==Shape.Ice){Layer(rig,0,4);Layer(rig,1,5,false,true);Layer(rig,2,3);EnableLight(rig,new Color(.3f,.65f,1f),3.2f,.3f);}
            else if(v.Shape==Shape.Saw){Layer(rig,0,8,true);Layer(rig,1,3);Layer(rig,2,3);}
            else if(v.Shape==Shape.Shield){Layer(rig,0,3);Layer(rig,1,7);Layer(rig,2,3);}
            else if(v.Shape==Shape.Ring){Layer(rig,0,v.Color.b>v.Color.r?4:3);Layer(rig,1,5,false,true);Layer(rig,2,3);}
            else{Layer(rig,0,3);Layer(rig,1,5,false,true);Layer(rig,2,3);EnableLight(rig,v.Color,2.5f,.18f);}
            StepLoop(v,.08f);
        }
        private void StepLoop(View v,float dt)
        {
            var rig=v.Rig;if(rig==null)return;
            float rate=(v.Shape==Shape.Ice?40f:v.Shape==Shape.Saw?16f:v.Shape==Shape.Shield?18f:v.Shape==Shape.Ring?7f:50f)*Intensity;
            if(v.Shape==Shape.Ice && rig.LightLife<.08f)EnableLight(rig,new Color(.3f,.65f,1f),3.2f,.25f);
            rig.Accumulator+=dt*rate;int count=Mathf.Min(12,(int)rig.Accumulator);rig.Accumulator-=count;
            for(int i=0;i<count;i++)
            {
                var p=v.Root.position;var direction=Radial(rig);float radius=Mathf.Max(.45f,v.Root.localScale.x);
                if(v.Shape==Shape.Ice)
                {
                    p+=direction*(radius*.75f);p.y+=Random(rig)*1.5f;
                    Emit(rig,0,p,new Vector3(direction.x*.18f,.35f,direction.z*.18f),.7f,.28f+Random(rig)*.3f,new Color(.7f,.9f,1f,.85f));
                    if(i%4==0)Emit(rig,1,v.Root.position+direction*.5f,Vector3.up*.3f,.85f,1.35f,new Color(.4f,.67f,.85f,.3f));
                }
                else if(v.Shape==Shape.Saw)
                {
                    Emit(rig,0,p,Vector3.up*.04f,.2f,1.7f,new Color(.75f,.88f,1f,.25f));
                    if(i%3==0)Emit(rig,2,p+direction*.35f,direction*.5f,.2f,.11f,new Color(1f,.82f,.4f,.7f));
                }
                else if(v.Shape==Shape.Shield)
                {
                    float y=Random(rig)*2f-1f;direction.y=y;
                    Emit(rig,0,p+direction.normalized*1.05f,Vector3.up*.3f,.65f,.11f,new Color(.45f,.95f,1f,.8f));
                }
                else if(v.Shape==Shape.Ring)
                {
                    Emit(rig,0,p+direction*radius,Vector3.up*.4f,.55f,.14f,v.Color);
                }
                else
                {
                    Emit(rig,0,p,Vector3.zero,.2f,.18f+Random(rig)*.12f,v.Color);
                    if(i%3==0)Emit(rig,2,p,Vector3.up*.15f,.16f,.1f,Color.white);
                }
            }
        }
        private void SeedBurst(View v)
        {
            var rig=v.Rig;if(rig==null)return;
            float radius=v.Radius;var center=v.Root.position+Vector3.up*.12f;var color=v.Color;
            bool cold=v.Kind==BurstKind.Frost, repair=v.Kind==BurstKind.Repair, inward=v.Inward;
            Layer(rig,0,cold?4:repair||inward?3:9);
            Layer(rig,1,5,false,true);Layer(rig,2,cold?4:3);
            int count=Mathf.RoundToInt((v.Kind==BurstKind.Impact?10:repair?14:inward?38:30)*Mathf.Lerp(.35f,1f,Intensity));
            if(!cold&&!repair&&!inward&&v.Kind!=BurstKind.Impact)
            {
                Emit(rig,0,center+Vector3.up*.4f,Vector3.up*.2f,.35f,radius*.9f,new Color(1f,.86f,.5f,.68f));
                Emit(rig,0,center+Vector3.up*.3f,Vector3.zero,.2f,radius*.55f,new Color(1f,.98f,.85f,.7f));
            }
            for(int i=0;i<count;i++)
            {
                var direction=Radial(rig);float distance=inward?radius*(.7f+Random(rig)*.3f):Random(rig)*radius*.25f;
                var p=center+direction*distance;p.y+=Random(rig)*(repair?1.2f:.6f);
                var velocity=inward?-direction*(radius*1.2f)+new Vector3(-direction.z,0,direction.x)*radius*.35f:direction*(radius*(.6f+Random(rig)*1.1f));
                velocity.y=repair?.7f:cold?.4f:.5f+Random(rig)*1.4f;
                float life=v.Kind==BurstKind.Impact?.2f+Random(rig)*.15f:.4f+Random(rig)*.35f;
                Emit(rig,2,p,velocity,life,cold?.2f+Random(rig)*.2f:repair?.12f:.12f+Random(rig)*.2f,color);
                if(!repair&&!inward&&i%4==0)Emit(rig,1,p,new Vector3(direction.x*.35f,.55f,direction.z*.35f),.65f,radius*.35f,new Color(cold?.42f:.24f,cold?.65f:.21f,cold?.75f:.2f,.24f));
            }
            EnableLight(rig,color,Mathf.Clamp(radius*1.5f,2,8),v.Kind==BurstKind.Impact?.12f:.22f);
        }
        private void EnableLight(ParticleRig rig,Color color,float range,float life)
        {
            if(rig==null||!Decorative)return;
            if(rig.Light.enabled){rig.LightLife=Mathf.Max(rig.LightLife,life);return;}
            if(ActiveLightCount>=LightCapacity)return;
            rig.Light.enabled=true;rig.Light.color=color;rig.Light.range=range;rig.Light.intensity=life*18f*Intensity;rig.LightLife=life;ActiveLightCount++;
        }
        private static float Random(ParticleRig rig)
        { uint value=rig.Seed;value^=value<<13;value^=value>>17;value^=value<<5;rig.Seed=value;return(value&0xffffff)/16777216f; }
        private static Vector3 Radial(ParticleRig rig)
        {float angle=Random(rig)*Mathf.PI*2;return new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));}
        private static void Emit(ParticleRig rig,int layer,Vector3 position,Vector3 velocity,float life,float size,Color color)
        {
            var emission=new ParticleSystem.EmitParams {position=position,velocity=velocity,startLifetime=life,startSize=size,startColor=color,rotation=Random(rig)*360f};
            rig.Systems[layer].Emit(emission,1);
        }
        public void Clear(){for(int i=0;i<views.Length;i++)Release(views[i]);}
        public void Dispose()
        {
            Clear();for(int i=0;i<views.Length;i++)if(views[i].Root!=null)Object.Destroy(views[i].Root.gameObject);
            for(int i=0;i<rigs.Length;i++)if(rigs[i].Root!=null)Object.Destroy(rigs[i].Root.gameObject);
            Object.Destroy(groundQuad);Object.Destroy(uprightQuad);Object.Destroy(turretQuad);Object.Destroy(shieldMesh);
            for(int i=0;i<spriteMaterials.Length;i++){Object.Destroy(spriteMaterials[i]);Object.Destroy(particleMaterials[i]);}
            Object.Destroy(shieldMaterial);Object.Destroy(smokeMaterial);
            if(flatMaterial!=null&&!Resources.Load<Material>("Upgrades/UpgradeEffects"))Object.Destroy(flatMaterial);
        }
        private static Mesh Quad(bool upright,bool bottomPivot)
        {
            float low=bottomPivot?-.24f:-1f,high=bottomPivot?1.76f:1f;
            var vertices=upright?new[]{new Vector3(-1,low,0),new Vector3(1,low,0),new Vector3(1,high,0),new Vector3(-1,high,0)}:new[]{new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(1,0,1),new Vector3(-1,0,1)};
            var mesh=new Mesh{name=upright?"Upgrade upright sprite":"Upgrade ground sprite",vertices=vertices,triangles=new[]{0,2,1,0,3,2},uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)},colors=new[]{Color.white,Color.white,Color.white,Color.white}};mesh.RecalculateNormals();mesh.RecalculateTangents();return mesh;
        }
        private static Mesh Sphere()
        {
            const int rings=16,segments=32;var vertices=new Vector3[(rings+1)*(segments+1)];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];var colors=new Color[vertices.Length];var triangles=new int[rings*segments*6];
            for(int y=0;y<=rings;y++)for(int x=0;x<=segments;x++)
            {float v=(float)y/rings,u=(float)x/segments;float a=u*Mathf.PI*2,b=v*Mathf.PI;int i=y*(segments+1)+x;var normal=new Vector3(Mathf.Sin(b)*Mathf.Cos(a),Mathf.Cos(b),Mathf.Sin(b)*Mathf.Sin(a));normals[i]=normal;vertices[i]=Vector3.Scale(normal,new Vector3(1f,1.25f,1f));uv[i]=new Vector2(u,v);colors[i]=Color.white;}
            for(int y=0;y<rings;y++)for(int x=0;x<segments;x++){int a=y*(segments+1)+x,b=a+segments+1,j=(y*segments+x)*6;triangles[j]=a;triangles[j+1]=b;triangles[j+2]=a+1;triangles[j+3]=a+1;triangles[j+4]=b;triangles[j+5]=b+1;}
            var mesh=new Mesh{name="Nanobot energy envelope",vertices=vertices,normals=normals,uv=uv,triangles=triangles,colors=colors};mesh.RecalculateBounds();return mesh;
        }
    }
}
