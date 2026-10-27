using UnityEngine;
namespace EchoScrap.Combat
{
    public sealed class FusionRevealSmoke : MonoBehaviour
    {
        private ParticleSystem smoke;
        private Material material;
        private Texture2D texture;
        public int RevealCount { get; private set; }
        public void Play(Vector3 position)
        {
            if(smoke==null){
                texture=new Texture2D(32,32,TextureFormat.RGBA32,false);
                for(int y=0;y<32;y++)for(int x=0;x<32;x++){
                    float radius=Vector2.Distance(new Vector2(x,y),Vector2.one*15.5f)/15.5f;
                    texture.SetPixel(x,y,new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-radius),2)));
                }
                texture.Apply();texture.wrapMode=TextureWrapMode.Clamp;
                material=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);
                material.SetFloat("_Surface",1);material.SetFloat("_SrcBlend",5);material.SetFloat("_DstBlend",10);material.SetFloat("_ZWrite",0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=3000;
                var child=new GameObject("HumoApertura");child.transform.SetParent(transform,false);smoke=child.AddComponent<ParticleSystem>();
                smoke.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=smoke.main;main.loop=false;main.useUnscaledTime=true;main.startLifetime=1.3f;main.startSpeed=new ParticleSystem.MinMaxCurve(.8f,1.5f);
                main.startSize=new ParticleSystem.MinMaxCurve(1.4f,2.4f);main.startColor=new Color(.6f,.52f,.72f,.45f);main.maxParticles=40;main.simulationSpace=ParticleSystemSimulationSpace.World;
                var emission=smoke.emission;emission.rateOverTime=0;
                var shape=smoke.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.radius=1.5f;shape.angle=20;
                var color=smoke.colorOverLifetime;color.enabled=true;
                var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.15f),new GradientAlphaKey(0,1)});color.color=gradient;
                smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial=material;
            }
            smoke.transform.position=position+Vector3.up*.8f;smoke.transform.rotation=Quaternion.Euler(-90,0,0);
            smoke.Play();smoke.Emit(28);RevealCount++;
        }
        private void OnDestroy(){if(material!=null)Destroy(material);if(texture!=null)Destroy(texture);}
    }
}
