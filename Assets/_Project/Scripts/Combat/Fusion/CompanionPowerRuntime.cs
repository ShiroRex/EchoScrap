using System;
using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    // Equipment changes never reset expedition cooldowns or the mythical rescue.
    public sealed class CompanionPowerRuntime : MonoBehaviour
    {
        public static readonly string[] Descriptions = {
            "+10 de vida máxima", "Proyectil mágico perforante cada 10 s",
            "+15% daño · 30% de quemar: 4 daño/s durante 3 s", "+2 de vida cada 5 s",
            "Vida máxima ×2 · +20% daño · +15% velocidad · 1 salvación por expedición",
            "+10% velocidad de movimiento", "Bloquea un golpe · recarga de 20 s",
            "+20% velocidad de ataque · congela 2 s cada 12 s (ralentiza jefes)",
            "+8% daño", "Velocidad ×2 durante 2 s después del dash",
            "+1 de vida por cada 5 bajas", "Onda cada 8 s: 15 daño y ralentización de 40% durante 3 s",
            "Onda cada 15 s: empuja y cura 5% de vida máxima", "+15 de vida máxima · −15% daño recibido",
            "+15% velocidad de ataque"
        };
        public static readonly string[] HudDescriptions = {
            "+10 VIDA MÁXIMA", "PROYECTIL CADA 10 S", "+15% DAÑO · QUEMADURA",
            "CURA 2 / 5 S", "VIDA ×2 · SALVACIÓN", "+10% VELOCIDAD",
            "BLOQUEA 1 GOLPE / 20 S", "+20% ATAQUE · CONGELA", "+8% DAÑO",
            "DASH: VELOCIDAD ×2", "CURA 1 / 5 BAJAS", "ONDA: DAÑO Y LENTITUD",
            "ONDA: EMPUJA Y CURA", "+15 VIDA · −15% DAÑO", "+15% VELOCIDAD DE ATAQUE"
        };
        private readonly bool[] equipped = new bool[FusionCatalog.Count];
        private readonly float[] cooldowns = new float[FusionCatalog.Count];
        private readonly List<EnemyActor> targets = new List<EnemyActor>(256);
        private sealed class Burn { public EnemyActor Enemy; public int Version, Ticks; public float Next; }
        private readonly List<Burn> burns = new List<Burn>(64);
        private readonly Dictionary<int,int> boltHits = new Dictionary<int,int>();
        private CombatGameBootstrap game;
        private CompanionPixelEffects effects;
        private float speedBuff, boltLife;
        private Vector3 boltPosition, boltDirection;
        private int kills;
        private bool dealingDamage;
        public bool AutoTick = true;
        public bool RescueUsed { get; private set; }
        public bool ShieldReady => Has(6) && cooldowns[6] <= 0;
        public float ShieldCooldown => cooldowns[6];
        public bool Has(int id) => id >= 0 && id < equipped.Length && equipped[id];
        public void Initialize(CombatGameBootstrap owner)
        {
            game=owner; game.Player.CompanionPowers=this;
            effects=gameObject.AddComponent<CompanionPixelEffects>();effects.Initialize();
            game.Session.Events.OnDamageResolved+=OnDamage;
            game.Session.Events.OnActorDied+=OnDeath;
            game.Motor.OnDashStateChanged+=OnDash;
            game.Session.SessionReset+=ResetExpedition;
            ResetExpedition();
        }
        public void SetEquipment(int[] ids)
        {
            Array.Clear(equipped,0,equipped.Length);
            foreach(int id in ids) if(FusionCatalog.Valid(id)) equipped[id]=true;
            if(!Has(2))burns.Clear();
            if(!Has(1)){boltLife=0;effects.SetBolt(boltPosition,false);}
            if(!Has(9))speedBuff=0;
            RefreshStats();
        }
        private void RefreshStats()
        {
            game.Stats.SetCompanionBonuses((Has(0)?10:0)+(Has(13)?15:0),Has(4)?2:1,
                (Has(2)?.15f:0)+(Has(8)?.08f:0)+(Has(4)?.2f:0),
                (1+(Has(5)?.1f:0)+(Has(4)?.15f:0))*(speedBuff>0?2:1),
                (Has(14)?.15f:0)+(Has(7)?.2f:0),Has(13)?.15f:0);
        }
        private bool CanFight => game!=null && game.Player.IsAlive && !game.Session.IsPaused &&
            !game.Session.IsInWarehouse && game.Session.State==SessionState.Running;
        private void Update()
        {
            if(AutoTick) Advance(Time.deltaTime);
            effects.SetShield(ShieldReady && game.Player.IsAlive && !game.Session.IsInWarehouse);
        }
        public void Advance(float dt)
        {
            if(!CanFight || dt<=0 || float.IsNaN(dt) || float.IsInfinity(dt))return;
            effects.Advance(dt);
            if(speedBuff>0){speedBuff=Mathf.Max(0,speedBuff-dt);if(speedBuff==0)RefreshStats();}
            for(int i=0;i<cooldowns.Length;i++) if(Has(i))cooldowns[i]=Mathf.Max(0,cooldowns[i]-dt);
            TickBurns(dt); TickBolt(dt);
            if(Ready(1,10)) LaunchBolt();
            if(Ready(3,5)) Heal(2);
            if(Ready(7,12)) Pulse(7);
            if(Ready(11,8)) Pulse(11);
            if(Ready(12,15)){Pulse(12);Heal(game.Player.MaxHealth*.05f);}
        }
        private bool Ready(int id,float period)
        {
            if(!Has(id)||cooldowns[id]>0)return false;
            cooldowns[id]=period;return true;
        }
        public bool TryBlockHit()
        {
            if(!CanFight||!ShieldReady)return false;
            cooldowns[6]=20;effects.Emit(0,transform.position,2.6f,new Color(.6f,.3f,1));return true;
        }
        public bool TrySaveLife()
        {
            // Health is being resolved, so IsAlive is temporarily false here.
            if(!Has(4)||RescueUsed||game.Session.IsInWarehouse||game.Session.IsPaused)return false;
            RescueUsed=true;game.Player.GrantUpgradeProtection(3);
            effects.Emit(7,transform.position,4,new Color(1,.72f,.15f));return true;
        }
        private void OnDash(DashStateEvent e)
        {
            if(!e.Ended||!Has(9)||!CanFight)return;
            speedBuff=2;RefreshStats();effects.Emit(6,transform.position,2.4f,new Color(.2f,.55f,1));
        }
        private void OnDeath(ActorDeathEvent e)
        {
            if(e.IsPlayer||!Has(10)||!CanFight)return;
            if(++kills>=5){kills-=5;Heal(1);}
        }
        private void Heal(float amount)
        {
            if(game.Player.Heal(amount).ActualHealed>0)effects.Emit(1,transform.position,1.8f,new Color(.3f,1,.55f));
        }
        private void Gather()
        {
            targets.Clear();foreach(var e in EnemyActor.ActiveEnemies)if(e!=null&&e.IsAlive&&e.CurrentHealth>0)targets.Add(e);
        }
        private void OnDamage(DamageResult hit)
        {
            if(dealingDamage||!Has(2)||!CanFight||hit.IsPlayerTarget||!hit.AppliedRealDamage||hit.CausedDeath||
                !(hit.SourceType==DamageSourceType.FlashlightPulse||hit.SourceType==DamageSourceType.SolarPulse||
                hit.SourceType>=DamageSourceType.UpgradeProjectile&&hit.SourceType<=DamageSourceType.UpgradeBlade)||
                game.Session.Rng.Combat.NextFloat()>=.3f)return;
            EnemyActor enemy=null;foreach(var e in EnemyActor.ActiveEnemies)if(e.TargetId==hit.TargetId){enemy=e;break;}
            if(enemy==null||!enemy.IsAlive)return;
            foreach(var burn in burns)if(burn.Enemy==enemy&&burn.Version==enemy.SpawnVersion){burn.Ticks=3;return;}
            burns.Add(new Burn{Enemy=enemy,Version=enemy.SpawnVersion,Ticks=3,Next=1});
        }
        private void TickBurns(float dt)
        {
            for(int i=burns.Count-1;i>=0;i--){var b=burns[i];
                if(b.Enemy==null||!b.Enemy.IsAlive||b.Enemy.SpawnVersion!=b.Version){burns.RemoveAt(i);continue;}
                b.Next-=dt;
                while(b.Next<=0&&b.Ticks>0&&b.Enemy.IsAlive){b.Next+=1;b.Ticks--;Deal(b.Enemy,4,b.Enemy.WorldCenter);effects.Emit(3,b.Enemy.WorldCenter,1.3f,new Color(1,.3f,.05f));}
                if(b.Ticks==0)burns.RemoveAt(i);
            }
        }
        private void Deal(EnemyActor enemy,float amount,Vector3 origin)
        {
            dealingDamage=true;
            try {var request=new DamageRequest(game.Player.TargetId,DamageSourceType.UpgradeProjectile,origin,
                (enemy.WorldCenter-origin).normalized,amount,damageType:DamageType.Supernatural);enemy.ReceiveDamage(in request);}
            finally{dealingDamage=false;}
        }
        private void Pulse(int id)
        {
            Gather();var center=transform.position;
            effects.Emit(id==7?4:5,center,10,FusionCatalog.ColorOf(id));
            foreach(var enemy in targets){if(!enemy.IsAlive||Vector3.Distance(center,enemy.WorldCenter)>8)continue;
                if(id==7){if(enemy.ArchetypeId==EnemyArchetypeId.GolgoraBoss)enemy.ApplySlow(.5f,2);else enemy.ApplyFreeze(2);}
                else if(id==11){Deal(enemy,15,center);if(enemy.IsAlive)enemy.ApplySlow(.4f,3);}
                else enemy.PushDistance(enemy.WorldCenter-center,4);
            }
        }
        private void LaunchBolt()
        {
            Gather();EnemyActor nearest=null;float distance=18;
            foreach(var enemy in targets){float d=Vector3.Distance(transform.position,enemy.WorldCenter);if(d<distance){nearest=enemy;distance=d;}}
            if(nearest==null){cooldowns[1]=.5f;return;}
            boltPosition=transform.position;boltDirection=(nearest.WorldCenter-boltPosition);boltDirection.y=0;boltDirection.Normalize();boltLife=1.5f;boltHits.Clear();
        }
        private void TickBolt(float dt)
        {
            if(boltLife<=0)return;
            Vector3 from=boltPosition,to=from+boltDirection*18*Mathf.Min(dt,boltLife);boltLife-=dt;
            bool blocked=Physics.Linecast(from,to,out var wall,1,QueryTriggerInteraction.Ignore);if(blocked)to=wall.point;
            Gather();Vector3 segment=to-from;float length=segment.sqrMagnitude;
            foreach(var enemy in targets){if(!enemy.IsAlive||boltHits.TryGetValue(enemy.TargetId,out int version)&&version==enemy.SpawnVersion)continue;
                float t=length>0?Mathf.Clamp01(Vector3.Dot(enemy.WorldCenter-from,segment)/length):0;
                if((enemy.WorldCenter-(from+segment*t)).sqrMagnitude>Mathf.Pow(enemy.HitRadius+.3f,2))continue;
                boltHits[enemy.TargetId]=enemy.SpawnVersion;Deal(enemy,18,from);
            }
            boltPosition=to;effects.SetBolt(to,boltLife>0&&!blocked);if(blocked)boltLife=0;
        }
        private void ResetExpedition()
        {
            Array.Clear(cooldowns,0,cooldowns.Length);cooldowns[1]=10;cooldowns[3]=5;cooldowns[7]=12;cooldowns[11]=8;cooldowns[12]=15;
            kills=0;speedBuff=boltLife=0;RescueUsed=false;burns.Clear();effects.Clear();RefreshStats();
        }
        private void OnDestroy()
        {
            if(game==null)return;
            game.Session.Events.OnDamageResolved-=OnDamage;game.Session.Events.OnActorDied-=OnDeath;
            game.Motor.OnDashStateChanged-=OnDash;game.Session.SessionReset-=ResetExpedition;
            if(game.Player!=null&&game.Player.CompanionPowers==this)game.Player.CompanionPowers=null;
        }
    }
}

