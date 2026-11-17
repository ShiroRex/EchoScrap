using System.Collections.Generic;
using EchoScrap.Core;
using EchoScrap.Player;
using Unity.Profiling;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Run-scoped upgrade simulation. Events normally enqueue attacks for the next step;
    /// a saturated logical chain reserve finishes its oldest attack before reusing the slot.</summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeCombatRuntime : MonoBehaviour
    {
        private static readonly ProfilerMarker blastsMarker = new ProfilerMarker("EchoScrap.ChainedBlasts");
        private bool allTargetsIndexed;

        private enum ProjectileMode { Normal = 0, BouncingDisc = 1, Boomerang = 2, Parabolic = 3 }
        private enum GroundZoneKind { PlasmaPuddle = 0, FlameTrail = 1 }
        private const int GroundZoneCapacity = 64;
        private sealed class Projectile
        {
            public bool Active, Piercing, BoolFlag;
            public ProjectileMode Mode;
            public Vector3 Position, Direction, StartPosition, EndPosition;
            public float Damage, Speed, Life, Freeze, Radius, FloatData, Elapsed, Duration;
            public DamageSourceType Source;
            public DamageType DamageType;
            public EnemyActor Target;
            public int TargetVersion;
            public readonly Dictionary<int, int> Hits = new Dictionary<int, int>(256);
            public readonly Dictionary<int, float> HitCooldowns = new Dictionary<int, float>(256);
            public UpgradeEffectPool.View View;
        }
        private sealed class GroundZone
        {
            public bool Active;
            public GroundZoneKind Kind;
            public Vector3 Position;
            public float Radius, DamagePerTick, TickInterval, TickTimer, Life, Slow, Vulnerability;
            public DamageSourceType Source;
            public DamageType DamageType;
            public UpgradeEffectPool.View View;
        }
        private enum DeviceKind { Mine, Decoy, Turret }
        private const int MaxActiveTurrets = 2;
        private const int MaxElectricCables = 2;
        private sealed class Device
        {
            public bool Active, Fresh;
            public DeviceKind Kind;
            public Vector3 Position;
            public float Life, Damage, ShotTimer, BurstTimer, PlacedAt;
            public int Shots;
            public ulong PlacementOrder;
            public UpgradeEffectPool.View View;
        }
        private struct ElectricCable
        {
            public Vector3 Start, End;
            public float ExpiresAt, Damage;
        }
        private struct Blast
        {
            public Vector3 Position;
            public float Damage, Radius, Delay, Stun, Push, Freeze;
            public bool Implosion, Echo, Shockwave;
            public DamageSourceType Source;
            public DamageType DamageType;
        }
        private sealed class StatusView
        {
            public EnemyActor Actor;
            public int Version;
            public UpgradeEffectPool.View View;
            public byte AppliedKind;
            public float AppliedScale = -1f;
            public Vector3 AppliedPos;
            public bool HasPos;
        }
        private sealed class VoltaicChain
        {
            public bool Active;
            public readonly EnemyActor[] Targets = new EnemyActor[6];
            public readonly int[] Versions = new int[6];
            public int Count, Next, PreviousVersion, Order;
            public EnemyActor PreviousActor;
            public Vector3 Origin, Previous;
            public float Damage, Radius, Wait, HopDelay;
        }
        private CombatSession session;
        private PlayerRuntimeStats stats;
        private PlayerHealth player;
        private CollectorSpriteView collectorSprite;
        private FlashlightWeapon weapon;
        private CombatPickupPool pickups;
        private CombatProgression progression;
        private UpgradeTuning tuning;
        private UpgradeEffectPool effects;
        private VoltaicChainRenderer lightning;
        private VoltaicChain[] voltaicChains;
        private uint voltaicEpoch;
        private Projectile[] projectiles;
        private Device[] devices;
        private Blast[] blasts;
        private int blastCount;
        private StatusView[] statuses;
        private readonly GroundZone[] groundZones = new GroundZone[GroundZoneCapacity];
        private readonly int[] attackTiers = new int[UpgradeRules.Count];
        private readonly List<EnemyActor> targets = new List<EnemyActor>(512);
        private readonly Dictionary<int, EnemyActor> targetById = new Dictionary<int, EnemyActor>(256);
        private readonly Dictionary<int, StatusView> statusByActorId = new Dictionary<int, StatusView>(64);
        private readonly HashSet<int> selectedTargets = new HashSet<int>();
        private readonly UpgradeEffectPool.View[] sawViews = new UpgradeEffectPool.View[5];
        private UpgradeEffectPool.View shield;
        private UpgradeEffectPool.View returnMarker;
        private UpgradeEffectPool.View auraView;
        private PlayerMotor motor;
        private Vector3 dashStart, lastTrailPos;
        private float wallSlamReadyAt;
        private bool trackingDash, trailInitialized;
        private readonly ElectricCable[] cables = new ElectricCable[MaxElectricCables];
        private readonly List<EnemyActor> segmentTargets = new List<EnemyActor>(64);
        private readonly List<EnemyActor> spatialCandidates = new List<EnemyActor>(64);
        private readonly List<EnemyActor> nearestCandidates = new List<EnemyActor>(64);
        private readonly List<EnemyActor> cryoCandidates = new List<EnemyActor>(64);
        private int activeStatusCount;
        private float time, blizzardTimer, turretTimer, regenTimer, emergencyReady, overheal;
        private float reactiveReadyAt, puddleTimer, discTimer, boomerangTimer, orbitalTimer, parabolicTimer, whipTimer, auraTimer;
        private int gems, batteryGems, activeDecoys;
        private ulong nextPlacementOrder;
        private bool bound, collecting;
        private static readonly Color Amber = new Color(1f,.66f,.22f);
        private static readonly Color Plasma = new Color(.35f,.94f,1f);
        private static readonly Color Ice = new Color(.55f,.84f,1f);
        private static readonly Color Repair = new Color(.35f,1f,.55f);
        private static readonly Color FlameColor = new Color(1f,.42f,.12f);
        public bool AutoTick { get; set; } = true;
        public int ActiveProjectileCount { get; private set; }
        public int ActiveDeviceCount { get; private set; }
        public int ActiveVisualCount => (effects?.ActiveCount ?? 0) + (lightning?.ActiveCount ?? 0);
        public int ActiveLightningCount => lightning?.ActiveCount ?? 0;
        public int ActiveChainCount { get; private set; }
        public int ChainsTriggered { get; private set; }
        public int ChainHitsResolved { get; private set; }
        public int ChainTargetsInvalidated { get; private set; }
        public int ChainOverflowResolutions { get; private set; }
        public int ActiveParticleCount => effects?.ActiveParticleCount ?? 0;
        public int ActiveParticleRigCount => effects?.ActiveParticleRigCount ?? 0;
        public int ActiveLightCount => effects?.ActiveLightCount ?? 0;
        public int ExplosionsResolved { get; private set; }
        public int ProjectilesFired { get; private set; }
        public int AssemblerGemRemainder => gems;
        public int BatteryGemRemainder => batteryGems;
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
            collectorSprite=player.GetComponent<CollectorSpriteView>();
            motor = player.GetComponent<PlayerMotor>();
            tuning=stats.Upgrades;
            projectiles=new Projectile[Mathf.Max(64,tuning.projectileCapacity)];
            for(int i=0;i<projectiles.Length;i++) projectiles[i]=new Projectile();
            devices=new Device[Mathf.Max(32,tuning.deviceCapacity)];
            for(int i=0;i<devices.Length;i++) devices[i]=new Device();
            for(int i=0;i<groundZones.Length;i++) groundZones[i]??=new GroundZone();
            blasts=new Blast[Mathf.Max(64,tuning.blastCapacity)];
            statuses=new StatusView[Mathf.Max(32,tuning.statusViews)];
            for(int i=0;i<statuses.Length;i++) statuses[i]=new StatusView();
            effects=new UpgradeEffectPool(transform,tuning.projectileViews+tuning.deviceCapacity+tuning.transientViews+tuning.statusViews+GroundZoneCapacity+12,collectorSprite,session.Effects);
            lightning=new VoltaicChainRenderer(transform,session.Effects,tuning,MaxElectricCables);
            voltaicChains=new VoltaicChain[Mathf.Max(256,tuning.projectileCapacity)];
            for(int i=0;i<voltaicChains.Length;i++)voltaicChains[i]=new VoltaicChain();
            bound=true; session.Upgrades=this;
            session.SessionReset+=ResetForSession;
            session.Events.OnDamageResolved+=DamageResolved;
            session.Events.OnDashStateChanged+=DashResolved;
            session.Events.OnPlayerHealed+=Healed;
            session.Events.OnPulseResolved+=PulseResolved;
            session.Events.OnActorDied+=ActorDied;
            if(weapon!=null) weapon.OnBurstStarted+=BurstStarted;
            if(pickups!=null) pickups.OnPickupCollected+=PickupCollected;
            if(progression!=null) progression.OnLevelGained+=LevelGained;
            stats.CopyUpgradeTiers(attackTiers);
            ResetForSession();
        }
        private void OnDestroy() => Unbind();
        private void OnDisable()
        {
            if(!bound)return;
            ClearVoltaicChains();lightning.Clear();
            ClearContextualUpgrades();
        }
        private void Unbind()
        {
            if(!bound) return;
            session.SessionReset-=ResetForSession;
            session.Events.OnDamageResolved-=DamageResolved; session.Events.OnDashStateChanged-=DashResolved;
            session.Events.OnPlayerHealed-=Healed; session.Events.OnPulseResolved-=PulseResolved;
            session.Events.OnActorDied-=ActorDied;
            if(session.Upgrades==this) session.Upgrades=null;
            if(weapon!=null) weapon.OnBurstStarted-=BurstStarted;
            if(pickups!=null) pickups.OnPickupCollected-=PickupCollected;
            if(progression!=null) progression.OnLevelGained-=LevelGained;
            ClearContextualUpgrades();
            effects.Dispose();lightning.Dispose();bound=false;
        }
        private void Update() { if(AutoTick) Step(Time.deltaTime); }
        private void BurstStarted(int _) => stats.CopyUpgradeTiers(attackTiers);
        public void ResetForSession()
        {
            if(!bound) return;
            ClearContextualUpgrades();
            effects.Clear();
            ClearVoltaicChains();lightning.Clear();
            foreach(var p in projectiles) {p.Active=false;p.Mode=ProjectileMode.Normal;p.View=null;p.Hits.Clear();p.HitCooldowns.Clear();p.Target=null;}
            foreach(var d in devices) {d.Active=false;d.View=null;}
            foreach(var z in groundZones) if (z != null) {z.Active=false;z.View=null;}
            foreach(var s in statuses) {s.Actor=null;s.View=null;s.AppliedKind=0;s.AppliedScale=-1f;s.HasPos=false;}
            activeStatusCount = 0;
            for(int i=0;i<sawViews.Length;i++) sawViews[i]=null;
            shield=null; auraView=null; blastCount=0; time=blizzardTimer=turretTimer=regenTimer=emergencyReady=overheal=0f;
            reactiveReadyAt=puddleTimer=discTimer=boomerangTimer=orbitalTimer=parabolicTimer=whipTimer=auraTimer=0f;
            trailInitialized=false; lastTrailPos=Vector3.zero;
            nextPlacementOrder = 0;
            gems=batteryGems=activeDecoys=0; collecting=false; targets.Clear(); targetById.Clear(); statusByActorId.Clear(); selectedTargets.Clear();
            ActiveProjectileCount=ActiveDeviceCount=ExplosionsResolved=ProjectilesFired=0;
            ChainsTriggered=ChainHitsResolved=ChainTargetsInvalidated=ChainOverflowResolutions=0;
            stats.CopyUpgradeTiers(attackTiers);
        }
        private void Gather()
        {
            targets.Clear(); targetById.Clear();
            allTargetsIndexed = true;
            var active = CombatTargetRegistry.ActiveTargets;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] is EnemyActor enemy && enemy.IsAlive && enemy.Session == session)
                {
                    targets.Add(enemy);
                    targetById[enemy.TargetId] = enemy;
                    // A registered target can have its locomotion component disabled.
                    allTargetsIndexed &= enemy.IsInSimulationIndex;
                }
            }
        }
        private EnemyActor Find(int id)
        {
            if (targetById.TryGetValue(id, out EnemyActor cached) && cached != null && cached.TargetId == id)
                return cached;
            var active = CombatTargetRegistry.ActiveTargets;
            for (int i = 0; i < active.Count; i++) if (active[i].TargetId == id) return active[i] as EnemyActor;
            return null;
        }
        private EnemyActor Nearest(Vector3 origin,float radius, int exclude=0, bool unique=false)
        {
            EnemyActor best=null; float distance=radius*radius;
            if (allTargetsIndexed && !unique && radius <= 24f)
            {
                EnemyActor.GatherCandidatesUnsorted(origin, radius, nearestCandidates);
                for(int i=0;i<nearestCandidates.Count;i++)
                {
                    EnemyActor e=nearestCandidates[i];
                    if(e==null || !e.IsAlive || e.CurrentHealth<=0 || e.Session!=session || e.TargetId==exclude) continue;
                    var d=e.WorldCenter-origin;d.y=0;float square=d.sqrMagnitude;
                    if(square>distance) continue; best=e;distance=square;
                }
                return best;
            }
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
            if(!bound)return;
            if(hit.IsPlayerTarget)
            {
                if(hit.WasImmune && shield!=null) effects.PulseShield(shield,hit.HitDirection);
                int deflectTier = Tier(UpgradeId.SCRAP_08);
                if(hit.WasBlocked && deflectTier>0 && CanResolveContextualUpgrade)
                    QueueBlast(player.WorldCenter, At(tuning.deflectDamage, deflectTier), tuning.deflectRadius,
                        push: tuning.deflectPush, source: DamageSourceType.ShieldDeflect, damageType: DamageType.Physical, shockwave: true);
                if(!hit.AppliedRealDamage) return;
                if(hit.RemainingHealth>0)
                {
                    if(Tier(UpgradeId.BIO_03)>0 && hit.RemainingHealth/hit.MaxHealth<tuning.emergencyThreshold && time>=emergencyReady)
                    {player.GrantUpgradeProtection(tuning.emergencyInvulnerability);emergencyReady=time+tuning.emergencyCooldown;effects.Burst(hit.WorldPosition,2,Repair);}
                    int reactiveTier = Tier(UpgradeId.DEMO_07);
                    if(reactiveTier>0 && time>=reactiveReadyAt && CanResolveContextualUpgrade)
                    {
                        reactiveReadyAt = time + tuning.reactiveCooldown;
                        QueueBlast(player.WorldCenter, tuning.reactiveBaseDamage + reactiveTier * tuning.reactiveDamagePerTier,
                            tuning.reactiveRadius, source: DamageSourceType.UpgradeExplosion, damageType: DamageType.Physical);
                    }
                }
                return;
            }
            if(!hit.AppliedRealDamage) return;
            bool primary=hit.SourceType==DamageSourceType.FlashlightPulse||hit.SourceType==DamageSourceType.SolarPulse;
            if(!primary && (int)hit.SourceType<10) return;
            var target=Find(hit.TargetId);
            if(target!=null && target.Session!=session) return;
            if(hit.IsCritical && Tier(UpgradeId.CRIT_05)>0 && (hit.RemainingHealth+hit.HealthLost)/hit.MaxHealth<tuning.eyeHealthThreshold)
                QueueBlast(hit.WorldPosition,0,tuning.eyeRadius,stun:tuning.eyeStun,source:DamageSourceType.UpgradeProjectile,damageType:DamageType.Supernatural);
            if(!primary) return;
            int slow=Attack(UpgradeId.CRYO_01), freeze=Attack(UpgradeId.CRYO_02);
            if(target!=null && !hit.CausedDeath && target.CurrentHealth>0)
            {
                // A pulse applies its slow before counting toward the freezing threshold.
                if(slow>0) target.ApplySlow(tuning.slowFirstTier+(slow-1)*tuning.slowPerTier,tuning.slowDuration);
                bool wasFrozen=target.IsFrozen;
                if(freeze>0) target.RegisterColdHit(Mathf.RoundToInt(At(tuning.freezeHits,freeze)),At(tuning.freezeDuration,freeze));
                if(!wasFrozen && target.IsFrozen)
                {
                    effects.Burst(target.WorldCenter,1.2f,Ice,kind:UpgradeEffectPool.BurstKind.Frost);
                    TriggerCryoMagnet(target.WorldCenter);
                }
            }
            if(hit.PulseIndex!=4) return;
            int siphon=Attack(UpgradeId.BIO_02);
            if(siphon>0 && session.Rng.Combat.NextFloat()<At(tuning.siphonChance,siphon)) player.Heal(At(tuning.siphonHealing,siphon));
            int prism=Attack(UpgradeId.FLASH_03);
            if(prism>0)QueueVoltaicChain(in hit,target,prism);
        }
        private void ActorDied(ActorDeathEvent death)
        {
            if(!death.IsPlayer)return;
            ClearVoltaicChains();lightning.Clear();
            ClearContextualUpgrades();
        }
        private void ClearVoltaicChains()
        {
            voltaicEpoch++;
            if(voltaicChains==null)return;
            for(int i=0;i<voltaicChains.Length;i++)ReleaseChain(voltaicChains[i]);
            ActiveChainCount=0;
        }
        private static void ReleaseChain(VoltaicChain chain)
        {
            chain.Active=false;chain.PreviousActor=null;
            for(int i=0;i<chain.Targets.Length;i++)chain.Targets[i]=null;
            chain.Count=chain.Next=0;
        }
        private void QueueVoltaicChain(in DamageResult hit,EnemyActor origin,int tier)
        {
            if(!isActiveAndEnabled)return;
            VoltaicChain chain=null;
            for(int i=0;i<voltaicChains.Length;i++)if(!voltaicChains[i].Active){chain=voltaicChains[i];break;}
            if(chain==null)
            {
                // A saturated logical reserve completes the oldest queued damage, never drops it.
                // Derived hits cannot request another electrical chain: only primary pulse four can.
                chain=voltaicChains[0];
                for(int i=1;i<voltaicChains.Length;i++)if(voltaicChains[i].Order<chain.Order)chain=voltaicChains[i];
                uint epoch=voltaicEpoch;ChainOverflowResolutions++;
                while(chain.Next<chain.Count)if(!ResolveVoltaicHop(chain,epoch))return;
                ReleaseChain(chain);ActiveChainCount--;
            }
            chain.Origin=chain.Previous=hit.WorldPosition;chain.PreviousActor=origin;
            chain.PreviousVersion=origin!=null?origin.SpawnVersion:0;
            chain.Damage=hit.ResolvedDamage*At(tuning.prismDamage,tier);chain.Radius=tuning.prismRadius;
            chain.HopDelay=Mathf.Max(.01f,tuning.voltaicHopDelay);chain.Wait=chain.HopDelay;chain.Count=chain.Next=0;
            int desired=Mathf.Clamp(Mathf.RoundToInt(At(tuning.prismCounts,tier))+stats.ExtraProjectileCount,0,chain.Targets.Length);
            Vector3 previous=chain.Origin;
            var active=CombatTargetRegistry.ActiveTargets;
            for(int hop=0;hop<desired;hop++)
            {
                EnemyActor closest=null;float closestSquared=float.MaxValue;
                for(int i=0;i<active.Count;i++)
                {
                    if(!(active[i] is EnemyActor enemy)||enemy.Session!=session||!enemy.IsAlive||enemy.CurrentHealth<=0||enemy.TargetId==hit.TargetId)continue;
                    var fromOrigin=enemy.WorldCenter-chain.Origin;fromOrigin.y=0;
                    if(fromOrigin.sqrMagnitude>chain.Radius*chain.Radius)continue;
                    bool visited=false;for(int j=0;j<chain.Count;j++)if(chain.Targets[j]==enemy){visited=true;break;}
                    if(visited)continue;
                    var delta=enemy.WorldCenter-previous;delta.y=0;float square=delta.sqrMagnitude;
                    if(square>closestSquared || (Mathf.Approximately(square,closestSquared)&&closest!=null&&enemy.TargetId>closest.TargetId))continue;
                    closest=enemy;closestSquared=square;
                }
                if(closest==null)break;
                chain.Targets[chain.Count]=closest;chain.Versions[chain.Count]=closest.SpawnVersion;chain.Count++;
                previous=closest.WorldCenter;
            }
            if(chain.Count==0){ReleaseChain(chain);return;}
            chain.Active=true;chain.Order=ChainsTriggered++;ActiveChainCount++;
        }
        private bool ResolveVoltaicHop(VoltaicChain chain,uint epoch)
        {
            int hop=chain.Next++;
            EnemyActor target=chain.Targets[hop];int version=chain.Versions[hop];
            var offset=target!=null?target.WorldCenter-chain.Origin:Vector3.zero;offset.y=0;
            if(target==null||!target.IsAlive||target.CurrentHealth<=0||target.Session!=session||target.SpawnVersion!=version||offset.sqrMagnitude>chain.Radius*chain.Radius+.0001f)
            {ChainTargetsInvalidated++;return true;}
            Vector3 endpoint=target.WorldCenter;
            if(chain.PreviousActor!=null&&chain.PreviousActor.IsAlive&&chain.PreviousActor.SpawnVersion==chain.PreviousVersion)
                chain.Previous=chain.PreviousActor.WorldCenter;
            var request=new DamageRequest(player.TargetId,DamageSourceType.UpgradeProjectile,chain.Previous,
                Flat(endpoint-chain.Previous),chain.Damage,damageType:DamageType.Supernatural);
            var result=target.ReceiveDamage(in request);
            // A damage listener can restart the session or disable this component.
            if(!bound||voltaicEpoch!=epoch||!chain.Active)return false;
            lightning.Show(chain.PreviousActor,chain.PreviousVersion,chain.Previous,target,version,endpoint);
            if(result.AppliedRealDamage)
            {
                ChainHitsResolved++;
                effects.Burst(endpoint,.5f,new Color(.3f,.65f,1f),kind:UpgradeEffectPool.BurstKind.Electric);
            }
            chain.Previous=endpoint;chain.PreviousActor=target;chain.PreviousVersion=version;
            return true;
        }
        private void StepVoltaicChains(float dt)
        {
            uint epoch=voltaicEpoch;
            for(int i=0;i<voltaicChains.Length;i++)
            {
                var chain=voltaicChains[i];if(!chain.Active)continue;
                chain.Wait-=dt;
                while(chain.Wait<=.000001f && chain.Next<chain.Count)
                {
                    chain.Wait+=chain.HopDelay;
                    if(!ResolveVoltaicHop(chain,epoch))return;
                }
                if(chain.Next>=chain.Count){ReleaseChain(chain);ActiveChainCount--;}
            }
        }
        private void PulseResolved(PulseResolvedEvent pulse)
        {
            int tier=Attack(UpgradeId.CRIT_04);
            if(pulse.PulseIndex==4 && pulse.CritCount>0 && tier>0 && weapon!=null)
            {weapon.QueueGuaranteedCriticalPulses(Mathf.RoundToInt(At(tuning.guaranteedPulses,tier)));effects.Burst(player.WorldCenter,1.4f,Amber,kind:UpgradeEffectPool.BurstKind.Shock);}
        }
        private void Healed(HealResult heal)
        {
            if(!bound) return;
            if(heal.ActualHealed>0) effects.Burst(player.WorldCenter,1.25f,Repair,kind:UpgradeEffectPool.BurstKind.Repair);
            if(Tier(UpgradeId.BIO_04)==0) return;
            overheal+=Mathf.Max(0,heal.RequestedAmount-heal.ActualHealed);
            while(overheal>=tuning.novaOverheal) {overheal-=tuning.novaOverheal;QueueBlast(player.WorldCenter,tuning.novaDamage,tuning.novaRadius,damageType:DamageType.Supernatural);}
        }
        private bool CanResolveContextualUpgrade => bound && isActiveAndEnabled && player.IsAlive &&
            !session.IsPaused && session.State == SessionState.Running && session.IsGameplayFlowActive();

        private void ClearContextualUpgrades()
        {
            stats.ClearCombatMedkit();
            effects.Release(returnMarker);
            returnMarker = null;
            effects.Release(auraView);
            auraView = null;
            trackingDash = false;
            trailInitialized = false;
            wallSlamReadyAt = 0f;
            System.Array.Clear(cables, 0, cables.Length);
        }

        public void TriggerWallSlam(Vector3 position, int tier)
        {
            if (!CanResolveContextualUpgrade || tier <= 0 || time < wallSlamReadyAt) return;
            wallSlamReadyAt = time + tuning.wallSlamInterval;
            QueueBlast(position, At(tuning.wallSlamDamage, tier), tuning.wallSlamRadius,
                stun: tuning.wallSlamStun, source: DamageSourceType.WallSlam, shockwave: true);
        }

        private void DashResolved(DashStateEvent dash)
        {
            if (dash.Cancelled)
            {
                trackingDash = false;
                trailInitialized = false;
                effects.Release(returnMarker);
                returnMarker = null;
                return;
            }
            if (dash.Returned)
            {
                effects.Release(returnMarker);
                returnMarker = null;
                int tier = Tier(UpgradeId.DASH_06);
                if (tier > 0 && CanResolveContextualUpgrade)
                {
                    lightning.Show(null, 0, dash.ReturnOrigin, null, 0, dash.Position);
                    DamageDashSegment(dash.ReturnOrigin, dash.Position, tuning.returnWidth, At(tuning.returnDamage, tier),
                        DamageSourceType.DashReturn);
                }
                return;
            }
            if(dash.Started)
            {
                trackingDash = CanResolveContextualUpgrade;
                dashStart = dash.Position;
                if (trackingDash && Tier(UpgradeId.FLASH_13) > 0)
                    stats.ActivateSolarCapacitor(tuning.solarCapacitorDuration);
                int trailTier = Tier(UpgradeId.DASH_08);
                if (trackingDash && trailTier > 0)
                {
                    trailInitialized = true;
                    lastTrailPos = dash.Position;
                    PlaceFlameTrail(dash.Position, trailTier);
                }
                if (trackingDash && motor != null && motor.HasReturnAnchor)
                    returnMarker = effects.Acquire(UpgradeEffectPool.Shape.Decoy, dashStart, Plasma, .9f);
                int decoy=Tier(UpgradeId.DASH_03);
                int mine = Tier(UpgradeId.DEMO_03);
                if (mine > 0)
                    PlaceDevice(DeviceKind.Mine, tuning.mineLimit, dash.Position, float.PositiveInfinity, At(tuning.mineDamage, mine));
                if(decoy>0) DeviceAt(DeviceKind.Decoy,dash.Position,At(tuning.decoyDuration,decoy),At(tuning.decoyDamage,decoy));
                if(Tier(UpgradeId.DASH_04)>0) QueueBlast(dash.Position,tuning.cataclysmDamage,tuning.cataclysmRadius);
            }
            if(dash.Ended)
            {
                if (trackingDash && CanResolveContextualUpgrade)
                {
                    float length = Vector3.ProjectOnPlane(dash.Position - dashStart, Vector3.up).magnitude;
                    int cable = Tier(UpgradeId.DASH_07);
                    if (cable > 0 && length > tuning.cableWidth &&
                        !Physics.Linecast(dashStart, dash.Position, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore))
                        PlaceElectricCable(dashStart, dash.Position, cable);
                    int trailTier = Tier(UpgradeId.DASH_08);
                    if (trailTier > 0 && trailInitialized)
                    {
                        Vector3 delta = dash.Position - lastTrailPos; delta.y = 0f;
                        float dist = delta.magnitude;
                        float step = Mathf.Max(.2f, tuning.trailStepDistance);
                        int steps = Mathf.Min(10, Mathf.FloorToInt(dist / step));
                        Vector3 dir = dist > .001f ? delta / dist : Vector3.zero;
                        for (int s = 1; s <= steps; s++)
                            PlaceFlameTrail(lastTrailPos + dir * (s * step), trailTier);
                        if (dist >= .35f && steps == 0)
                            PlaceFlameTrail(dash.Position, trailTier);
                    }
                }
                trackingDash = false;
                trailInitialized = false;
                int shock=Tier(UpgradeId.DASH_02);
                if(shock>0) QueueBlast(dash.Position,At(tuning.shockDamage,shock),At(tuning.shockRadius,shock),stun:At(tuning.shockStun,shock),push:tuning.shockPushDistance,shockwave:true);
                if(Tier(UpgradeId.DASH_04)>0) Fire(dash.Position,dash.Direction,tuning.bladeDamage,tuning.bladeSpeed,tuning.bladeLifetime,DamageSourceType.UpgradeBlade,null,true,0,tuning.bladeHitRadius);
            }
        }
        private void DamageDashSegment(Vector3 from, Vector3 to, float width, float damage, DamageSourceType source)
        {
            EnemyActor.GatherSegmentCandidates(from, to, width, segmentTargets);
            foreach (EnemyActor enemy in segmentTargets)
            {
                if (!CanResolveContextualUpgrade) break;
                if (enemy == null || !enemy.IsAlive || enemy.Session != session ||
                    enemy is GolgoraActor { IsAirborne: true } ||
                    !EnemyActor.TouchesSegment(from, to, enemy.WorldCenter, width + enemy.HitRadius, out float fraction))
                    continue;

                Vector3 point = Vector3.Lerp(from, to, fraction);
                if (Physics.Linecast(point, enemy.WorldCenter, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore))
                    continue;

                if (Deal(enemy, damage, point, source, canCrit: false).AppliedRealDamage)
                    effects.Impact(enemy.WorldCenter, Plasma);
            }
        }

        private void PlaceElectricCable(Vector3 start, Vector3 end, int tier)
        {
            int slot = 0;
            for (int i = 1; i < cables.Length; i++)
                if (cables[i].ExpiresAt < cables[slot].ExpiresAt) slot = i;

            float duration = At(tuning.cableDuration, tier);
            cables[slot] = new ElectricCable { Start = start, End = end,
                Damage = At(tuning.cableDamage, tier), ExpiresAt = time + duration };
            lightning.ShowCable(slot, start - Vector3.up * .45f, end - Vector3.up * .45f, duration);
        }

        private void StepElectricCables()
        {
            for (int i = 0; i < cables.Length; i++)
            {
                var cable = cables[i];
                if (cable.ExpiresAt <= time) continue;
                List<EnemyActor> candidates = targets;
                if (allTargetsIndexed)
                {
                    EnemyActor.GatherSegmentCandidatesUnsorted(cable.Start, cable.End, tuning.cableWidth, segmentTargets);
                    candidates = segmentTargets;
                }
                foreach (EnemyActor enemy in candidates)
                {
                    if (!CanResolveContextualUpgrade) return;
                    if (enemy == null || !enemy.IsAlive || enemy.CurrentHealth <= 0f || enemy.Session != session ||
                        enemy is GolgoraActor { IsAirborne: true } ||
                        !EnemyActor.TouchesSegment(cable.Start, cable.End, enemy.WorldCenter, tuning.cableWidth + enemy.HitRadius, out float fraction))
                        continue;

                    Vector3 point = Vector3.Lerp(cable.Start, cable.End, fraction);
                    if (Physics.Linecast(point, enemy.WorldCenter, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore))
                        continue;
                    if (enemy.TouchElectricCable(time, tuning.cableInterval, tuning.cableSlow))
                        Deal(enemy, cable.Damage, point, DamageSourceType.ElectricCable, canCrit: false);
                }
            }
        }

        public void EnemyDied(EnemyActor enemy, Vector3 position)
        {
            if(!bound) return;
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
            if (pickup.Kind == CombatPickupKind.Medkit)
            {
                int tier = Tier(UpgradeId.BIO_05);
                if (tier > 0 && CanResolveContextualUpgrade)
                {
                    stats.ActivateCombatMedkit(At(tuning.medkitDuration, tier));
                    effects.Burst(player.WorldCenter, 1.4f, Repair, kind: UpgradeEffectPool.BurstKind.Repair);
                }
                return;
            }
            if (pickup.Kind != CombatPickupKind.Experience) return;
            int batteryTier = Tier(UpgradeId.DASH_10);
            if (batteryTier > 0 && CanResolveContextualUpgrade)
            {
                batteryGems += pickup.GemCount;
                while (batteryGems >= tuning.batteryGemCount)
                {
                    batteryGems -= tuning.batteryGemCount;
                    motor?.ReduceCooldownFraction(At(tuning.batteryCooldownReduction, batteryTier));
                    effects.Burst(player.WorldCenter, 1.1f, Plasma, kind: UpgradeEffectPool.BurstKind.Electric);
                }
            }
            int assemblerTier = Tier(UpgradeId.SCRAP_02);
            if (assemblerTier == 0) return;
            gems += pickup.GemCount;
            while (gems >= tuning.assemblerGemCount)
            {
                gems -= tuning.assemblerGemCount;
                var target = Nearest(player.WorldCenter, 1000);
                if (target != null)
                {
                    int bolts = 1 + stats.ExtraProjectileCount;
                    Vector3 baseDir = Flat(target.WorldCenter - player.WorldCenter);
                    float dmg = At(tuning.assemblerDamage, assemblerTier);
                    for (int b = 0; b < bolts; b++)
                    {
                        float yaw = bolts == 1 ? 0f : (b - (bolts - 1) * 0.5f) * 14f;
                        Vector3 dir = yaw == 0f ? baseDir : Quaternion.Euler(0f, yaw, 0f) * baseDir;
                        Fire(player.WorldCenter, dir, dmg, tuning.plasmaSpeed, 10, DamageSourceType.UpgradeProjectile, b == 0 ? target : null, damageType: DamageType.Physical);
                    }
                }
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
            try {pickups.AttractAll();effects.Burst(player.WorldCenter,Mathf.Min(12f,stats.MagnetRadius+5),Plasma,true,UpgradeEffectPool.BurstKind.Collection);}
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
        private Projectile AcquireProjectile(Vector3 position, Vector3 direction, float damage, float speed, float life, DamageSourceType source,
            EnemyActor target = null, bool piercing = false, float freeze = 0, float radius = .25f, EnemyActor ignore = null,
            DamageType damageType = DamageType.Inherit, ProjectileMode mode = ProjectileMode.Normal,
            UpgradeEffectPool.Shape? shapeOverride = null, Color? colorOverride = null, float viewScale = 0f)
        {
            for (int i = 0; i < projectiles.Length; i++)
            {
                var p = projectiles[i]; if (p.Active) continue;
                p.Active = true; p.Mode = mode; p.BoolFlag = false;
                p.Position = position; p.StartPosition = position; p.EndPosition = position;
                p.Direction = Flat(direction); p.Damage = damage; p.Speed = speed; p.Life = life;
                p.FloatData = p.Elapsed = p.Duration = 0f;
                p.Source = source; p.DamageType = DamageTypes.Resolve(source, damageType);
                p.Target = target; p.TargetVersion = target != null ? target.SpawnVersion : 0;
                p.Piercing = piercing; p.Freeze = freeze; p.Radius = radius;
                p.Hits.Clear(); p.HitCooldowns.Clear();
                if (ignore != null) p.Hits[ignore.TargetId] = ignore.SpawnVersion;
                var shape = shapeOverride ?? (source == DamageSourceType.UpgradeBlade ? UpgradeEffectPool.Shape.Blade : UpgradeEffectPool.Shape.Dart);
                Color color = colorOverride ?? (freeze > 0 ? Ice : p.DamageType == DamageType.Physical ? Amber : Plasma);
                float scale = viewScale > 0f ? viewScale : (source == DamageSourceType.UpgradeBlade ? 1.5f : .65f);
                p.View = effects.Acquire(shape, position, color, scale);
                if (p.View != null && p.Direction.sqrMagnitude > .0001f)
                    p.View.Root.rotation = Quaternion.LookRotation(p.Direction);
                ActiveProjectileCount++; ProjectilesFired++;
                return p;
            }
            return null;
        }
        private void Fire(Vector3 position,Vector3 direction,float damage,float speed,float life,DamageSourceType source,
            EnemyActor target=null,bool piercing=false,float freeze=0,float radius=.25f,EnemyActor ignore=null,DamageType damageType=DamageType.Inherit) =>
            AcquireProjectile(position, direction, damage, speed, life, source, target, piercing, freeze, radius, ignore, damageType);
        private void QueueBlast(Vector3 p,float damage,float radius,float delay=0,float stun=0,float push=0,float freeze=0, bool echo=false,bool implosion=false,DamageSourceType source=DamageSourceType.UpgradeExplosion,DamageType damageType=DamageType.Inherit,bool shockwave=false)
        {
            if(blastCount>=blasts.Length) return;
            bool explosion=source==DamageSourceType.UpgradeExplosion;
            if(explosion&&!echo) {damage*=stats.ExplosionDamageMultiplier;radius*=stats.ExplosionRadiusMultiplier;}
            else if(!echo) radius*=stats.AreaMultiplier;
            blasts[blastCount++]=new Blast {Position=p,Damage=damage,Radius=radius,Delay=delay,Stun=stun,Push=push,Freeze=freeze,Echo=echo,Implosion=implosion,Shockwave=shockwave,Source=source,DamageType=DamageTypes.Resolve(source,damageType)};
        }
        private void DeviceAt(DeviceKind kind,Vector3 position,float life,float damage)
        {
            for(int i=0;i<devices.Length;i++)
            {
                var d=devices[i];if(d.Active) continue;
                d.Active=true;d.Fresh=true;d.Kind=kind;d.Position=position;d.Life=life;d.Damage=damage;d.PlacedAt=time;d.PlacementOrder=++nextPlacementOrder;d.BurstTimer=d.ShotTimer=0;d.Shots=0;
                var shape=kind==DeviceKind.Mine?UpgradeEffectPool.Shape.Mine:kind==DeviceKind.Decoy?UpgradeEffectPool.Shape.Decoy:UpgradeEffectPool.Shape.Turret;
                var p=position;if(kind!=DeviceKind.Decoy)p.y=ArenaSurface.Height(position)+(kind==DeviceKind.Turret?.025f:.15f);
                d.View=effects.Acquire(shape,p,kind==DeviceKind.Decoy?Plasma:Amber,kind==DeviceKind.Mine?.65f:1f);
                if(kind==DeviceKind.Decoy)activeDecoys++;
                ActiveDeviceCount++;return;
            }
        }

        private void PlaceDevice(DeviceKind kind, int limit, Vector3 position, float lifetime, float damage)
        {
            Device oldest = null;
            int count = 0;
            foreach (var device in devices)
            {
                if (!device.Active || device.Kind != kind) continue;
                count++;
                if (oldest == null || device.PlacementOrder < oldest.PlacementOrder) oldest = device;
            }
            if (count >= Mathf.Max(1, limit)) ReleaseDevice(oldest);
            DeviceAt(kind, position, lifetime, damage);
        }

        private void ReleaseDevice(Device device)
        {
            if (device.Kind == DeviceKind.Decoy) activeDecoys--;
            device.Active = false;
            effects.Release(device.View);
            device.View = null;
            ActiveDeviceCount--;
        }

        public void Step(float dt)
        {
            if(!bound || dt<=0 || session.IsPaused || session.State == SessionState.BossIntro || !session.IsGameplayFlowActive() || !player.IsAlive || !isActiveAndEnabled) return;
            EnemyActor.InvalidateNeighbors();
            time+=dt; effects.Step(dt);lightning.Step(dt);
            stats.StepCombatMedkit(dt);
            stats.StepSolarCapacitor(dt);
            if (returnMarker != null && (motor == null || !motor.HasReturnAnchor))
            {
                effects.Release(returnMarker);
                returnMarker = null;
            }
            if (motor != null && motor.IsDashing && Tier(UpgradeId.DASH_08) > 0 && CanResolveContextualUpgrade)
            {
                Vector3 pos = player.WorldCenter;
                float step = tuning.trailStepDistance;
                Vector3 delta = pos - lastTrailPos; delta.y = 0f;
                if (!trailInitialized) { trailInitialized = true; lastTrailPos = pos; PlaceFlameTrail(pos, Tier(UpgradeId.DASH_08)); }
                else if (delta.sqrMagnitude >= step * step) { lastTrailPos = pos; PlaceFlameTrail(pos, Tier(UpgradeId.DASH_08)); }
            }
            Gather();
            for(int i=0;i<targets.Count;i++) targets[i].StepUpgradeStatus(dt);
            int tier=Tier(UpgradeId.BIO_03);
            if(tier>0) {regenTimer+=dt;while(regenTimer>=tuning.regenerationInterval) {regenTimer-=tuning.regenerationInterval;player.Heal(At(tuning.regeneration,tier));}} else regenTimer=0;
            tier=Tier(UpgradeId.CRYO_03);
            if(tier>0) {blizzardTimer+=dt;while(blizzardTimer>=At(tuning.blizzardInterval,tier)) {blizzardTimer-=At(tuning.blizzardInterval,tier);QueueBlast(player.WorldCenter,At(tuning.blizzardDamage,tier),tuning.blizzardRadius,freeze:tuning.blizzardFreeze,source:DamageSourceType.UpgradeFrost);}} else blizzardTimer=0;
            if (Tier(UpgradeId.SCRAP_04) > 0)
            {
                turretTimer += dt;
                while (turretTimer >= tuning.turretInterval)
                {
                    turretTimer -= tuning.turretInterval;
                    PlaceDevice(DeviceKind.Turret, MaxActiveTurrets, player.WorldCenter, tuning.turretDuration, tuning.turretDamage);
                }
            }
            else turretTimer = 0f;
            StepAura(dt);
            StepPlasmaPuddles(dt);
            StepBouncingDiscs(dt);
            StepBoomerangs(dt);
            StepOrbitalLightning(dt);
            StepParabolicScrap(dt);
            StepWhipSweep(dt);
            StepGroundZones(dt);
            StepElectricCables();
            StepDevices(dt); StepProjectiles(dt); StepVoltaicChains(dt); StepSaws(); StepBlasts(dt); StepStatusViews();
            if(player.UpgradeProtectionRemaining>0)
            {
                shield??=effects.Acquire(UpgradeEffectPool.Shape.Shield,player.WorldCenter,new Color(.3f,.85f,1f),1.1f);
                if(shield!=null)
                {
                    if(collectorSprite!=null && collectorSprite.CharacterRenderer!=null)
                    {
                        Bounds body=collectorSprite.CharacterRenderer.bounds;
                        float radius=Mathf.Max(.9f,body.extents.x*1.5f,body.extents.z*1.5f);
                        shield.Root.position=body.center;
                        shield.Root.localScale=new Vector3(radius,Mathf.Max(1.2f,body.extents.y*1.22f)/1.25f,radius);
                    }
                    else shield.Root.position=player.WorldCenter+Vector3.up*.15f;
                }
            }
            else {effects.Release(shield);shield=null;}
        }
        private void StepAura(float dt)
        {
            int tier = Tier(UpgradeId.SCRAP_05);
            if (tier <= 0)
            {
                auraTimer = 0f;
                if (auraView != null) { effects.Release(auraView); auraView = null; }
                return;
            }
            float radius = At(tuning.auraRadius, tier) * stats.AreaMultiplier;
            Vector3 center = player.WorldCenter;
            Vector3 groundPos = center; groundPos.y = ArenaSurface.Height(center) + .06f;
            auraView ??= effects.Acquire(UpgradeEffectPool.Shape.Ring, groundPos, Plasma, radius);
            if (auraView != null) effects.ProjectGroundRing(auraView, center, radius);
            auraTimer += dt;
            float interval = Mathf.Max(.05f, tuning.auraInterval);
            while (auraTimer >= interval)
            {
                auraTimer -= interval;
                float damage = At(tuning.auraDamage, tier);
                List<EnemyActor> list = targets;
                if (allTargetsIndexed && radius <= 24f)
                {
                    EnemyActor.GatherCandidatesUnsorted(center, radius, spatialCandidates);
                    list = spatialCandidates;
                }
                for (int i = 0; i < list.Count; i++)
                {
                    EnemyActor enemy = list[i];
                    if (enemy == null || !enemy.IsAlive || enemy.CurrentHealth <= 0f || enemy.Session != session) continue;
                    Vector3 offset = enemy.WorldCenter - center; offset.y = 0f;
                    float hitRange = radius + enemy.HitRadius;
                    if (offset.sqrMagnitude > hitRange * hitRange) continue;
                    var hit = Deal(enemy, damage, center, DamageSourceType.StaticAura, DamageType.Supernatural);
                    if (hit.AppliedRealDamage && enemy.IsAlive && enemy.CurrentHealth > 0f && tuning.auraPush > 0f)
                        enemy.ApplyKnockback(Flat(offset) * tuning.auraPush);
                }
            }
        }
        private void StepPlasmaPuddles(float dt)
        {
            int tier = Tier(UpgradeId.DEMO_05);
            if (tier <= 0) { puddleTimer = 0f; return; }
            float interval = Mathf.Max(.2f, At(tuning.puddleInterval, tier));
            puddleTimer += dt;
            while (puddleTimer >= interval)
            {
                puddleTimer -= interval;
                EnemyActor target = Nearest(player.WorldCenter, tuning.puddleThrowRange);
                Vector3 fwd = weapon != null ? Flat(weapon.CurrentAimDirection) : Flat(player.transform.forward);
                Vector3 pos = target != null ? target.WorldCenter : player.WorldCenter + fwd * 4f;
                float radius = tuning.puddleRadius * stats.AreaMultiplier;
                PlaceGroundZone(GroundZoneKind.PlasmaPuddle, pos, radius, At(tuning.puddleDamage, tier),
                    tuning.puddleTick, tuning.puddleDuration, DamageSourceType.PlasmaPuddle, DamageType.Supernatural);
                effects.Burst(pos, radius * .45f, Plasma, kind: UpgradeEffectPool.BurstKind.PlasmaImpact);
            }
        }
        private void StepBouncingDiscs(float dt)
        {
            int tier = Tier(UpgradeId.SCRAP_06);
            if (tier <= 0) { discTimer = 0f; return; }
            float interval = Mathf.Max(.2f, tuning.discInterval);
            discTimer += dt;
            while (discTimer >= interval)
            {
                discTimer -= interval;
                int count = Mathf.RoundToInt(At(tuning.discCount, tier)) + stats.ExtraProjectileCount;
                float dmg = At(tuning.discDamage, tier);
                EnemyActor target = Nearest(player.WorldCenter, tuning.discBounceRange);
                Vector3 baseDir = target != null
                    ? Flat(target.WorldCenter - player.WorldCenter)
                    : (weapon != null ? Flat(weapon.CurrentAimDirection) : Flat(player.transform.forward));
                for (int i = 0; i < count; i++)
                {
                    float yaw = count <= 1 ? 0f : (i - (count - 1) * .5f) * 24f;
                    Vector3 dir = yaw == 0f ? baseDir : Quaternion.Euler(0f, yaw, 0f) * baseDir;
                    var p = AcquireProjectile(player.WorldCenter, dir, dmg, tuning.discSpeed, tuning.discLifetime,
                        DamageSourceType.BouncingDisc, piercing: true, radius: tuning.discRadius * stats.AreaMultiplier,
                        damageType: DamageType.Physical, mode: ProjectileMode.BouncingDisc,
                        shapeOverride: UpgradeEffectPool.Shape.Saw, colorOverride: Amber, viewScale: .42f);
                    if (p != null) p.FloatData = tuning.discHitCooldown;
                }
            }
        }
        private void StepBoomerangs(float dt)
        {
            int tier = Tier(UpgradeId.GREED_07);
            if (tier <= 0) { boomerangTimer = 0f; return; }
            float interval = Mathf.Max(.2f, tuning.boomerangInterval);
            boomerangTimer += dt;
            while (boomerangTimer >= interval)
            {
                boomerangTimer -= interval;
                int count = Mathf.RoundToInt(At(tuning.boomerangCount, tier)) + stats.ExtraProjectileCount;
                float dmg = At(tuning.boomerangDamage, tier);
                EnemyActor target = Nearest(player.WorldCenter, 18f);
                Vector3 baseDir = target != null
                    ? Flat(target.WorldCenter - player.WorldCenter)
                    : (weapon != null ? Flat(weapon.CurrentAimDirection) : Flat(player.transform.forward));
                for (int i = 0; i < count; i++)
                {
                    float yaw = count <= 1 ? 0f : (i - (count - 1) * .5f) * 28f;
                    Vector3 dir = yaw == 0f ? baseDir : Quaternion.Euler(0f, yaw, 0f) * baseDir;
                    var p = AcquireProjectile(player.WorldCenter, dir, dmg, tuning.boomerangSpeed,
                        tuning.boomerangOutTime * 2.5f + 2f, DamageSourceType.Boomerang, piercing: true,
                        radius: tuning.boomerangHitRadius * stats.AreaMultiplier, damageType: DamageType.Physical,
                        mode: ProjectileMode.Boomerang, shapeOverride: UpgradeEffectPool.Shape.Blade,
                        colorOverride: Plasma, viewScale: 1f);
                    if (p != null) { p.Duration = tuning.boomerangOutTime; p.Elapsed = 0f; p.BoolFlag = false; }
                }
            }
        }
        private void StepOrbitalLightning(float dt)
        {
            int tier = Tier(UpgradeId.FLASH_09);
            if (tier <= 0) { orbitalTimer = 0f; return; }
            float interval = Mathf.Max(.2f, At(tuning.orbitalInterval, tier));
            orbitalTimer += dt;
            while (orbitalTimer >= interval)
            {
                orbitalTimer -= interval;
                int count = Mathf.RoundToInt(At(tuning.orbitalCount, tier)) + stats.ExtraProjectileCount;
                float dmg = At(tuning.orbitalDamage, tier);
                selectedTargets.Clear();
                for (int i = 0; i < count; i++)
                {
                    EnemyActor target = Nearest(player.WorldCenter, tuning.orbitalRange, unique: true);
                    if (target == null) break;
                    selectedTargets.Add(target.TargetId);
                    Vector3 strikePos = target.WorldCenter;
                    lightning.ShowStrike(strikePos + Vector3.up * 7.5f, strikePos, .28f);
                    QueueBlast(strikePos, dmg, tuning.orbitalRadius, source: DamageSourceType.OrbitalLightning, damageType: DamageType.Supernatural);
                }
                selectedTargets.Clear();
            }
        }
        private void StepParabolicScrap(float dt)
        {
            int tier = Tier(UpgradeId.DEMO_06);
            if (tier <= 0) { parabolicTimer = 0f; return; }
            float interval = Mathf.Max(.2f, tuning.parabolicInterval);
            parabolicTimer += dt;
            while (parabolicTimer >= interval)
            {
                parabolicTimer -= interval;
                int count = Mathf.RoundToInt(At(tuning.parabolicCount, tier)) + stats.ExtraProjectileCount;
                float dmg = At(tuning.parabolicDamage, tier);
                Vector3 fwd = weapon != null ? Flat(weapon.CurrentAimDirection) : Flat(player.transform.forward);
                selectedTargets.Clear();
                for (int i = 0; i < count; i++)
                {
                    EnemyActor target = Nearest(player.WorldCenter, 12f, unique: true);
                    Vector3 endPos;
                    if (target != null)
                    {
                        selectedTargets.Add(target.TargetId);
                        endPos = target.WorldCenter;
                    }
                    else
                    {
                        Vector3 dir = Quaternion.Euler(0f, i * (360f / Mathf.Max(1, count)), 0f) * fwd;
                        endPos = player.WorldCenter + dir * 6.5f;
                    }
                    float dist = Vector3.ProjectOnPlane(endPos - player.WorldCenter, Vector3.up).magnitude;
                    float duration = Mathf.Clamp(dist / Mathf.Max(1f, tuning.parabolicSpeed), .35f, tuning.parabolicLifetime);
                    var p = AcquireProjectile(player.WorldCenter, endPos - player.WorldCenter, dmg, tuning.parabolicSpeed,
                        duration, DamageSourceType.ParabolicScrap, piercing: true,
                        radius: tuning.parabolicRadius * stats.AreaMultiplier, damageType: DamageType.Physical,
                        mode: ProjectileMode.Parabolic, shapeOverride: UpgradeEffectPool.Shape.Mine,
                        colorOverride: Amber, viewScale: .8f);
                    if (p != null) { p.StartPosition = player.WorldCenter; p.EndPosition = endPos; p.Duration = duration; p.Elapsed = 0f; }
                }
                selectedTargets.Clear();
            }
        }
        private void StepWhipSweep(float dt)
        {
            int tier = Tier(UpgradeId.FLASH_10);
            if (tier <= 0) { whipTimer = 0f; return; }
            float interval = Mathf.Max(.2f, tuning.whipInterval);
            whipTimer += dt;
            while (whipTimer >= interval)
            {
                whipTimer -= interval;
                Vector3 origin = player.WorldCenter;
                Vector3 fwd = weapon != null ? Flat(weapon.CurrentAimDirection) : Flat(player.transform.forward);
                float range = tuning.whipRange * stats.AreaMultiplier;
                float halfCos = Mathf.Cos(tuning.whipAngle * .5f * Mathf.Deg2Rad);
                float dmg = tuning.whipBaseDamage + tier * tuning.whipDamagePerTier;
                effects.Burst(origin + fwd * (range * .45f), range * .55f, Amber, kind: UpgradeEffectPool.BurstKind.Shock);
                List<EnemyActor> list = targets;
                if (allTargetsIndexed && range <= 24f)
                {
                    EnemyActor.GatherCandidatesUnsorted(origin, range, spatialCandidates);
                    list = spatialCandidates;
                }
                for (int i = 0; i < list.Count; i++)
                {
                    EnemyActor enemy = list[i];
                    if (enemy == null || !enemy.IsAlive || enemy.CurrentHealth <= 0f || enemy.Session != session) continue;
                    Vector3 offset = enemy.WorldCenter - origin; offset.y = 0f;
                    float maxDist = range + enemy.HitRadius;
                    if (offset.sqrMagnitude > maxDist * maxDist) continue;
                    if (offset.sqrMagnitude > .0001f && Vector3.Dot(offset.normalized, fwd) < halfCos) continue;
                    var hit = Deal(enemy, dmg, origin, DamageSourceType.WhipSweep, DamageType.Physical);
                    if (hit.AppliedRealDamage && enemy.IsAlive && enemy.CurrentHealth > 0f && tuning.whipPush > 0f)
                        enemy.ApplyKnockback(Flat(offset) * tuning.whipPush);
                }
            }
        }
        private void PlaceFlameTrail(Vector3 position, int tier) =>
            PlaceGroundZone(GroundZoneKind.FlameTrail, position, tuning.trailRadius * stats.AreaMultiplier,
                At(tuning.trailDamage, tier), tuning.trailTick, tuning.trailDuration,
                DamageSourceType.FlameTrail, DamageType.Supernatural);
        private void PlaceGroundZone(GroundZoneKind kind, Vector3 position, float radius, float damagePerTick,
            float tickInterval, float life, DamageSourceType source, DamageType damageType)
        {
            GroundZone slot = null, oldest = null;
            for (int i = 0; i < groundZones.Length; i++)
            {
                var z = groundZones[i];
                if (z == null) continue;
                if (!z.Active) { slot = z; break; }
                if (oldest == null || z.Life < oldest.Life) oldest = z;
            }
            if (slot == null)
            {
                slot = oldest;
                if (slot == null) return;
                effects.Release(slot.View);
                slot.View = null;
            }
            Vector3 pos = position; pos.y = ArenaSurface.Height(position) + .06f;
            slot.Active = true; slot.Kind = kind; slot.Position = pos; slot.Radius = radius;
            slot.DamagePerTick = damagePerTick; slot.TickInterval = Mathf.Max(.05f, tickInterval);
            slot.TickTimer = 0f; slot.Life = life; slot.Slow = 0f; slot.Vulnerability = 0f;
            slot.Source = source; slot.DamageType = damageType;
            slot.View = effects.Acquire(UpgradeEffectPool.Shape.Ring, pos, kind == GroundZoneKind.FlameTrail ? FlameColor : Plasma, radius);
            if (slot.View != null) effects.ProjectGroundRing(slot.View, pos, radius);
        }
        private void StepGroundZones(float dt)
        {
            for (int i = 0; i < groundZones.Length; i++)
            {
                var z = groundZones[i];
                if (z == null || !z.Active) continue;
                z.Life -= dt;
                if (z.Life <= 0f)
                {
                    z.Active = false;
                    effects.Release(z.View);
                    z.View = null;
                    continue;
                }
                z.TickTimer -= dt;
                while (z.TickTimer <= 0f)
                {
                    z.TickTimer += z.TickInterval;
                    List<EnemyActor> list = targets;
                    if (allTargetsIndexed && z.Radius <= 24f)
                    {
                        EnemyActor.GatherCandidatesUnsorted(z.Position, z.Radius, spatialCandidates);
                        list = spatialCandidates;
                    }
                    for (int e = 0; e < list.Count; e++)
                    {
                        EnemyActor enemy = list[e];
                        if (enemy == null || !enemy.IsAlive || enemy.CurrentHealth <= 0f || enemy.Session != session) continue;
                        Vector3 d = enemy.WorldCenter - z.Position; d.y = 0f;
                        float hitRadius = z.Radius + enemy.HitRadius;
                        if (d.sqrMagnitude <= hitRadius * hitRadius)
                            Deal(enemy, z.DamagePerTick, z.Position, z.Source, z.DamageType);
                    }
                }
            }
        }
        private void TriggerCryoMagnet(Vector3 center)
        {
            int tier = Tier(UpgradeId.CRYO_05);
            if (tier <= 0 || !CanResolveContextualUpgrade) return;
            float attractRadius = At(tuning.cryoMagnetRadius, tier) * stats.AreaMultiplier;
            pickups?.AttractWithin(center, attractRadius);
            float chillRadius = tuning.cryoMagnetSlowRadius * stats.AreaMultiplier;
            List<EnemyActor> list = targets;
            if (allTargetsIndexed && chillRadius <= 24f)
            {
                EnemyActor.GatherCandidatesUnsorted(center, chillRadius, cryoCandidates);
                list = cryoCandidates;
            }
            for (int i = 0; i < list.Count; i++)
            {
                EnemyActor enemy = list[i];
                if (enemy == null || !enemy.IsAlive || enemy.CurrentHealth <= 0f || enemy.Session != session) continue;
                Vector3 d = enemy.WorldCenter - center; d.y = 0f;
                float hitRadius = chillRadius + enemy.HitRadius;
                if (d.sqrMagnitude <= hitRadius * hitRadius)
                    enemy.ApplySlow(tuning.cryoMagnetSlow, tuning.cryoMagnetSlowDuration);
            }
            effects.Burst(center, Mathf.Max(.6f, chillRadius * .65f), Ice, kind: UpgradeEffectPool.BurstKind.Frost);
        }
        private void StepDevices(float dt)
        {
            for(int i=0;i<devices.Length;i++)
            {
                var d=devices[i];if(!d.Active)continue;float elapsed=d.Fresh?0:dt;d.Fresh=false;d.Life-=elapsed;
                bool expired=d.Life<=0;
                if (d.Kind == DeviceKind.Mine && time - d.PlacedAt >= tuning.mineArmDelay && Nearest(d.Position, tuning.mineTriggerRadius) != null)
                    expired = true;
                if(expired)
                {
                    if (d.Kind == DeviceKind.Mine)
                        QueueBlast(d.Position, d.Damage, tuning.mineRadius, push: tuning.minePushDistance, damageType: DamageType.Physical);
                    else if (d.Kind == DeviceKind.Decoy)
                        QueueBlast(d.Position, d.Damage, tuning.decoyExplosionRadius, implosion: true, damageType: DamageType.Supernatural);
                    ReleaseDevice(d);
                    continue;
                }
                if(d.Kind!=DeviceKind.Turret) continue;
                var tracked=Nearest(d.Position,tuning.turretRange);
                if(tracked!=null && d.View!=null)d.View.Turret?.AimAt(tracked.WorldCenter);
                d.BurstTimer-=elapsed; d.ShotTimer-=elapsed;
                if(d.Shots==0 && d.BurstTimer<=0) {d.Shots=tuning.turretBurstCount;d.BurstTimer=tuning.turretBurstInterval;d.ShotTimer=0;}
                while(d.Shots>0&&d.ShotTimer<=0)
                {
                    var target = tracked != null && tracked.IsAlive && tracked.CurrentHealth > 0 ? tracked : Nearest(d.Position, tuning.turretRange);
                    tracked = target;
                    if(target!=null)
                    {
                        var turret=d.View!=null?d.View.Turret:null;
                        turret?.AimAt(target.WorldCenter);
                        Vector3 muzzle=turret!=null?turret.MuzzleWorldPosition:d.Position+Vector3.up*.8f;
                        Fire(muzzle,target.WorldCenter-muzzle,d.Damage,tuning.turretProjectileSpeed,1,DamageSourceType.UpgradeTurret,target);
                        effects.Impact(muzzle,Amber);
                        turret?.Fire();
                    }
                    d.Shots--;d.ShotTimer+=tuning.turretShotInterval;
                }
            }
        }
        private EnemyActor NearestDiscTarget(Vector3 origin, int excludeId, Dictionary<int, float> cooldowns)
        {
            EnemyActor readyBest = null, fallbackBest = null;
            float readyDistSq = tuning.discBounceRange * tuning.discBounceRange;
            float fallbackDistSq = readyDistSq;
            for (int i = 0; i < targets.Count; i++)
            {
                EnemyActor enemy = targets[i];
                if (enemy == null || !enemy.IsAlive || enemy.CurrentHealth <= 0f || enemy.Session != session || enemy.TargetId == excludeId) continue;
                Vector3 d = enemy.WorldCenter - origin; d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq > fallbackDistSq) continue;
                bool onCooldown = cooldowns != null && cooldowns.TryGetValue(enemy.TargetId, out float readyAt) && time < readyAt;
                if (!onCooldown && sq <= readyDistSq) { readyBest = enemy; readyDistSq = sq; }
                fallbackBest = enemy; fallbackDistSq = sq;
            }
            return readyBest ?? fallbackBest;
        }
        private void StepProjectiles(float dt)
        {
            for(int i=0;i<projectiles.Length;i++)
            {
                var p=projectiles[i];if(!p.Active)continue;
                Vector3 from=p.Position,to;
                bool blocked=false;
                if (p.Mode == ProjectileMode.BouncingDisc)
                {
                    to = from + p.Direction * (p.Speed * dt);
                    if (Physics.Linecast(from, to, out RaycastHit wall, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore))
                    {
                        Vector3 n = wall.normal; n.y = 0f;
                        p.Direction = n.sqrMagnitude > .0001f ? Flat(Vector3.Reflect(p.Direction, n.normalized)) : -p.Direction;
                        to = wall.point + p.Direction * .08f; to.y = from.y;
                    }
                    p.Position = to; p.Life -= dt;
                    if (p.View != null) { p.View.Root.position = to; p.View.Root.rotation = Quaternion.Euler(0f, -time * 900f, 0f); }
                }
                else if (p.Mode == ProjectileMode.Boomerang)
                {
                    if (!p.BoolFlag)
                    {
                        float remainingOut = Mathf.Max(0f, p.Duration - p.Elapsed);
                        float stepOut = Mathf.Min(dt, remainingOut);
                        p.Elapsed += dt;
                        to = from + p.Direction * (p.Speed * stepOut);
                        if (p.Elapsed >= p.Duration)
                        {
                            p.BoolFlag = true;
                            p.Hits.Clear();
                            Vector3 home = player.WorldCenter;
                            Vector3 toPlayer = home - to; toPlayer.y = 0f;
                            if (toPlayer.sqrMagnitude > .0001f) p.Direction = toPlayer.normalized;
                        }
                    }
                    else
                    {
                        p.Elapsed += dt;
                        Vector3 home = player.WorldCenter;
                        Vector3 toPlayer = home - from; toPlayer.y = 0f;
                        if (toPlayer.sqrMagnitude > .0001f) p.Direction = toPlayer.normalized;
                        to = from + p.Direction * (p.Speed * 1.15f * dt);
                        Vector3 rem = home - to; rem.y = 0f;
                        if (rem.sqrMagnitude <= .65f * .65f) p.Life = 0f;
                    }
                    p.Position = to; p.Life -= dt;
                    if (p.View != null) { p.View.Root.position = to; p.View.Root.rotation = Quaternion.Euler(0f, time * 1080f, 0f); }
                }
                else if (p.Mode == ProjectileMode.Parabolic)
                {
                    p.Elapsed += dt;
                    float u = Mathf.Clamp01(p.Elapsed / Mathf.Max(.05f, p.Duration));
                    Vector3 nextFlat = Vector3.Lerp(p.StartPosition, p.EndPosition, u);
                    float arcY = ArenaSurface.Height(nextFlat) + .35f + Mathf.Sin(u * Mathf.PI) * 3.2f;
                    to = new Vector3(nextFlat.x, arcY, nextFlat.z);
                    p.Position = to; p.Life -= dt;
                    if (u >= 1f) p.Life = 0f;
                    if (p.View != null) { p.View.Root.position = to; p.View.Root.rotation = Quaternion.Euler(time * 540f, 0f, 0f); }
                }
                else
                {
                    if(p.Target!=null && p.Target.IsAlive && p.Target.SpawnVersion==p.TargetVersion) p.Direction=Flat(p.Target.WorldCenter-p.Position);
                    to=from+p.Direction*p.Speed*dt;
                    blocked=Physics.Linecast(from,to,out RaycastHit wall,1<<0,QueryTriggerInteraction.Ignore);
                    if(blocked) to=wall.point;
                    p.Position=to;p.Life-=dt;
                    if(p.View!=null) {p.View.Root.position=to;p.View.Root.rotation=Quaternion.LookRotation(p.Direction);}
                }
                List<EnemyActor> candidates = targets;
                if (allTargetsIndexed)
                {
                    EnemyActor.GatherSegmentCandidatesUnsorted(from, to, p.Radius, spatialCandidates);
                    candidates = spatialCandidates;
                }
                var delta=to-from;delta.y=0;
                float deltaSqr = delta.sqrMagnitude;
                for(int e=0;e<candidates.Count;e++)
                {
                    var enemy=candidates[e];
                    if(enemy==null || !enemy.IsAlive || enemy.CurrentHealth<=0 || enemy.Session!=session) continue;
                    if (p.Mode == ProjectileMode.BouncingDisc)
                    {
                        if (p.HitCooldowns.TryGetValue(enemy.TargetId, out float readyAt) && time < readyAt) continue;
                    }
                    else if (p.Hits.TryGetValue(enemy.TargetId, out int version) && version == enemy.SpawnVersion) continue;
                    var offset=enemy.WorldCenter-from;offset.y=0;
                    float t=deltaSqr>0?Mathf.Clamp01(Vector3.Dot(offset,delta)/deltaSqr):0;
                    float radius=enemy.HitRadius+p.Radius;
                    if((offset-delta*t).sqrMagnitude>radius*radius)continue;
                    if (p.Mode == ProjectileMode.BouncingDisc) p.HitCooldowns[enemy.TargetId] = time + p.FloatData;
                    else p.Hits[enemy.TargetId]=enemy.SpawnVersion;
                    var hit=Deal(enemy,p.Damage,from,p.Source,p.DamageType);
                    if(p.Freeze>0&&hit.AppliedRealDamage&&enemy.IsAlive&&enemy.CurrentHealth>0)
                    {
                        bool wasFrozen = enemy.IsFrozen;
                        enemy.ApplyFreeze(p.Freeze);
                        if (!wasFrozen && enemy.IsFrozen) TriggerCryoMagnet(enemy.WorldCenter);
                    }
                    if(hit.AppliedRealDamage)
                    {
                        if(p.Freeze<=0&&p.DamageType==DamageType.Supernatural)effects.Burst(enemy.WorldCenter,.45f,Plasma,kind:UpgradeEffectPool.BurstKind.PlasmaImpact);
                        else effects.Impact(enemy.WorldCenter,p.Freeze>0?Ice:Amber,p.Freeze>0);
                    }
                    if (p.Mode == ProjectileMode.BouncingDisc)
                    {
                        EnemyActor next = NearestDiscTarget(p.Position, enemy.TargetId, p.HitCooldowns);
                        if (next != null) p.Direction = Flat(next.WorldCenter - p.Position);
                        break;
                    }
                    if(!p.Piercing) {p.Life=0;break;}
                }
                if(p.Life<=0||blocked)
                {
                    if (p.Mode == ProjectileMode.Parabolic && p.Life <= 0f)
                        effects.Burst(p.EndPosition, p.Radius * 1.6f, Amber, kind: UpgradeEffectPool.BurstKind.Impact);
                    p.Active=false;effects.Release(p.View);p.View=null;p.Target=null;p.HitCooldowns.Clear();ActiveProjectileCount--;
                }
            }
        }
        private DamageResult Deal(EnemyActor enemy,float damage,Vector3 origin,DamageSourceType source,DamageType damageType=DamageType.Inherit,bool canCrit=true)
        {
            var request=new DamageRequest(player.TargetId,source,origin,Flat(enemy.WorldCenter-origin),damage,canCrit,stats.CritChance,stats.CritMultiplier,damageType:damageType);
            return enemy.ReceiveDamage(in request);
        }
        private void StepBlasts(float dt)
        {
            if (blastCount == 0) return;
            using var sample = blastsMarker.Auto();
            // Drain the ready prefix including chained deaths. Delayed echoes are compacted in place.
            int count=blastCount; int write=0;
            for(int i=0;i<blastCount;i++)
            {
                var b=blasts[i]; if(i<count)b.Delay-=dt;
                if(b.Delay>0) {blasts[write++]=b;continue;}
                effects.Burst(b.Position,b.Radius,b.Freeze>0?Ice:b.DamageType==DamageType.Supernatural?Plasma:Amber,b.Implosion,b.Freeze>0?UpgradeEffectPool.BurstKind.Frost:b.Implosion?UpgradeEffectPool.BurstKind.Implosion:b.Shockwave||b.Damage<=0?UpgradeEffectPool.BurstKind.Shock:b.DamageType==DamageType.Supernatural?UpgradeEffectPool.BurstKind.PlasmaExplosion:UpgradeEffectPool.BurstKind.Explosion);
                List<EnemyActor> list = targets;
                if (allTargetsIndexed && b.Radius <= 24f)
                {
                    EnemyActor.GatherCandidatesUnsorted(b.Position, b.Radius, spatialCandidates);
                    list = spatialCandidates;
                }
                for(int e=0;e<list.Count;e++)
                {
                    var enemy=list[e];if(enemy==null||!enemy.IsAlive||enemy.CurrentHealth<=0||enemy.Session!=session)continue;
                    var delta=enemy.WorldCenter-b.Position;delta.y=0;
                    float hitRange = b.Radius + enemy.HitRadius;
                    if (delta.sqrMagnitude > hitRange * hitRange) continue;
                    if(b.Damage>0 && !Deal(enemy,b.Damage,b.Position,b.Source,b.DamageType,canCrit:b.Source!=DamageSourceType.WallSlam).AppliedRealDamage) continue;
                    if(!enemy.IsAlive||enemy.CurrentHealth<=0)continue;
                    if(b.Freeze>0)
                    {
                        bool wasFrozen = enemy.IsFrozen;
                        enemy.ApplyFreeze(b.Freeze);
                        if (!wasFrozen && enemy.IsFrozen) TriggerCryoMagnet(enemy.WorldCenter);
                    }
                    if(b.Stun>0)enemy.ApplyStun(b.Stun);
                    if(b.Push>0)enemy.PushDistance(delta,b.Push);
                    if (b.Echo) enemy.PushDistance(-delta, Mathf.Min(delta.magnitude, b.Radius * tuning.implosionPullRadiusFraction));
                    else if (b.Implosion) enemy.ApplyKnockback(-Flat(delta) * tuning.implosionPullSpeed);
                }
                if(b.Source==DamageSourceType.UpgradeExplosion)
                {
                    ExplosionsResolved++;
                    if(!b.Echo&&Tier(UpgradeId.DEMO_04)>0)QueueBlast(b.Position,b.Damage*tuning.implosionFraction,b.Radius,tuning.implosionDelay,echo:true,implosion:true,damageType:DamageType.Supernatural);
                }
            }
            blastCount=write;
        }
        private void StepSaws()
        {
            int tier=Tier(UpgradeId.SCRAP_03);
            int count=tier>0?Mathf.Min(sawViews.Length,Mathf.RoundToInt(At(tuning.sawCounts,tier))+stats.ExtraProjectileCount):0;
            float orbitRadius=tuning.sawRadius*stats.AreaMultiplier;
            float hitRadiusThreshold=tuning.sawHitRadius*stats.AreaMultiplier;
            for(int i=0;i<sawViews.Length;i++)
            {
                if(i>=count) {effects.Release(sawViews[i]);sawViews[i]=null;continue;}
                float angle=(time*tuning.sawAngularSpeed+i*360f/count)*Mathf.Deg2Rad;
                Vector3 p=player.WorldCenter+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*orbitRadius;
                sawViews[i]??=effects.Acquire(UpgradeEffectPool.Shape.Saw,p,Amber,.55f);
                if(sawViews[i]!=null) {sawViews[i].Root.position=p;sawViews[i].Root.rotation=Quaternion.Euler(0,-time*720,0);}
                List<EnemyActor> candidates = targets;
                if (allTargetsIndexed)
                {
                    EnemyActor.GatherCandidatesUnsorted(p, hitRadiusThreshold, spatialCandidates);
                    candidates = spatialCandidates;
                }
                for(int e=0;e<candidates.Count;e++)
                {
                    var enemy=candidates[e];if(enemy==null||!enemy.IsAlive||enemy.CurrentHealth<=0||enemy.Session!=session)continue;
                    var delta=enemy.WorldCenter-p;delta.y=0;
                    float hitRadius = enemy.HitRadius + hitRadiusThreshold;
                    if (delta.sqrMagnitude > hitRadius * hitRadius) continue;
                    var sawHit=Deal(enemy,At(tuning.sawDamage,tier),p,DamageSourceType.UpgradeSaw);
                    if(sawHit.AppliedRealDamage)effects.Impact(enemy.WorldCenter,Amber);
                    if(enemy.IsAlive)enemy.ApplyKnockback(Flat(enemy.WorldCenter-player.WorldCenter)*tuning.sawPush);
                }
            }
        }
        private static readonly Color SlowStatusColor = new Color(.2f, .6f, .8f);
        private void StepStatusViews()
        {
            for (int i = 0; i < statuses.Length; i++)
            {
                var s = statuses[i];
                if (s.Actor != null && (!s.Actor.IsAlive || s.Actor.SpawnVersion != s.Version || (!s.Actor.IsControlLocked && s.Actor.SlowRemaining <= 0)))
                {
                    statusByActorId.Remove(s.Actor.TargetId);
                    effects.Release(s.View);
                    s.View = null;
                    s.Actor = null;
                    s.AppliedKind = 0;
                    s.AppliedScale = -1f;
                    s.HasPos = false;
                    activeStatusCount = Mathf.Max(0, activeStatusCount - 1);
                }
            }
            for (int e = 0; e < targets.Count; e++)
            {
                var enemy = targets[e];
                if (!enemy.IsControlLocked && enemy.SlowRemaining <= 0) continue;
                int id = enemy.TargetId;
                if (!statusByActorId.TryGetValue(id, out StatusView found) || found.Actor != enemy)
                {
                    if (activeStatusCount >= statuses.Length) continue;
                    found = null;
                    for (int i = 0; i < statuses.Length; i++)
                    {
                        if (statuses[i].Actor == null) { found = statuses[i]; break; }
                    }
                    if (found == null) continue;
                    found.Actor = enemy;
                    found.Version = enemy.SpawnVersion;
                    found.AppliedKind = 0;
                    found.AppliedScale = -1f;
                    found.HasPos = false;
                    activeStatusCount++;
                    statusByActorId[id] = found;
                }
                else
                {
                    found.Version = enemy.SpawnVersion;
                }
                found.View ??= effects.Acquire(UpgradeEffectPool.Shape.Ring, enemy.WorldCenter, Ice, enemy.HitRadius + .25f);
                if (found.View != null)
                {
                    Vector3 statusPos = enemy.WorldCenter + Vector3.down * .6f;
                    if (!found.HasPos || (found.AppliedPos - statusPos).sqrMagnitude >= 0.0001f)
                    {
                        found.AppliedPos = statusPos;
                        found.HasPos = true;
                        found.View.Root.position = statusPos;
                    }
                    byte kind = enemy.IsFrozen ? (byte)1 : enemy.StunRemaining > 0 ? (byte)2 : (byte)3;
                    if (found.AppliedKind != kind)
                    {
                        found.AppliedKind = kind;
                        effects.SetShape(found.View, kind == 1 ? UpgradeEffectPool.Shape.Ice : UpgradeEffectPool.Shape.Ring);
                        effects.SetColor(found.View, kind == 1 ? Ice : kind == 2 ? Amber : SlowStatusColor);
                    }
                    float scale = enemy.HitRadius + (kind == 1 ? .45f : .2f);
                    if (!Mathf.Approximately(found.AppliedScale, scale))
                    {
                        found.AppliedScale = scale;
                        found.View.Root.localScale = Vector3.one * scale;
                    }
                }
            }
        }
        private static Vector3 Flat(Vector3 direction) {direction.y=0;return direction.sqrMagnitude>.0001f?direction.normalized:Vector3.forward;}

        internal UpgradeRuntimeSnapshot CaptureSnapshot(IReadOnlyList<EnemyActor> enemyLookup)
        {
            var snapshot = new UpgradeRuntimeSnapshot
            {
                attackTiers = (int[])attackTiers.Clone(),
                dashStart = dashStart,
                wallSlamReadyAt = wallSlamReadyAt,
                trackingDash = trackingDash,
                hasReturnMarker = returnMarker != null,
                time = time,
                blizzardTimer = blizzardTimer,
                turretTimer = turretTimer,
                regenTimer = regenTimer,
                emergencyReady = emergencyReady,
                overheal = overheal,
                gems = gems,
                activeDecoys = activeDecoys,
                nextPlacementOrder = nextPlacementOrder,
                explosionsResolved = ExplosionsResolved,
                projectilesFired = ProjectilesFired,
                chainsTriggered = ChainsTriggered,
                reactiveReadyAt = reactiveReadyAt,
                batteryGems = batteryGems,
                puddleTimer = puddleTimer,
                discTimer = discTimer,
                boomerangTimer = boomerangTimer,
                orbitalTimer = orbitalTimer,
                parabolicTimer = parabolicTimer,
                whipTimer = whipTimer,
                auraTimer = auraTimer,
                lastTrailPos = lastTrailPos,
                trailInitialized = trailInitialized
            };
            if (projectiles != null)
            {
                for (int i = 0; i < projectiles.Length; i++)
                {
                    Projectile p = projectiles[i];
                    if (p == null || !p.Active) continue;
                    var item = new UpgradeProjectileSnapshot
                    {
                        piercing = p.Piercing,
                        position = p.Position,
                        direction = p.Direction,
                        damage = p.Damage,
                        speed = p.Speed,
                        life = p.Life,
                        freeze = p.Freeze,
                        radius = p.Radius,
                        sourceType = (int)p.Source,
                        damageType = (int)p.DamageType,
                        targetEnemyIndex = FindEnemyIndex(enemyLookup, p.Target, p.TargetVersion),
                        mode = (int)p.Mode,
                        floatData = p.FloatData,
                        boolFlag = p.BoolFlag,
                        elapsed = p.Elapsed,
                        duration = p.Duration,
                        startPosition = p.StartPosition,
                        endPosition = p.EndPosition
                    };
                    if (enemyLookup != null && p.Hits != null && p.Hits.Count > 0)
                    {
                        for (int e = 0; e < enemyLookup.Count; e++)
                        {
                            EnemyActor enemy = enemyLookup[e];
                            if (enemy != null && p.Hits.TryGetValue(enemy.TargetId, out int ver) && ver == enemy.SpawnVersion)
                                item.hitEnemyIndices.Add(e);
                        }
                    }
                    if (enemyLookup != null && p.HitCooldowns != null && p.HitCooldowns.Count > 0)
                    {
                        for (int e = 0; e < enemyLookup.Count; e++)
                        {
                            EnemyActor enemy = enemyLookup[e];
                            if (enemy != null && p.HitCooldowns.TryGetValue(enemy.TargetId, out float readyAt) && readyAt > time)
                            {
                                item.hitCooldownKeys.Add(e);
                                item.hitCooldownValues.Add(readyAt);
                            }
                        }
                    }
                    snapshot.projectiles.Add(item);
                }
            }
            if (devices != null)
            {
                for (int i = 0; i < devices.Length; i++)
                {
                    Device d = devices[i];
                    if (d == null || !d.Active) continue;
                    bool inf = float.IsPositiveInfinity(d.Life);
                    snapshot.devices.Add(new UpgradeDeviceSnapshot
                    {
                        fresh = d.Fresh,
                        kind = (int)d.Kind,
                        position = d.Position,
                        life = inf ? 0f : d.Life,
                        infiniteLife = inf,
                        damage = d.Damage,
                        shotTimer = d.ShotTimer,
                        burstTimer = d.BurstTimer,
                        placedAt = d.PlacedAt,
                        shots = d.Shots,
                        placementOrder = d.PlacementOrder
                    });
                }
            }
            if (cables != null)
            {
                for (int i = 0; i < cables.Length; i++)
                {
                    if (cables[i].ExpiresAt <= time) continue;
                    snapshot.cables.Add(new ElectricCableSnapshot
                    {
                        slotIndex = i,
                        start = cables[i].Start,
                        end = cables[i].End,
                        expiresAt = cables[i].ExpiresAt,
                        damage = cables[i].Damage
                    });
                }
            }
            if (blasts != null)
            {
                for (int i = 0; i < blastCount; i++)
                {
                    Blast b = blasts[i];
                    snapshot.blasts.Add(new UpgradeBlastSnapshot
                    {
                        position = b.Position,
                        damage = b.Damage,
                        radius = b.Radius,
                        delay = b.Delay,
                        stun = b.Stun,
                        push = b.Push,
                        freeze = b.Freeze,
                        implosion = b.Implosion,
                        echo = b.Echo,
                        shockwave = b.Shockwave,
                        sourceType = (int)b.Source,
                        damageType = (int)b.DamageType
                    });
                }
            }
            if (voltaicChains != null)
            {
                for (int i = 0; i < voltaicChains.Length; i++)
                {
                    VoltaicChain chain = voltaicChains[i];
                    if (chain == null || !chain.Active) continue;
                    var item = new VoltaicChainSnapshot
                    {
                        count = chain.Count,
                        next = chain.Next,
                        order = chain.Order,
                        previousEnemyIndex = FindEnemyIndex(enemyLookup, chain.PreviousActor, chain.PreviousVersion),
                        origin = chain.Origin,
                        previous = chain.Previous,
                        damage = chain.Damage,
                        radius = chain.Radius,
                        wait = chain.Wait,
                        hopDelay = chain.HopDelay
                    };
                    for (int t = 0; t < chain.Targets.Length && t < item.targetEnemyIndices.Length; t++)
                        item.targetEnemyIndices[t] = FindEnemyIndex(enemyLookup, chain.Targets[t], chain.Versions[t]);
                    snapshot.chains.Add(item);
                }
            }
            for (int i = 0; i < groundZones.Length; i++)
            {
                GroundZone z = groundZones[i];
                if (z == null || !z.Active) continue;
                snapshot.zones.Add(new GroundZoneSnapshot
                {
                    kind = (int)z.Kind,
                    position = z.Position,
                    radius = z.Radius,
                    damagePerTick = z.DamagePerTick,
                    tickInterval = z.TickInterval,
                    tickTimer = z.TickTimer,
                    life = z.Life,
                    slow = z.Slow,
                    vulnerability = z.Vulnerability,
                    sourceType = (int)z.Source,
                    damageType = (int)z.DamageType
                });
            }
            return snapshot;
        }

        internal void RestoreFromSnapshot(UpgradeRuntimeSnapshot snapshot, IReadOnlyList<EnemyActor> enemyLookup)
        {
            if (!bound) return;
            ResetForSession();
            if (snapshot == null) return;
            if (snapshot.attackTiers != null)
                System.Array.Copy(snapshot.attackTiers, attackTiers, Mathf.Min(attackTiers.Length, snapshot.attackTiers.Length));
            dashStart = snapshot.dashStart;
            wallSlamReadyAt = snapshot.wallSlamReadyAt;
            trackingDash = snapshot.trackingDash;
            time = Mathf.Max(0f, snapshot.time);
            blizzardTimer = snapshot.blizzardTimer;
            turretTimer = snapshot.turretTimer;
            regenTimer = snapshot.regenTimer;
            emergencyReady = snapshot.emergencyReady;
            overheal = Mathf.Max(0f, snapshot.overheal);
            gems = Mathf.Max(0, snapshot.gems);
            batteryGems = Mathf.Max(0, snapshot.batteryGems);
            reactiveReadyAt = snapshot.reactiveReadyAt;
            puddleTimer = snapshot.puddleTimer;
            discTimer = snapshot.discTimer;
            boomerangTimer = snapshot.boomerangTimer;
            orbitalTimer = snapshot.orbitalTimer;
            parabolicTimer = snapshot.parabolicTimer;
            whipTimer = snapshot.whipTimer;
            auraTimer = snapshot.auraTimer;
            lastTrailPos = snapshot.lastTrailPos;
            trailInitialized = snapshot.trailInitialized;
            activeDecoys = Mathf.Max(0, snapshot.activeDecoys);
            nextPlacementOrder = snapshot.nextPlacementOrder;
            ExplosionsResolved = Mathf.Max(0, snapshot.explosionsResolved);
            ProjectilesFired = Mathf.Max(0, snapshot.projectilesFired);
            ChainsTriggered = Mathf.Max(0, snapshot.chainsTriggered);

            if (snapshot.hasReturnMarker && effects != null)
                returnMarker = effects.Acquire(UpgradeEffectPool.Shape.Decoy, dashStart, Plasma, .9f);

            if (snapshot.zones != null)
            {
                for (int i = 0; i < snapshot.zones.Count && i < groundZones.Length; i++)
                {
                    GroundZoneSnapshot item = snapshot.zones[i];
                    GroundZone z = groundZones[i];
                    if (z == null) continue;
                    z.Active = true;
                    z.Kind = (GroundZoneKind)item.kind;
                    z.Position = item.position;
                    z.Radius = item.radius;
                    z.DamagePerTick = item.damagePerTick;
                    z.TickInterval = Mathf.Max(.05f, item.tickInterval);
                    z.TickTimer = item.tickTimer;
                    z.Life = item.life;
                    z.Slow = item.slow;
                    z.Vulnerability = item.vulnerability;
                    z.Source = (DamageSourceType)item.sourceType;
                    z.DamageType = (DamageType)item.damageType;
                    if (effects != null)
                    {
                        z.View = effects.Acquire(UpgradeEffectPool.Shape.Ring, z.Position, z.Kind == GroundZoneKind.FlameTrail ? FlameColor : Plasma, z.Radius);
                        if (z.View != null) effects.ProjectGroundRing(z.View, z.Position, z.Radius);
                    }
                }
            }

            if (projectiles != null && snapshot.projectiles != null)
            {
                int slot = 0;
                for (int i = 0; i < snapshot.projectiles.Count && slot < projectiles.Length; i++)
                {
                    UpgradeProjectileSnapshot item = snapshot.projectiles[i];
                    Projectile p = projectiles[slot++];
                    p.Active = true;
                    p.Piercing = item.piercing;
                    p.Mode = (ProjectileMode)item.mode;
                    p.FloatData = item.floatData;
                    p.BoolFlag = item.boolFlag;
                    p.Elapsed = item.elapsed;
                    p.Duration = item.duration;
                    p.StartPosition = item.startPosition;
                    p.EndPosition = item.endPosition;
                    p.Position = item.position;
                    p.Direction = Flat(item.direction);
                    p.Damage = item.damage;
                    p.Speed = item.speed;
                    p.Life = item.life;
                    p.Freeze = item.freeze;
                    p.Radius = item.radius;
                    p.Source = (DamageSourceType)item.sourceType;
                    p.DamageType = (DamageType)item.damageType;
                    EnemyActor target = item.targetEnemyIndex >= 0 && enemyLookup != null && item.targetEnemyIndex < enemyLookup.Count
                        ? enemyLookup[item.targetEnemyIndex]
                        : null;
                    p.Target = target;
                    p.TargetVersion = target != null ? target.SpawnVersion : 0;
                    p.Hits.Clear();
                    p.HitCooldowns.Clear();
                    if (enemyLookup != null && item.hitEnemyIndices != null)
                    {
                        for (int h = 0; h < item.hitEnemyIndices.Count; h++)
                        {
                            int idx = item.hitEnemyIndices[h];
                            if (idx >= 0 && idx < enemyLookup.Count && enemyLookup[idx] != null)
                                p.Hits[enemyLookup[idx].TargetId] = enemyLookup[idx].SpawnVersion;
                        }
                    }
                    if (enemyLookup != null && item.hitCooldownKeys != null && item.hitCooldownValues != null)
                    {
                        int pairs = Mathf.Min(item.hitCooldownKeys.Count, item.hitCooldownValues.Count);
                        for (int h = 0; h < pairs; h++)
                        {
                            int idx = item.hitCooldownKeys[h];
                            if (idx >= 0 && idx < enemyLookup.Count && enemyLookup[idx] != null)
                                p.HitCooldowns[enemyLookup[idx].TargetId] = item.hitCooldownValues[h];
                        }
                    }
                    if (effects != null)
                    {
                        var shape = p.Mode == ProjectileMode.BouncingDisc ? UpgradeEffectPool.Shape.Saw
                            : p.Mode == ProjectileMode.Parabolic ? UpgradeEffectPool.Shape.Mine
                            : p.Mode == ProjectileMode.Boomerang || p.Source == DamageSourceType.UpgradeBlade ? UpgradeEffectPool.Shape.Blade
                            : UpgradeEffectPool.Shape.Dart;
                        Color color = p.Freeze > 0 ? Ice : (p.DamageType == DamageType.Physical ? Amber : Plasma);
                        float scale = p.Mode == ProjectileMode.BouncingDisc ? .42f
                            : p.Mode == ProjectileMode.Parabolic ? .8f
                            : p.Mode == ProjectileMode.Boomerang ? 1f
                            : p.Source == DamageSourceType.UpgradeBlade ? 1.5f : .65f;
                        p.View = effects.Acquire(shape, p.Position, color, scale);
                        if (p.View != null && p.Direction.sqrMagnitude > 0.0001f)
                            p.View.Root.rotation = Quaternion.LookRotation(p.Direction);
                    }
                    ActiveProjectileCount++;
                }
            }

            if (devices != null && snapshot.devices != null)
            {
                int slot = 0;
                for (int i = 0; i < snapshot.devices.Count && slot < devices.Length; i++)
                {
                    UpgradeDeviceSnapshot item = snapshot.devices[i];
                    Device d = devices[slot++];
                    d.Active = true;
                    d.Fresh = item.fresh;
                    d.Kind = (DeviceKind)item.kind;
                    d.Position = item.position;
                    d.Life = item.infiniteLife ? float.PositiveInfinity : item.life;
                    d.Damage = item.damage;
                    d.ShotTimer = item.shotTimer;
                    d.BurstTimer = item.burstTimer;
                    d.PlacedAt = item.placedAt;
                    d.Shots = item.shots;
                    d.PlacementOrder = item.placementOrder;
                    if (effects != null)
                    {
                        var shape = d.Kind == DeviceKind.Mine ? UpgradeEffectPool.Shape.Mine
                            : d.Kind == DeviceKind.Decoy ? UpgradeEffectPool.Shape.Decoy : UpgradeEffectPool.Shape.Turret;
                        Vector3 visualPos = d.Position;
                        if (d.Kind != DeviceKind.Decoy)
                            visualPos.y = ArenaSurface.Height(d.Position) + (d.Kind == DeviceKind.Turret ? .025f : .15f);
                        d.View = effects.Acquire(shape, visualPos, d.Kind == DeviceKind.Decoy ? Plasma : Amber, d.Kind == DeviceKind.Mine ? .65f : 1f);
                    }
                    ActiveDeviceCount++;
                }
            }

            if (cables != null && snapshot.cables != null)
            {
                for (int i = 0; i < snapshot.cables.Count; i++)
                {
                    ElectricCableSnapshot item = snapshot.cables[i];
                    if (item.slotIndex < 0 || item.slotIndex >= cables.Length) continue;
                    cables[item.slotIndex] = new ElectricCable
                    {
                        Start = item.start,
                        End = item.end,
                        ExpiresAt = item.expiresAt,
                        Damage = item.damage
                    };
                    float remaining = item.expiresAt - time;
                    if (remaining > 0f && lightning != null)
                        lightning.ShowCable(item.slotIndex, item.start - Vector3.up * .45f, item.end - Vector3.up * .45f, remaining);
                }
            }

            if (blasts != null && snapshot.blasts != null)
            {
                blastCount = 0;
                for (int i = 0; i < snapshot.blasts.Count && blastCount < blasts.Length; i++)
                {
                    UpgradeBlastSnapshot item = snapshot.blasts[i];
                    blasts[blastCount++] = new Blast
                    {
                        Position = item.position,
                        Damage = item.damage,
                        Radius = item.radius,
                        Delay = item.delay,
                        Stun = item.stun,
                        Push = item.push,
                        Freeze = item.freeze,
                        Implosion = item.implosion,
                        Echo = item.echo,
                        Shockwave = item.shockwave,
                        Source = (DamageSourceType)item.sourceType,
                        DamageType = (DamageType)item.damageType
                    };
                }
            }

            if (voltaicChains != null && snapshot.chains != null)
            {
                int slot = 0;
                for (int i = 0; i < snapshot.chains.Count && slot < voltaicChains.Length; i++)
                {
                    VoltaicChainSnapshot item = snapshot.chains[i];
                    VoltaicChain chain = voltaicChains[slot++];
                    chain.Active = true;
                    chain.Count = Mathf.Clamp(item.count, 0, chain.Targets.Length);
                    chain.Next = Mathf.Clamp(item.next, 0, chain.Count);
                    chain.Order = item.order;
                    EnemyActor prevActor = item.previousEnemyIndex >= 0 && enemyLookup != null && item.previousEnemyIndex < enemyLookup.Count
                        ? enemyLookup[item.previousEnemyIndex] : null;
                    chain.PreviousActor = prevActor;
                    chain.PreviousVersion = prevActor != null ? prevActor.SpawnVersion : 0;
                    chain.Origin = item.origin;
                    chain.Previous = item.previous;
                    chain.Damage = item.damage;
                    chain.Radius = item.radius;
                    chain.Wait = item.wait;
                    chain.HopDelay = item.hopDelay;
                    for (int t = 0; t < chain.Targets.Length; t++)
                    {
                        int idx = item.targetEnemyIndices != null && t < item.targetEnemyIndices.Length ? item.targetEnemyIndices[t] : -1;
                        EnemyActor actor = idx >= 0 && enemyLookup != null && idx < enemyLookup.Count ? enemyLookup[idx] : null;
                        chain.Targets[t] = actor;
                        chain.Versions[t] = actor != null ? actor.SpawnVersion : 0;
                    }
                    ActiveChainCount++;
                }
            }
            StepStatusViews();
        }

        private static int FindEnemyIndex(IReadOnlyList<EnemyActor> enemyLookup, EnemyActor actor, int version)
        {
            if (enemyLookup == null || actor == null) return -1;
            for (int i = 0; i < enemyLookup.Count; i++)
                if (enemyLookup[i] == actor && enemyLookup[i].SpawnVersion == version) return i;
            return -1;
        }
    }
}
