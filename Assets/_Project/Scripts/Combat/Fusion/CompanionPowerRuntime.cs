using System;
using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    // Equipment changes never reset expedition cooldowns or restore spent rescues.
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
            "Onda cada 15 s: empuja y cura 5% de vida máxima", "+15 de vida máxima · −15% daño recibido · +10% armadura",
            "+15% velocidad de ataque"
        };
        public static readonly string[] HudDescriptions = {
            "+10 VIDA MÁXIMA", "PROYECTIL CADA 10 S", "+15% DAÑO · QUEMADURA",
            "CURA 2 / 5 S", "VIDA ×2 · SALVACIÓN", "+10% VELOCIDAD",
            "BLOQUEA 1 GOLPE / 20 S", "+20% ATAQUE · CONGELA", "+8% DAÑO",
            "DASH: VELOCIDAD ×2", "CURA 1 / 5 BAJAS", "ONDA: DAÑO Y LENTITUD",
            "ONDA: EMPUJA Y CURA", "+15 VIDA · −15% DAÑO · +10% ARMADURA", "+15% VELOCIDAD DE ATAQUE"
        };
        internal static string DescribeHudPower(int id, int copies)
        {
            if (copies == 1) return HudDescriptions[id];

            return id switch
            {
                0 => $"+{10 * copies} VIDA MÁXIMA",
                1 => $"PROYECTIL: {18 * copies} DAÑO / 10 S",
                2 => $"+{15 * copies}% DAÑO · HASTA {4 * copies} DAÑO/S DE QUEMADURA",
                3 => $"CURA {2 * copies} / 5 S",
                4 => $"VIDA ×{1 + copies} · +{20 * copies}% DAÑO · +{15 * copies}% VEL. · {copies} SALVACIONES",
                5 => $"+{10 * copies}% VELOCIDAD",
                6 => $"BLOQUEA {copies} GOLPES · RECARGA 20 S C/U",
                7 => $"+{20 * copies}% ATAQUE · CONGELA {2 * copies} S / 12 S",
                8 => $"+{8 * copies}% DAÑO",
                9 => $"DASH: VELOCIDAD ×{1 + copies} DURANTE 2 S",
                10 => $"CURA {copies} / 5 BAJAS",
                11 => $"ONDA: {15 * copies} DAÑO · −{Mathf.Min(100, 40 * copies)}% VEL. / 8 S",
                12 => $"ONDA: EMPUJA {4 * copies} M · CURA {5 * copies}% / 15 S",
                13 => $"+{15 * copies} VIDA · −{15 * copies}% DAÑO RECIBIDO · +{10 * copies}% ARMADURA",
                14 => $"+{15 * copies}% VELOCIDAD DE ATAQUE",
                _ => ""
            };
        }

        private const int EquipmentSlots = 3;
        private readonly int[] equipped = new int[FusionCatalog.Count];
        private readonly float[] shieldCooldowns = new float[EquipmentSlots];
        private readonly float[] cooldowns = new float[FusionCatalog.Count];
        private readonly List<EnemyActor> targets = new List<EnemyActor>(256);
        private sealed class Burn { public EnemyActor Enemy; public int Version, Ticks, CopyIndex; public float Next; }
        private readonly List<Burn> burns = new List<Burn>(64);
        private readonly Dictionary<int,int> boltHits = new Dictionary<int,int>();
        private CombatGameBootstrap game;
        private CompanionPixelEffects effects;
        private float speedBuff, boltLife;
        private Vector3 boltPosition, boltDirection;
        private int kills, rescuesUsed;
        private bool dealingDamage;
        public bool AutoTick = true;
        public bool RescueUsed => rescuesUsed > 0;
        public bool ShieldReady => Has(6) && ShieldCooldown <= 0;
        public float ShieldCooldown
        {
            get
            {
                float next = shieldCooldowns[0];
                for (int i = 1; i < Copies(6); i++)
                    next = Mathf.Min(next, shieldCooldowns[i]);
                return next;
            }
        }

        public bool Has(int id) => Copies(id) > 0;
        private int Copies(int id) => FusionCatalog.Valid(id) ? equipped[id] : 0;
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
            Array.Clear(equipped, 0, equipped.Length);
            for (int slot = 0; slot < Mathf.Min(ids.Length, EquipmentSlots); slot++)
            {
                int id = ids[slot];
                if (FusionCatalog.Valid(id)) equipped[id]++;
            }
            for (int i = burns.Count - 1; i >= 0; i--)
                if (burns[i].CopyIndex >= Copies(2)) burns.RemoveAt(i);
            if(!Has(1)){boltLife=0;effects.SetBolt(boltPosition,false);}
            if(!Has(9))speedBuff=0;
            RefreshStats();
        }
        private void RefreshStats()
        {
            int soldiers = Copies(13);
            int yanikuas = Copies(FusionCatalog.Yanikua);
            game.Stats.SetCompanionBonuses(
                health: 10 * Copies(0) + 15 * soldiers,
                healthMultiplier: 1 + yanikuas,
                damage: .15f * Copies(2) + .08f * Copies(8) + .2f * yanikuas,
                speed: (1 + .1f * Copies(5) + .15f * yanikuas) * (1 + (speedBuff > 0 ? Copies(9) : 0)),
                attackSpeed: .15f * Copies(14) + .2f * Copies(7),
                reduction: .15f * soldiers,
                armor: .10f * soldiers);
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
            for (int i = 0; i < Copies(6); i++)
                shieldCooldowns[i] = Mathf.Max(0, shieldCooldowns[i] - dt);
            TickBurns(dt); TickBolt(dt);
            if(Ready(1,10)) LaunchBolt();
            if(Ready(3,5)) Heal(2 * Copies(3));
            if(Ready(7,12)) Pulse(7);
            if(Ready(11,8)) Pulse(11);
            if(Ready(12,15)){Pulse(12);Heal(game.Player.MaxHealth * .05f * Copies(12));}
        }
        private bool Ready(int id,float period)
        {
            if(!Has(id)||cooldowns[id]>0)return false;
            cooldowns[id]=period;return true;
        }
        public bool TryBlockHit()
        {
            if (!CanFight) return false;
            for (int i = 0; i < Copies(6); i++)
            {
                if (shieldCooldowns[i] > 0) continue;
                shieldCooldowns[i] = 20;
                effects.Emit(0, transform.position, 2.6f, new Color(.6f, .3f, 1));
                return true;
            }
            return false;
        }
        public bool TrySaveLife()
        {
            // Health is being resolved, so IsAlive is temporarily false here.
            if (rescuesUsed >= Copies(FusionCatalog.Yanikua) || game.Session.IsInWarehouse || game.Session.IsPaused)
                return false;
            rescuesUsed++;
            game.Player.GrantUpgradeProtection(3);
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
            if(++kills>=5){kills-=5;Heal(Copies(10));}
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
                hit.SourceType>=DamageSourceType.UpgradeProjectile&&hit.SourceType<=DamageSourceType.UpgradeBlade))return;
            EnemyActor enemy = null;
            for (int copy = 0; copy < Copies(2); copy++)
            {
                if (game.Session.Rng.Combat.NextFloat() >= .3f) continue;
                if (enemy == null)
                {
                    foreach (var target in EnemyActor.ActiveEnemies)
                    {
                        if (target.TargetId != hit.TargetId) continue;
                        enemy = target;
                        break;
                    }
                    if (enemy == null || !enemy.IsAlive) return;
                }
                Ignite(enemy, copy);
            }
        }

        private void Ignite(EnemyActor enemy, int copy)
        {
            foreach (var burn in burns)
            {
                if (burn.Enemy != enemy || burn.Version != enemy.SpawnVersion || burn.CopyIndex != copy) continue;
                burn.Ticks = 3;
                return;
            }
            burns.Add(new Burn { Enemy = enemy, Version = enemy.SpawnVersion, Ticks = 3, CopyIndex = copy, Next = 1 });
        }
        private void TickBurns(float dt)
        {
            for(int i=burns.Count-1;i>=0;i--){var b=burns[i];
                if(b.Enemy==null||!b.Enemy.IsAlive||b.Enemy.SpawnVersion!=b.Version){burns.RemoveAt(i);continue;}
                b.Next-=dt;
                while(b.Next<=0&&b.Ticks>0&&b.Enemy.IsAlive){b.Next+=1;b.Ticks--;Deal(b.Enemy, 4, b.Enemy.WorldCenter);effects.Emit(3,b.Enemy.WorldCenter,1.3f,new Color(1,.3f,.05f));}
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
            Gather();
            var center = transform.position;
            int copies = Copies(id);
            effects.Emit(id == 7 ? 4 : 5, center, 10, FusionCatalog.ColorOf(id));
            foreach (var enemy in targets)
            {
                if (!enemy.IsAlive || Vector3.Distance(center, enemy.WorldCenter) > 8) continue;
                if (id == 7)
                {
                    if (enemy.ArchetypeId == EnemyArchetypeId.GolgoraBoss) enemy.ApplySlow(.5f, 2 * copies);
                    else enemy.ApplyFreeze(2 * copies);
                }
                else if (id == 11)
                {
                    Deal(enemy, 15 * copies, center);
                    if (enemy.IsAlive) enemy.ApplySlow(.4f * copies, 3);
                }
                else enemy.PushDistance(enemy.WorldCenter - center, 4 * copies);
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
                boltHits[enemy.TargetId]=enemy.SpawnVersion;Deal(enemy, 18 * Copies(1), from);
            }
            boltPosition=to;effects.SetBolt(to,boltLife>0&&!blocked);if(blocked)boltLife=0;
        }
        private void ResetExpedition()
        {
            Array.Clear(cooldowns,0,cooldowns.Length);cooldowns[1]=10;cooldowns[3]=5;cooldowns[7]=12;cooldowns[11]=8;cooldowns[12]=15;
            Array.Clear(shieldCooldowns, 0, shieldCooldowns.Length);
            kills = rescuesUsed = 0;
            speedBuff = boltLife = 0;
            burns.Clear();
            effects.Clear();
            RefreshStats();
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

