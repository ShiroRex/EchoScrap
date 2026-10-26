using System.Collections.Generic;
using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Run-scoped simulation of the complete upgrade catalogue. Events enqueue attacks;
    /// damage is drained after the originating hit, never while the weapon iterates its targets.</summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeCombatRuntime : MonoBehaviour
    {
        private sealed class Projectile
        {
            public bool Active, Piercing;
            public Vector3 Position, Direction;
            public float Damage, Speed, Life, Freeze, Radius;
            public DamageSourceType Source;
            public EnemyActor Target;
            public int TargetVersion;
            public readonly Dictionary<int,int> Hits = new Dictionary<int,int>(32);
            public UpgradeEffectPool.View View;
        }
        private enum DeviceKind { Mine, Decoy, Turret }
        private sealed class Device
        {
            public bool Active, Fresh;
            public DeviceKind Kind;
            public Vector3 Position;
            public float Life, Damage, ShotTimer, BurstTimer;
            public int Shots;
            public UpgradeEffectPool.View View;
        }
        private struct Blast
        {
            public Vector3 Position;
            public float Damage, Radius, Delay, Stun, Push, Freeze;
            public bool Implosion, Echo;
            public DamageSourceType Source;
        }
        private sealed class StatusView
        {
            public EnemyActor Actor;
            public int Version;
            public UpgradeEffectPool.View View;
        }
        private CombatSession session;
        private PlayerRuntimeStats stats;
        private PlayerHealth player;
        private FlashlightWeapon weapon;
        private CombatPickupPool pickups;
        private CombatProgression progression;
        private UpgradeTuning tuning;
        private UpgradeEffectPool effects;
        private Projectile[] projectiles;
        private Device[] devices;
        private Blast[] blasts;
        private int blastCount;
        private StatusView[] statuses;
        private readonly int[] attackTiers = new int[UpgradeRules.Count];
        private readonly List<EnemyActor> targets = new List<EnemyActor>(512);
        private readonly HashSet<int> selectedTargets = new HashSet<int>();
        private readonly Dictionary<int,float> sawNextHit = new Dictionary<int,float>(512);
        private readonly UpgradeEffectPool.View[] sawViews = new UpgradeEffectPool.View[4];
        private UpgradeEffectPool.View shield;
        private float time, mineTimer, blizzardTimer, turretTimer, regenTimer, emergencyReady, overheal;
        private int gems, activeDecoys;
        private bool bound, collecting;
        private static readonly Color Amber = new Color(1f,.66f,.22f);
        private static readonly Color Plasma = new Color(.35f,.94f,1f);
        private static readonly Color Ice = new Color(.55f,.84f,1f);
        private static readonly Color Repair = new Color(.35f,1f,.55f);
        public bool AutoTick { get; set; } = true;
        public int ActiveProjectileCount { get; private set; }
        public int ActiveDeviceCount { get; private set; }
        public int ActiveVisualCount => effects?.ActiveCount ?? 0;
        public int ExplosionsResolved { get; private set; }
        public int ProjectilesFired { get; private set; }
        public int AssemblerGemRemainder => gems;
        public float OverhealCharge => overheal;
        public float EmergencyCooldownRemaining => Mathf.Max(0, emergencyReady-time);
        private int Tier(UpgradeId id) => stats.GetUpgradeTier(id);
        private int Attack(UpgradeId id) => attackTiers[(int)id];
        private static float At(Vector3 values,int tier) => UpgradeTuning.At(values,tier);

        public void Bind(CombatSession combatSession, PlayerRuntimeStats playerStats, PlayerHealth health,
            FlashlightWeapon flashlight, CombatPickupPool pickupPool, CombatProgression xp)
        {
            if (bound && session == combatSession && stats == playerStats) return;
            Unbind();
            session=combatSession; stats=playerStats; player=health; weapon=flashlight; pickups=pickupPool; progression=xp;
            tuning=stats.Upgrades;
            projectiles=new Projectile[Mathf.Max(64,tuning.projectileCapacity)];
            for(int i=0;i<projectiles.Length;i++) projectiles[i]=new Projectile();
            devices=new Device[Mathf.Max(32,tuning.deviceCapacity)];
            for(int i=0;i<devices.Length;i++) devices[i]=new Device();
            blasts=new Blast[Mathf.Max(64,tuning.blastCapacity)];
            statuses=new StatusView[Mathf.Max(32,tuning.statusViews)];
            for(int i=0;i<statuses.Length;i++) statuses[i]=new StatusView();
            effects=new UpgradeEffectPool(transform,tuning.projectileViews+tuning.deviceCapacity+tuning.transientViews+tuning.statusViews+8,player.GetComponent<CollectorSpriteView>(),session.Effects);
            bound=true; session.Upgrades=this;
            session.SessionReset+=ResetForSession;
            session.Events.OnDamageResolved+=DamageResolved;
            session.Events.OnDashStateChanged+=DashResolved;
            session.Events.OnPlayerHealed+=Healed;
            session.Events.OnPulseResolved+=PulseResolved;
            if(weapon!=null) weapon.OnBurstStarted+=BurstStarted;
            if(pickups!=null) pickups.OnPickupCollected+=PickupCollected;
            if(progression!=null) progression.OnLevelGained+=LevelGained;
            stats.CopyUpgradeTiers(attackTiers);
            ResetForSession();
        }
        private void OnDestroy() => Unbind();
        private void Unbind()
        {
            if(!bound) return;
            session.SessionReset-=ResetForSession;
            session.Events.OnDamageResolved-=DamageResolved; session.Events.OnDashStateChanged-=DashResolved;
            session.Events.OnPlayerHealed-=Healed; session.Events.OnPulseResolved-=PulseResolved;
            if(session.Upgrades==this) session.Upgrades=null;
            if(weapon!=null) weapon.OnBurstStarted-=BurstStarted;
            if(pickups!=null) pickups.OnPickupCollected-=PickupCollected;
            if(progression!=null) progression.OnLevelGained-=LevelGained;
            effects.Dispose(); bound=false;
        }
        private void Update() { if(AutoTick) Step(Time.deltaTime); }
        private void BurstStarted(int _) => stats.CopyUpgradeTiers(attackTiers);
        public void ResetForSession()
        {
            if(!bound) return;
            effects.Clear();
            foreach(var p in projectiles) {p.Active=false;p.View=null;p.Hits.Clear();p.Target=null;}
            foreach(var d in devices) {d.Active=false;d.View=null;}
            foreach(var s in statuses) {s.Actor=null;s.View=null;}
            for(int i=0;i<4;i++) sawViews[i]=null;
            shield=null; blastCount=0; time=mineTimer=blizzardTimer=turretTimer=regenTimer=emergencyReady=overheal=0f;
            gems=activeDecoys=0; collecting=false; sawNextHit.Clear(); targets.Clear(); selectedTargets.Clear();
            ActiveProjectileCount=ActiveDeviceCount=ExplosionsResolved=ProjectilesFired=0;
            stats.CopyUpgradeTiers(attackTiers);
        }
        private void Gather()
        {
            targets.Clear(); var active=CombatTargetRegistry.ActiveTargets;
            for(int i=0;i<active.Count;i++) if(active[i] is EnemyActor enemy && enemy.IsAlive && enemy.Session==session) targets.Add(enemy);
        }
        private EnemyActor Find(int id)
        {
            var active=CombatTargetRegistry.ActiveTargets;
            for(int i=0;i<active.Count;i++) if(active[i].TargetId==id) return active[i] as EnemyActor;
            return null;
        }
        private EnemyActor Nearest(Vector3 origin,float radius, int exclude=0, bool unique=false)
        {
            EnemyActor best=null; float distance=radius*radius;
            var active=CombatTargetRegistry.ActiveTargets;
            for(int i=0;i<active.Count;i++)
            {
                if(!(active[i] is EnemyActor e) || !e.IsAlive || e.CurrentHealth<=0 || e.Session!=session || e.TargetId==exclude || (unique&&selectedTargets.Contains(e.TargetId))) continue;
                var d=e.WorldCenter-origin;d.y=0;float square=d.sqrMagnitude;
                if(square>distance) continue; best=e;distance=square;
            }
            return best;
        }
        private void DamageResolved(DamageResult hit)
        {
            if(!bound || !hit.AppliedRealDamage) return;
            if(hit.IsPlayerTarget)
            {
                if(hit.RemainingHealth>0 && Tier(UpgradeId.BIO_03)>0 && hit.RemainingHealth/hit.MaxHealth<tuning.emergencyThreshold && time>=emergencyReady)
                {player.GrantUpgradeProtection(tuning.emergencyInvulnerability);emergencyReady=time+tuning.emergencyCooldown;effects.Burst(hit.WorldPosition,2,Repair);}
                return;
            }
            bool primary=hit.SourceType==DamageSourceType.FlashlightPulse||hit.SourceType==DamageSourceType.SolarPulse;
            if(!primary && (int)hit.SourceType<10) return;
            var target=Find(hit.TargetId);
            if(target==null || target.Session!=session) return;
            if(hit.IsCritical && Tier(UpgradeId.CRIT_05)>0 && (hit.RemainingHealth+hit.HealthLost)/hit.MaxHealth<tuning.eyeHealthThreshold)
                QueueBlast(hit.WorldPosition,0,tuning.eyeRadius,stun:tuning.eyeStun,source:DamageSourceType.UpgradeProjectile);
            if(!primary) return;
            int slow=Attack(UpgradeId.CRYO_01), freeze=Attack(UpgradeId.CRYO_02);
            if(!hit.CausedDeath && target.CurrentHealth>0)
            {
                // A pulse applies its slow before counting toward the freezing threshold.
                if(slow>0) target.ApplySlow(tuning.slowFirstTier+(slow-1)*tuning.slowPerTier,tuning.slowDuration);
                if(freeze>0) target.RegisterColdHit(Mathf.RoundToInt(At(tuning.freezeHits,freeze)),At(tuning.freezeDuration,freeze));
            }
            if(hit.PulseIndex!=4) return;
            int siphon=Attack(UpgradeId.BIO_02);
            if(siphon>0 && session.Rng.Combat.NextFloat()<At(tuning.siphonChance,siphon)) player.Heal(At(tuning.siphonHealing,siphon));
            int prism=Attack(UpgradeId.FLASH_03);
            if(prism>0)
            {
                selectedTargets.Clear(); int count=Mathf.RoundToInt(At(tuning.prismCounts,prism));
                for(int i=0;i<count;i++)
                {
                    var next=Nearest(hit.WorldPosition,tuning.prismRadius,hit.TargetId,true); if(next==null) break;
                    selectedTargets.Add(next.TargetId);
                    Fire(hit.WorldPosition,next.WorldCenter-hit.WorldPosition,hit.ResolvedDamage*At(tuning.prismDamage,prism),tuning.plasmaSpeed,1f,DamageSourceType.UpgradeProjectile,next,ignore:target);
                }
            }
        }
        private void PulseResolved(PulseResolvedEvent pulse)
        {
            int tier=Attack(UpgradeId.CRIT_04);
            if(pulse.PulseIndex==4 && pulse.CritCount>0 && tier>0 && weapon!=null)
            {weapon.QueueGuaranteedCriticalPulses(Mathf.RoundToInt(At(tuning.guaranteedPulses,tier)));effects.Burst(player.WorldCenter,1.4f,Amber);}
        }
        private void Healed(HealResult heal)
        {
            if(!bound) return;
            if(heal.ActualHealed>0) effects.Burst(player.WorldCenter,1.25f,Repair);
            if(Tier(UpgradeId.BIO_04)==0) return;
            overheal+=Mathf.Max(0,heal.RequestedAmount-heal.ActualHealed);
            while(overheal>=tuning.novaOverheal) {overheal-=tuning.novaOverheal;QueueBlast(player.WorldCenter,tuning.novaDamage,tuning.novaRadius);}
        }
        private void DashResolved(DashStateEvent dash)
        {
            if(dash.Started)
            {
                int decoy=Tier(UpgradeId.DASH_03);
                if(decoy>0) DeviceAt(DeviceKind.Decoy,dash.Position,At(tuning.decoyDuration,decoy),At(tuning.decoyDamage,decoy));
                if(Tier(UpgradeId.DASH_04)>0) QueueBlast(dash.Position,tuning.cataclysmDamage,tuning.cataclysmRadius);
            }
            if(dash.Ended)
            {
                int shock=Tier(UpgradeId.DASH_02);
                if(shock>0) QueueBlast(dash.Position,At(tuning.shockDamage,shock),At(tuning.shockRadius,shock),stun:At(tuning.shockStun,shock),push:tuning.shockPushDistance);
                if(Tier(UpgradeId.DASH_04)>0) Fire(dash.Position,dash.Direction,tuning.bladeDamage,tuning.bladeSpeed,tuning.bladeLifetime,DamageSourceType.UpgradeBlade,null,true,0,tuning.bladeHitRadius);
            }
        }
        public void EnemyDied(EnemyActor enemy, Vector3 position)
        {
            if(!bound) return;
            sawNextHit.Remove(enemy.TargetId);
            if(enemy.IsFrozen && Tier(UpgradeId.CRYO_04)>0)
                for(int i=0;i<tuning.shatterCount;i++)
                {
                    float angle=i*Mathf.PI*2/tuning.shatterCount;
                    Fire(position,new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)),tuning.shatterDamage,tuning.shatterSpeed,tuning.shatterLifetime,DamageSourceType.UpgradeFrost,null,true,tuning.shatterFreeze);
                }
            int chain=Tier(UpgradeId.DEMO_02);
            if(chain>0 && session.Rng.Combat.NextFloat()<At(tuning.chainChance,chain)) QueueBlast(position,At(tuning.chainDamage,chain),tuning.chainRadius);
        }
        private void PickupCollected(CombatPickupInfo pickup)
        {
            if(pickup.Kind!=CombatPickupKind.Experience || Tier(UpgradeId.SCRAP_02)==0) return;
            gems+=pickup.GemCount;
            while(gems>=tuning.assemblerGemCount)
            {
                gems-=tuning.assemblerGemCount; var target=Nearest(player.WorldCenter,1000);
                if(target!=null) Fire(player.WorldCenter,target.WorldCenter-player.WorldCenter,At(tuning.assemblerDamage,Tier(UpgradeId.SCRAP_02)),tuning.plasmaSpeed,10,DamageSourceType.UpgradeProjectile,target);
            }
        }
        public void UpgradeAcquired(string id)
        {
            if (bound && id == "GREED_05") LevelGained(0);
        }
        private void LevelGained(int _)
        {
            if(Tier(UpgradeId.GREED_05)==0 || pickups==null || collecting) return;
            collecting=true;
            try {pickups.CollectAll();effects.Burst(player.WorldCenter,stats.MagnetRadius+5,Plasma,true);}
            finally {collecting=false;}
        }
        public Vector3 PursuitGoal(Vector3 enemy,Vector3 fallback)
        {
            if(activeDecoys==0) return fallback;
            float closest=tuning.decoyTauntRadius*tuning.decoyTauntRadius; Vector3 goal=fallback;
            for(int i=0;i<devices.Length;i++)
            {
                var d=devices[i]; if(!d.Active || d.Kind!=DeviceKind.Decoy) continue;
                float square=(d.Position-enemy).sqrMagnitude; if(square>closest) continue; closest=square;goal=d.Position;
            }
            return goal;
        }
        private void Fire(Vector3 position,Vector3 direction,float damage,float speed,float life,DamageSourceType source,
            EnemyActor target=null,bool piercing=false,float freeze=0,float radius=.25f,EnemyActor ignore=null)
        {
            for(int i=0;i<projectiles.Length;i++)
            {
                var p=projectiles[i];if(p.Active) continue;
                p.Active=true;p.Position=position;p.Direction=Flat(direction);p.Damage=damage;p.Speed=speed;p.Life=life;p.Source=source;
                p.Target=target;p.TargetVersion=target!=null?target.SpawnVersion:0;p.Piercing=piercing;p.Freeze=freeze;p.Radius=radius;p.Hits.Clear();
                if(ignore!=null)p.Hits[ignore.TargetId]=ignore.SpawnVersion;
                p.View=effects.Acquire(source==DamageSourceType.UpgradeBlade?UpgradeEffectPool.Shape.Blade:UpgradeEffectPool.Shape.Dart,position,freeze>0?Ice:source==DamageSourceType.UpgradeTurret?Amber:Plasma,source==DamageSourceType.UpgradeBlade?1.5f:.65f);
                ActiveProjectileCount++;ProjectilesFired++;return;
            }
        }
        private void QueueBlast(Vector3 p,float damage,float radius,float delay=0,float stun=0,float push=0,float freeze=0, bool echo=false,bool implosion=false,DamageSourceType source=DamageSourceType.UpgradeExplosion)
        {
            if(blastCount>=blasts.Length) return;
            bool explosion=source==DamageSourceType.UpgradeExplosion;
            if(explosion&&!echo) {damage*=stats.ExplosionDamageMultiplier;radius*=stats.ExplosionRadiusMultiplier;}
            blasts[blastCount++]=new Blast {Position=p,Damage=damage,Radius=radius,Delay=delay,Stun=stun,Push=push,Freeze=freeze,Echo=echo,Implosion=implosion,Source=source};
        }
        private void DeviceAt(DeviceKind kind,Vector3 position,float life,float damage)
        {
            for(int i=0;i<devices.Length;i++)
            {
                var d=devices[i];if(d.Active) continue;
                d.Active=true;d.Fresh=true;d.Kind=kind;d.Position=position;d.Life=life;d.Damage=damage;d.BurstTimer=d.ShotTimer=0;d.Shots=0;
                var shape=kind==DeviceKind.Mine?UpgradeEffectPool.Shape.Mine:kind==DeviceKind.Decoy?UpgradeEffectPool.Shape.Decoy:UpgradeEffectPool.Shape.Turret;
                var p=position;if(kind!=DeviceKind.Decoy)p.y=ArenaSurface.Height(position)+.15f;
                d.View=effects.Acquire(shape,p,kind==DeviceKind.Decoy?Plasma:Amber,kind==DeviceKind.Mine?.65f:1f);
                if(kind==DeviceKind.Decoy)activeDecoys++;
                ActiveDeviceCount++;return;
            }
        }
        public void Step(float dt)
        {
            if(!bound || dt<=0 || session.IsPaused || !session.IsGameplayFlowActive() || !player.IsAlive || !isActiveAndEnabled) return;
            time+=dt; effects.Step(dt);
            Gather();
            for(int i=0;i<targets.Count;i++) targets[i].StepUpgradeStatus(dt);
            int tier=Tier(UpgradeId.BIO_03);
            if(tier>0) {regenTimer+=dt;while(regenTimer>=tuning.regenerationInterval) {regenTimer-=tuning.regenerationInterval;player.Heal(At(tuning.regeneration,tier));}} else regenTimer=0;
            tier=Tier(UpgradeId.DEMO_03);
            if(tier>0) {mineTimer+=dt;while(mineTimer>=At(tuning.mineInterval,tier)) {mineTimer-=At(tuning.mineInterval,tier);DeviceAt(DeviceKind.Mine,player.WorldCenter,float.PositiveInfinity,At(tuning.mineDamage,tier));}} else mineTimer=0;
            tier=Tier(UpgradeId.CRYO_03);
            if(tier>0) {blizzardTimer+=dt;while(blizzardTimer>=At(tuning.blizzardInterval,tier)) {blizzardTimer-=At(tuning.blizzardInterval,tier);QueueBlast(player.WorldCenter,At(tuning.blizzardDamage,tier),tuning.blizzardRadius,freeze:tuning.blizzardFreeze,source:DamageSourceType.UpgradeFrost);}} else blizzardTimer=0;
            if(Tier(UpgradeId.SCRAP_04)>0) {turretTimer+=dt;while(turretTimer>=tuning.turretInterval) {turretTimer-=tuning.turretInterval;DeviceAt(DeviceKind.Turret,player.WorldCenter,tuning.turretDuration,tuning.turretDamage);}} else turretTimer=0;
            StepDevices(dt); StepProjectiles(dt); StepSaws(); StepBlasts(dt); StepStatusViews();
            if(player.UpgradeProtectionRemaining>0)
            {
                shield??=effects.Acquire(UpgradeEffectPool.Shape.Ring,player.WorldCenter,Repair,1.2f);
                if(shield!=null) shield.Root.position=player.WorldCenter+Vector3.down*.7f;
            }
            else {effects.Release(shield);shield=null;}
        }
        private void StepDevices(float dt)
        {
            for(int i=0;i<devices.Length;i++)
            {
                var d=devices[i];if(!d.Active)continue;float elapsed=d.Fresh?0:dt;d.Fresh=false;d.Life-=elapsed;
                bool expired=d.Life<=0;
                if(d.Kind==DeviceKind.Mine && Nearest(d.Position,tuning.mineTriggerRadius)!=null) expired=true;
                if(expired)
                {
                    if(d.Kind!=DeviceKind.Turret) QueueBlast(d.Position,d.Damage,d.Kind==DeviceKind.Mine?tuning.mineRadius:tuning.decoyExplosionRadius,implosion:d.Kind==DeviceKind.Decoy);
                    if(d.Kind==DeviceKind.Decoy)activeDecoys--;
                    d.Active=false;effects.Release(d.View);d.View=null;ActiveDeviceCount--;continue;
                }
                if(d.Kind!=DeviceKind.Turret) continue;
                d.BurstTimer-=elapsed; d.ShotTimer-=elapsed;
                if(d.Shots==0 && d.BurstTimer<=0) {d.Shots=tuning.turretBurstCount;d.BurstTimer=tuning.turretBurstInterval;d.ShotTimer=0;}
                while(d.Shots>0&&d.ShotTimer<=0)
                {
                    var target=Nearest(d.Position,tuning.turretRange);
                    if(target!=null)
                    {
                        var direction=target.WorldCenter-d.Position;
                        Fire(d.Position,direction,d.Damage,tuning.plasmaSpeed,1,DamageSourceType.UpgradeTurret,target);
                        if(d.View!=null)d.View.Root.rotation=Quaternion.LookRotation(Flat(direction));
                    }
                    d.Shots--;d.ShotTimer+=tuning.turretShotInterval;
                }
            }
        }
        private void StepProjectiles(float dt)
        {
            for(int i=0;i<projectiles.Length;i++)
            {
                var p=projectiles[i];if(!p.Active)continue;
                if(p.Target!=null && p.Target.IsAlive && p.Target.SpawnVersion==p.TargetVersion) p.Direction=Flat(p.Target.WorldCenter-p.Position);
                Vector3 from=p.Position,to=from+p.Direction*p.Speed*dt;
                bool blocked=Physics.Linecast(from,to,out RaycastHit wall,1<<0,QueryTriggerInteraction.Ignore);
                if(blocked) to=wall.point;
                p.Position=to;p.Life-=dt;
                if(p.View!=null) {p.View.Root.position=to;p.View.Root.rotation=Quaternion.LookRotation(p.Direction);}
                Gather();
                for(int e=0;e<targets.Count;e++)
                {
                    var enemy=targets[e];
                    if(!enemy.IsAlive || enemy.CurrentHealth<=0 || (p.Hits.TryGetValue(enemy.TargetId,out int version)&&version==enemy.SpawnVersion)) continue;
                    var delta=to-from;delta.y=0;var offset=enemy.WorldCenter-from;offset.y=0;
                    float t=delta.sqrMagnitude>0?Mathf.Clamp01(Vector3.Dot(offset,delta)/delta.sqrMagnitude):0;
                    float radius=enemy.HitRadius+p.Radius;
                    if((offset-delta*t).sqrMagnitude>radius*radius)continue;
                    p.Hits[enemy.TargetId]=enemy.SpawnVersion;
                    var hit=Deal(enemy,p.Damage,from,p.Source);
                    if(p.Freeze>0&&hit.AppliedRealDamage&&enemy.IsAlive&&enemy.CurrentHealth>0)enemy.ApplyFreeze(p.Freeze);
                    if(hit.AppliedRealDamage)effects.Burst(enemy.WorldCenter,.45f,p.Freeze>0?Ice:Amber);
                    if(!p.Piercing) {p.Life=0;break;}
                }
                if(p.Life<=0||blocked) {p.Active=false;effects.Release(p.View);p.View=null;p.Target=null;ActiveProjectileCount--;}
            }
        }
        private DamageResult Deal(EnemyActor enemy,float damage,Vector3 origin,DamageSourceType source)
        {
            var request=new DamageRequest(player.TargetId,source,origin,Flat(enemy.WorldCenter-origin),damage,true,stats.CritChance,stats.CritMultiplier);
            return enemy.ReceiveDamage(in request);
        }
        private void StepBlasts(float dt)
        {
            // Drain the ready prefix including chained deaths. Delayed echoes are compacted in place.
            int count=blastCount; int write=0;
            for(int i=0;i<blastCount;i++)
            {
                var b=blasts[i]; if(i<count)b.Delay-=dt;
                if(b.Delay>0) {blasts[write++]=b;continue;}
                effects.Burst(b.Position,b.Radius,b.Freeze>0?Ice:b.Implosion?Plasma:Amber,b.Implosion);
                Gather();
                for(int e=0;e<targets.Count;e++)
                {
                    var enemy=targets[e];if(!enemy.IsAlive||enemy.CurrentHealth<=0)continue;
                    var delta=enemy.WorldCenter-b.Position;delta.y=0;
                    if(delta.sqrMagnitude>(b.Radius+enemy.HitRadius)*(b.Radius+enemy.HitRadius))continue;
                    if(b.Damage>0 && !Deal(enemy,b.Damage,b.Position,b.Source).AppliedRealDamage) continue;
                    if(!enemy.IsAlive||enemy.CurrentHealth<=0)continue;
                    if(b.Freeze>0)enemy.ApplyFreeze(b.Freeze);
                    if(b.Stun>0)enemy.ApplyStun(b.Stun);
                    if(b.Push>0)enemy.PushDistance(delta,b.Push);
                    if(b.Implosion)enemy.ApplyKnockback(-Flat(delta)*tuning.implosionPullSpeed);
                }
                if(b.Source==DamageSourceType.UpgradeExplosion)
                {
                    ExplosionsResolved++;
                    if(!b.Echo&&Tier(UpgradeId.DEMO_04)>0)QueueBlast(b.Position,b.Damage*tuning.implosionFraction,b.Radius,tuning.implosionDelay,echo:true,implosion:true);
                }
            }
            blastCount=write;
        }
        private void StepSaws()
        {
            int tier=Tier(UpgradeId.SCRAP_03),count=Mathf.RoundToInt(At(tuning.sawCounts,tier));
            for(int i=0;i<4;i++)
            {
                if(i>=count) {effects.Release(sawViews[i]);sawViews[i]=null;continue;}
                float angle=(time*tuning.sawAngularSpeed+i*360f/count)*Mathf.Deg2Rad;
                Vector3 p=player.WorldCenter+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*tuning.sawRadius;
                sawViews[i]??=effects.Acquire(UpgradeEffectPool.Shape.Saw,p,Amber,.55f);
                if(sawViews[i]!=null) {sawViews[i].Root.position=p;sawViews[i].Root.rotation=Quaternion.Euler(0,-time*720,0);}
                Gather();
                for(int e=0;e<targets.Count;e++)
                {
                    var enemy=targets[e];var delta=enemy.WorldCenter-p;delta.y=0;
                    if(delta.sqrMagnitude>(enemy.HitRadius+.55f)*(enemy.HitRadius+.55f) || (sawNextHit.TryGetValue(enemy.TargetId,out float next)&&time<next)) continue;
                    sawNextHit[enemy.TargetId]=time+tuning.sawHitInterval;
                    Deal(enemy,At(tuning.sawDamage,tier),p,DamageSourceType.UpgradeSaw);
                    if(enemy.IsAlive)enemy.ApplyKnockback(Flat(enemy.WorldCenter-player.WorldCenter)*tuning.sawPush);
                }
            }
        }
        private void StepStatusViews()
        {
            for(int i=0;i<statuses.Length;i++)
            {
                var s=statuses[i];
                if(s.Actor!=null && (!s.Actor.IsAlive || s.Actor.SpawnVersion!=s.Version || (!s.Actor.IsControlLocked && s.Actor.SlowRemaining<=0)))
                {effects.Release(s.View);s.View=null;s.Actor=null;}
            }
            Gather();
            for(int e=0;e<targets.Count;e++)
            {
                var enemy=targets[e];if(!enemy.IsControlLocked && enemy.SlowRemaining<=0)continue;
                StatusView found=null,free=null;
                for(int i=0;i<statuses.Length;i++) {if(statuses[i].Actor==enemy){found=statuses[i];break;} if(free==null&&statuses[i].Actor==null)free=statuses[i];}
                found??=free;if(found==null)continue;
                found.Actor=enemy;found.Version=enemy.SpawnVersion;
                found.View??=effects.Acquire(UpgradeEffectPool.Shape.Ring,enemy.WorldCenter,Ice,enemy.HitRadius+ .25f);
                if(found.View!=null)
                {
                    found.View.Root.position=enemy.WorldCenter+Vector3.down*.6f;
                    effects.SetShape(found.View,enemy.IsFrozen?UpgradeEffectPool.Shape.Ice:UpgradeEffectPool.Shape.Ring);
                    effects.SetColor(found.View,enemy.IsFrozen?Ice:enemy.StunRemaining>0?Amber:new Color(.2f,.6f,.8f));
                    found.View.Root.localScale=Vector3.one*(enemy.HitRadius+(enemy.IsFrozen?.45f:.2f));
                }
            }
        }
        private static Vector3 Flat(Vector3 direction) {direction.y=0;return direction.sqrMagnitude>.0001f?direction.normalized:Vector3.forward;}
    }
}
