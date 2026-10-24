using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Lava transitable: cinco de daño por contacto y cada segundo dentro.</summary>
    public sealed class LavaHazard : MonoBehaviour
    {
        public Vector2 Radii = new Vector2(3f, 2f);
        public bool AutoTick = true;
        public float AnimationTime { get; private set; }
        private CombatGameBootstrap game;
        private Renderer[] renderers;
        private MaterialPropertyBlock properties;
        private readonly Dictionary<int,float> nextDamage = new Dictionary<int,float>();

        private void Start()
        {
            game=FindAnyObjectByType<CombatGameBootstrap>();
            renderers=GetComponentsInChildren<Renderer>();
            properties=new MaterialPropertyBlock();
            if(game!=null) game.Session.SessionReset+=ResetHazard;
        }
        private void OnDestroy()
        { if(game!=null && game.Session!=null) game.Session.SessionReset-=ResetHazard; }
        private void ResetHazard() { nextDamage.Clear(); AnimationTime=0f; }
        private void Update() { if(AutoTick) StepHazard(Time.deltaTime); }
        public bool Contains(Vector3 position, float radius=0f)
        {
            Vector3 local=transform.InverseTransformPoint(position);
            float x=local.x/(Radii.x+radius), z=local.z/(Radii.y+radius);
            return x*x+z*z<=1f && Mathf.Abs(position.y-ArenaSurface.Height(position))<4f;
        }
        public void StepHazard(float deltaTime)
        {
            if(game==null || deltaTime<=0 || game.Session.IsPaused || !game.Session.IsGameplayFlowActive()) return;
            AnimationTime+=deltaTime;
            // Desplazamiento continuo del magma y emisión pulsante, calculados en Unity.
            properties.SetVector("_BaseMap_ST",new Vector4(1,1,AnimationTime*0.012f,AnimationTime*0.008f));
            properties.SetColor("_EmissionColor",new Color(2.8f,1.1f,0.35f)*(1f+0.22f*Mathf.Sin(AnimationTime*2.5f)));
            foreach(var renderer in renderers) renderer.SetPropertyBlock(properties);
            Touch(game.Player);
            var targets=CombatTargetRegistry.ActiveTargets;
            for(int i=targets.Count-1;i>=0;i--)
                if(i<targets.Count && targets[i].IsEnemyTarget) Touch(targets[i]);
        }
        private void Touch(IDamageTarget target)
        {
            if(target==null || !target.IsAlive) return;
            if(!Contains(target.WorldCenter,target.HitRadius*0.5f))
            { nextDamage.Remove(target.TargetId); return; }
            if(nextDamage.TryGetValue(target.TargetId,out float next) && AnimationTime<next) return;
            nextDamage[target.TargetId]=AnimationTime+1f;
            var hit=new DamageRequest(GetHashCode(),DamageSourceType.Lava,transform.position,Vector3.zero,5f);
            target.ReceiveDamage(in hit);
        }
    }
}
