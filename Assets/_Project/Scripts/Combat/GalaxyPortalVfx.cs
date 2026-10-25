using UnityEngine;

namespace EchoScrap.Combat
{
    public sealed class GalaxyPortalVfx : MonoBehaviour
    {
        public Material SparkMaterial;
        private void Start()
        {
            var child=new GameObject("PolvoEstelar"); child.transform.SetParent(transform,false);
            var particles=child.AddComponent<ParticleSystem>(); particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=particles.main; main.loop=true; main.startLifetime=3f; main.startSpeed=0.015f;
            main.startSize=new ParticleSystem.MinMaxCurve(0.008f,0.025f); main.maxParticles=180;
            main.simulationSpace=ParticleSystemSimulationSpace.Local;
            main.startColor=new ParticleSystem.MinMaxGradient(new Color(0.5f,0.45f,1f),new Color(1f,0.5f,1f));
            var emission=particles.emission; emission.rateOverTime=45;
            var shape=particles.shape; shape.shapeType=ParticleSystemShapeType.Circle; shape.radius=0.44f; shape.radiusThickness=1f;
            var velocity=particles.velocityOverLifetime; velocity.enabled=true; velocity.space=ParticleSystemSimulationSpace.Local;
            velocity.orbitalZ=0.7f;
            var color=particles.colorOverLifetime; color.enabled=true;
            var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,0.2f),new GradientAlphaKey(0,1)}); color.color=gradient;
            var renderer=particles.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=SparkMaterial;
            particles.Play();
        }
    }
}
