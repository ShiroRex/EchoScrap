using System.Collections.Generic;
using EchoScrap.Core;
using EchoScrap.Player;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Run-scoped upgrade simulation. Events normally enqueue attacks for the next step;
    /// a saturated logical chain reserve finishes its oldest attack before reusing the slot.</summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeCombatRuntime : MonoBehaviour
    {
        private sealed class Projectile
        {
            public bool Active, Piercing;
            public Vector3 Position, Direction;
            public float Damage, Speed, Life, Freeze, Radius;
            public DamageSourceType Source;
            public DamageType DamageType;
            public EnemyActor Target;
            public int TargetVersion;
            public readonly Dictionary<int,int> Hits = new Dictionary<int,int>(32);
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
        }
        private sealed class VoltaicChain
        {
            public bool Active;
            public readonly EnemyActor[] Targets = new EnemyActor[4];
            public readonly int[] Versions = new int[4];
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
        private readonly int[] attackTiers = new int[UpgradeRules.Count];
        private readonly List<EnemyActor> targets = new List<EnemyActor>(512);
        private readonly HashSet<int> selectedTargets = new HashSet<int>();
        private readonly UpgradeEffectPool.View[] sawViews = new UpgradeEffectPool.View[4];
        private UpgradeEffectPool.View shield;
        private UpgradeEffectPool.View returnMarker;
        private PlayerMotor motor;
        private Vector3 dashStart;
        private float wallSlamReadyAt;
        private bool trackingDash;
        private readonly ElectricCable[] cables = new ElectricCable[MaxElectricCables];
        private readonly List<EnemyActor> segmentTargets = new List<EnemyActor>(64);
        private readonly RaycastHit[] ramSweepHits = new RaycastHit[32];
        private float time, blizzardTimer, turretTimer, regenTimer, emergencyReady, overheal;
        private int gems, activeDecoys;
        private ulong nextPlacementOrder;
        private bool bound, collecting;
        private static readonly Color Amber = new Color(1f,.66f,.22f);
        private static readonly Color Plasma = new Color(.35f,.94f,1f);
        private static readonly Color Ice = new Color(.55f,.84f,1f);
        private static readonly Color Repair = new Color(.35f,1f,.55f);
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
            blasts=new Blast[Mathf.Max(64,tuning.blastCapacity)];
            statuses=new StatusView[Mathf.Max(32,tuning.statusViews)];
            for(int i=0;i<statuses.Length;i++) statuses[i]=new StatusView();
            effects=new UpgradeEffectPool(transform,tuning.projectileViews+tuning.deviceCapacity+tuning.transientViews+tuning.statusViews+8,collectorSprite,session.Effects);
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
            foreach(var p in projectiles) {p.Active=false;p.View=null;p.Hits.Clear();p.Target=null;}
            foreach(var d in devices) {d.Active=false;d.View=null;}
            foreach(var s in statuses) {s.Actor=null;s.View=null;}
            for(int i=0;i<4;i++) sawViews[i]=null;
            shield=null; blastCount=0; time=blizzardTimer=turretTimer=regenTimer=emergencyReady=overheal=0f;
            nextPlacementOrder = 0;
            gems=activeDecoys=0; collecting=false; targets.Clear(); selectedTargets.Clear();
            ActiveProjectileCount=ActiveDeviceCount=ExplosionsResolved=ProjectilesFired=0;
            ChainsTriggered=ChainHitsResolved=ChainTargetsInvalidated=ChainOverflowResolutions=0;
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
            if(!bound)return;
            if(hit.IsPlayerTarget&&hit.WasImmune&&shield!=null)effects.PulseShield(shield,hit.HitDirection);
            if(!hit.AppliedRealDamage) return;
            if(hit.IsPlayerTarget)
            {
                if(hit.RemainingHealth>0 && Tier(UpgradeId.BIO_03)>0 && hit.RemainingHealth/hit.MaxHealth<tuning.emergencyThreshold && time>=emergencyReady)
                {player.GrantUpgradeProtection(tuning.emergencyInvulnerability);emergencyReady=time+tuning.emergencyCooldown;effects.Burst(hit.WorldPosition,2,Repair);}
                return;
            }
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
                if(!wasFrozen && target.IsFrozen) effects.Burst(target.WorldCenter,1.2f,Ice,kind:UpgradeEffectPool.BurstKind.Frost);
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
            int desired=Mathf.Clamp(Mathf.RoundToInt(At(tuning.prismCounts,tier)),0,chain.Targets.Length);
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
            trackingDash = false;
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
                        DamageSourceType.DashReturn, dash.Direction);
                }
                return;
            }
            if(dash.Started)
            {
                trackingDash = CanResolveContextualUpgrade;
                dashStart = dash.Position;
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
                    int ram = Tier(UpgradeId.DASH_05);
                    if (ram > 0 && length > .05f)
                        DamageDashSegment(dashStart, dash.Position, tuning.ramRadius, At(tuning.ramDamage, ram),
                            DamageSourceType.DashRam, dash.Direction);
                    int cable = Tier(UpgradeId.DASH_07);
                    if (cable > 0 && length > tuning.cableWidth &&
                        !Physics.Linecast(dashStart, dash.Position, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore))
                        PlaceElectricCable(dashStart, dash.Position, cable);
                }
                trackingDash = false;
                int shock=Tier(UpgradeId.DASH_02);
                if(shock>0) QueueBlast(dash.Position,At(tuning.shockDamage,shock),At(tuning.shockRadius,shock),stun:At(tuning.shockStun,shock),push:tuning.shockPushDistance,shockwave:true);
                if(Tier(UpgradeId.DASH_04)>0) Fire(dash.Position,dash.Direction,tuning.bladeDamage,tuning.bladeSpeed,tuning.bladeLifetime,DamageSourceType.UpgradeBlade,null,true,0,tuning.bladeHitRadius);
            }
        }
        private void DamageDashSegment(Vector3 from, Vector3 to, float width, float damage, DamageSourceType source,
            Vector3 dashDirection)
        {
            bool ram = source == DamageSourceType.DashRam;
            float length = Vector3.ProjectOnPlane(to - from, Vector3.up).magnitude;
            EnemyActor.GatherSegmentCandidates(from, to, width, segmentTargets);
            foreach (EnemyActor enemy in segmentTargets)
            {
                if (!CanResolveContextualUpgrade) break;
                if (enemy == null || !enemy.IsAlive || enemy.Session != session ||
                    enemy is GolgoraActor { IsAirborne: true } || (ram && !enemy.CanBeRamPushed) ||
                    !EnemyActor.TouchesSegment(from, to, enemy.WorldCenter, width + enemy.HitRadius, out float fraction))
                    continue;

                Vector3 point = Vector3.Lerp(from, to, fraction);
                if (Physics.Linecast(point, enemy.WorldCenter, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore))
                    continue;

                Vector3 impact = enemy.WorldCenter;
                if (ram)
                {
                    impact = enemy.CarryAlongDash(dashDirection, length * (1f - fraction), ramSweepHits);
                    enemy.LaunchFromDash(dashDirection, tuning.ramLaunchSpeed);
                }
                if (Deal(enemy, damage, point, source, canCrit: false).AppliedRealDamage)
                    effects.Impact(impact, ram ? Amber : Plasma);
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
                EnemyActor.GatherSegmentCandidates(cable.Start, cable.End, tuning.cableWidth, segmentTargets);
                foreach (EnemyActor enemy in segmentTargets)
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
            if(pickup.Kind!=CombatPickupKind.Experience || Tier(UpgradeId.SCRAP_02)==0) return;
            gems+=pickup.GemCount;
            while(gems>=tuning.assemblerGemCount)
            {
                gems-=tuning.assemblerGemCount; var target=Nearest(player.WorldCenter,1000);
                if(target!=null) Fire(player.WorldCenter,target.WorldCenter-player.WorldCenter,At(tuning.assemblerDamage,Tier(UpgradeId.SCRAP_02)),tuning.plasmaSpeed,10,DamageSourceType.UpgradeProjectile,target,damageType:DamageType.Physical);
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
        private void Fire(Vector3 position,Vector3 direction,float damage,float speed,float life,DamageSourceType source,
            EnemyActor target=null,bool piercing=false,float freeze=0,float radius=.25f,EnemyActor ignore=null,DamageType damageType=DamageType.Inherit)
        {
            for(int i=0;i<projectiles.Length;i++)
            {
                var p=projectiles[i];if(p.Active) continue;
                p.Active=true;p.Position=position;p.Direction=Flat(direction);p.Damage=damage;p.Speed=speed;p.Life=life;p.Source=source;p.DamageType=DamageTypes.Resolve(source,damageType);
                p.Target=target;p.TargetVersion=target!=null?target.SpawnVersion:0;p.Piercing=piercing;p.Freeze=freeze;p.Radius=radius;p.Hits.Clear();
                if(ignore!=null)p.Hits[ignore.TargetId]=ignore.SpawnVersion;
                p.View=effects.Acquire(source==DamageSourceType.UpgradeBlade?UpgradeEffectPool.Shape.Blade:UpgradeEffectPool.Shape.Dart,position,freeze>0?Ice:p.DamageType==DamageType.Physical?Amber:Plasma,source==DamageSourceType.UpgradeBlade?1.5f:.65f);
                ActiveProjectileCount++;ProjectilesFired++;return;
            }
        }
        private void QueueBlast(Vector3 p,float damage,float radius,float delay=0,float stun=0,float push=0,float freeze=0, bool echo=false,bool implosion=false,DamageSourceType source=DamageSourceType.UpgradeExplosion,DamageType damageType=DamageType.Inherit,bool shockwave=false)
        {
            if(blastCount>=blasts.Length) return;
            bool explosion=source==DamageSourceType.UpgradeExplosion;
            if(explosion&&!echo) {damage*=stats.ExplosionDamageMultiplier;radius*=stats.ExplosionRadiusMultiplier;}
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
            time+=dt; effects.Step(dt);lightning.Step(dt);
            stats.StepCombatMedkit(dt);
            if (returnMarker != null && (motor == null || !motor.HasReturnAnchor))
            {
                effects.Release(returnMarker);
                returnMarker = null;
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
                    var target=Nearest(d.Position,tuning.turretRange);
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
                    var hit=Deal(enemy,p.Damage,from,p.Source,p.DamageType);
                    if(p.Freeze>0&&hit.AppliedRealDamage&&enemy.IsAlive&&enemy.CurrentHealth>0)enemy.ApplyFreeze(p.Freeze);
                    if(hit.AppliedRealDamage)
                    {
                        if(p.Freeze<=0&&p.DamageType==DamageType.Supernatural)effects.Burst(enemy.WorldCenter,.45f,Plasma,kind:UpgradeEffectPool.BurstKind.PlasmaImpact);
                        else effects.Impact(enemy.WorldCenter,p.Freeze>0?Ice:Amber,p.Freeze>0);
                    }
                    if(!p.Piercing) {p.Life=0;break;}
                }
                if(p.Life<=0||blocked) {p.Active=false;effects.Release(p.View);p.View=null;p.Target=null;ActiveProjectileCount--;}
            }
        }
        private DamageResult Deal(EnemyActor enemy,float damage,Vector3 origin,DamageSourceType source,DamageType damageType=DamageType.Inherit,bool canCrit=true)
        {
            var request=new DamageRequest(player.TargetId,source,origin,Flat(enemy.WorldCenter-origin),damage,canCrit,stats.CritChance,stats.CritMultiplier,damageType:damageType);
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
                effects.Burst(b.Position,b.Radius,b.Freeze>0?Ice:b.DamageType==DamageType.Supernatural?Plasma:Amber,b.Implosion,b.Freeze>0?UpgradeEffectPool.BurstKind.Frost:b.Implosion?UpgradeEffectPool.BurstKind.Implosion:b.Shockwave||b.Damage<=0?UpgradeEffectPool.BurstKind.Shock:b.DamageType==DamageType.Supernatural?UpgradeEffectPool.BurstKind.PlasmaExplosion:UpgradeEffectPool.BurstKind.Explosion);
                Gather();
                for(int e=0;e<targets.Count;e++)
                {
                    var enemy=targets[e];if(!enemy.IsAlive||enemy.CurrentHealth<=0)continue;
                    var delta=enemy.WorldCenter-b.Position;delta.y=0;
                    if(delta.sqrMagnitude>(b.Radius+enemy.HitRadius)*(b.Radius+enemy.HitRadius))continue;
                    if(b.Damage>0 && !Deal(enemy,b.Damage,b.Position,b.Source,b.DamageType,canCrit:b.Source!=DamageSourceType.WallSlam).AppliedRealDamage) continue;
                    if(!enemy.IsAlive||enemy.CurrentHealth<=0)continue;
                    if(b.Freeze>0)enemy.ApplyFreeze(b.Freeze);
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
                    float hitRadius = enemy.HitRadius + tuning.sawHitRadius;
                    if (delta.sqrMagnitude > hitRadius * hitRadius) continue;
                    var sawHit=Deal(enemy,At(tuning.sawDamage,tier),p,DamageSourceType.UpgradeSaw);
                    if(sawHit.AppliedRealDamage)effects.Impact(enemy.WorldCenter,Amber);
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
