using UnityEngine;
namespace EchoScrap.Combat
{
    /// <summary>Chispas que ascienden por los costados, dejando libre la puerta.</summary>
    public sealed class FusionAura : MonoBehaviour
    {
        private Material material;
        private void Start()
        {
            material=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            material.SetColor("_BaseColor",Color.white);material.SetFloat("_Surface",1);material.SetFloat("_Blend",2);material.SetFloat("_SrcBlend",5);material.SetFloat("_DstBlend",1);material.SetFloat("_ZWrite",0);material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=3000;
            for(int i=0;i<3;i++){
                var go=new GameObject("AuraParticulas_"+i);go.transform.SetParent(transform,false);
                go.transform.localPosition=i==0?new Vector3(-4,.3f,0):i==1?new Vector3(5,.3f,0):new Vector3(.5f,.3f,3);
                go.transform.localRotation=Quaternion.Euler(-90,0,0);
                var system=go.AddComponent<ParticleSystem>();var main=system.main;
                main.startLifetime=new ParticleSystem.MinMaxCurve(1.5f,3);main.startSpeed=new ParticleSystem.MinMaxCurve(.8f,1.8f);main.startSize=new ParticleSystem.MinMaxCurve(.06f,.18f);main.startColor=new Color(.7f,.35f,1,.65f);main.maxParticles=55;
                var shape=system.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=i==2?new Vector3(8,.5f,.2f):new Vector3(.5f,6,.2f);
                var emission=system.emission;emission.rateOverTime=14;
                var color=system.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(new Color(.5f,.15f,1),0),new GradientColorKey(new Color(.9f,.65f,1),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.7f,.2f),new GradientAlphaKey(0,1)});color.color=gradient;
                system.GetComponent<ParticleSystemRenderer>().sharedMaterial=material;
            }
        }
        private void OnDestroy(){if(material!=null)Destroy(material);}
    }
}
