using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EchoScrap.Tests
{
    public sealed class CompleteUpgradeTests
    {
        private Scene scene;
        private CombatSession session;
        private PlayerRuntimeStats stats;
        private PlayerHealth player;
        private FlashlightWeapon weapon;
        private CombatProgression xp;
        private CombatPickupPool pickups;
        private UpgradeCombatRuntime upgrades;
        private UpgradeTuning tuning;
        private UpgradeDraftController draft;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale=1; CombatTargetRegistry.Clear();
            scene=SceneManager.CreateScene("CompleteUpgrades_"+System.Guid.NewGuid().ToString("N"));SceneManager.SetActiveScene(scene);
            session=new GameObject("Session").AddComponent<CombatSession>();
            var go=new GameObject("Player");go.layer=PhysicsLayers.PlayerLayer;go.transform.position=Vector3.up;
            go.AddComponent<CapsuleCollider>();
            stats=go.AddComponent<PlayerRuntimeStats>();
            tuning=ScriptableObject.CreateInstance<UpgradeTuning>();
            typeof(PlayerRuntimeStats).GetField("upgradeTuning",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(stats,tuning);
            player=go.AddComponent<PlayerHealth>();player.BindSession(session);session.ResolvePlayerReferences();
            weapon=go.AddComponent<FlashlightWeapon>();weapon.BindSession(session);weapon.enabled=false;
            xp=new GameObject("XP").AddComponent<CombatProgression>();xp.BindSession(session);
            pickups=new GameObject("Pickups").AddComponent<CombatPickupPool>();pickups.AutoTick=false;pickups.BindReferences(session,player,stats,xp);
            upgrades=new GameObject("Upgrades").AddComponent<UpgradeCombatRuntime>();upgrades.AutoTick=false;upgrades.Bind(session,stats,player,weapon,pickups,xp);
            draft=new GameObject("Draft").AddComponent<UpgradeDraftController>();draft.BindReferences(session,stats,player,xp);
            session.ResetSession(4921);
            yield return null;
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale=1;CombatTargetRegistry.Clear();
            if(SceneManager.sceneCount<=1) SceneManager.SetActiveScene(SceneManager.CreateScene("UpgradeCleanup_"+System.Guid.NewGuid().ToString("N")));
            yield return SceneManager.UnloadSceneAsync(scene);Object.Destroy(tuning);
        }
        private void Set(string id,int tier=1) {stats.SetUpgradeTier(id,tier);weapon.ResetWeaponCycle();}
        private EnemyActor Enemy(float x=0,float z=3,float hp=1000,EnemyArchetypeId kind=EnemyArchetypeId.Residual)
        {
            var data=EnemyArchetypeData.CreateDefault(kind);data.MaxHealth=hp;data.ContactDamage=0;data.SpeedVarianceFraction=0;
            var go=new GameObject("Target");var enemy=go.AddComponent<EnemyActor>();
            enemy.InitializeOrResetFromPool(data,session,player,new Vector3(x,1,z));return enemy;
        }
        private DamageResult Hit(EnemyActor e,float damage=1,int pulse=1,bool crit=false)
        {
            var hit=new DamageRequest(player.TargetId,DamageSourceType.FlashlightPulse,player.WorldCenter,Vector3.forward,damage,crit,crit?1:0,stats.CritMultiplier,1,pulse);
            return e.ReceiveDamage(in hit);
        }
        private void Dash(bool started,bool ended,Vector3 position)
        {
            var dash=new DashStateEvent(started,ended,false,started,!started,position,Vector3.forward,0,stats.DashCooldown);session.Events.PublishDash(in dash);
        }
        [Test]
        public void NewPassiveStatsAndTierLimitsAreAppliedAndReset()
        {
            Set("SCRAP_01",99);Set("CRIT_01",99);Set("CRIT_02",99);Set("CRIT_05");Set("GREED_02",5);Set("DEMO_01",5);Set("DASH_04");
            Assert.That(stats.MaxHealth,Is.EqualTo(150));Assert.That(stats.Armor,Is.EqualTo(.2f).Within(.0001));
            Assert.That(stats.CritChance,Is.EqualTo(.6f).Within(.0001));Assert.That(stats.CritMultiplier,Is.EqualTo(2.75f));
            Assert.That(stats.ExperienceMultiplier,Is.EqualTo(1.75f));Assert.That(stats.ExplosionDamageMultiplier,Is.EqualTo(1.6f));Assert.That(stats.ExplosionRadiusMultiplier,Is.EqualTo(1.75f));
            Assert.That(stats.DashCooldown,Is.EqualTo(.5f));
            session.ResetSession(4921);Assert.That(stats.MaxHealth,Is.EqualTo(100));Assert.That(stats.CritChance,Is.Zero);Assert.That(stats.Armor,Is.Zero);
        }
        [TestCase(1,.12f,4,1.2f)] [TestCase(3,.28f,3,1.8f)] [TestCase(5,.44f,3,2.4f)]
        public void ColdSlowsThenFreezesAndFrozenTargetsTakeBonusDamage(int slowTier,float slow,int hits,float duration)
        {
            Set("CRYO_01",slowTier);Set("CRYO_02",slowTier==1?1:slowTier==3?2:3);var e=Enemy();
            for(int i=0;i<hits;i++)Hit(e);
            Assert.That(e.SlowFraction,Is.EqualTo(slow).Within(.0001));Assert.That(e.FrozenRemaining,Is.EqualTo(duration).Within(.0001));
            Assert.That(Hit(e,10).HealthLost,Is.EqualTo(13).Within(.001));
            upgrades.Step(.1f);Assert.That(upgrades.ActiveVisualCount,Is.GreaterThan(0));
            session.SetManualPause(true);upgrades.Step(10);Assert.That(e.FrozenRemaining,Is.GreaterThan(0));
            session.SetManualPause(false);upgrades.Step(3);Assert.That(e.IsFrozen,Is.False);
            e.ApplyFreeze(2);e.ResetForSpawn(new Vector3(0,1,3));Assert.That(e.IsFrozen,Is.False);Assert.That(e.SlowFraction,Is.Zero);
        }
        [TestCase(1,2,.4f)] [TestCase(2,3,.6f)] [TestCase(3,4,.8f)]
        public void FourthPulsePrismLinksDistinctEnemiesInSequence(int tier,int count,float fraction)
        {
            Set("FLASH_03",tier);var origin=Enemy(0,3);var neighbors=new EnemyActor[count];
            for(int i=0;i<count;i++)neighbors[i]=Enemy(i%2==0?1:-1,3+i*.15f);
            Hit(origin,10,4);Assert.That(upgrades.ChainsTriggered,Is.EqualTo(1));
            for(int i=0;i<20;i++)upgrades.Step(.02f);
            float damage=0;foreach(var neighbor in neighbors)damage+=1000-neighbor.CurrentHealth;
            Assert.That(damage,Is.EqualTo(count*10*fraction).Within(.01));
            Assert.That(upgrades.ChainHitsResolved,Is.EqualTo(count),"A chain hits each selected enemy once.");
            Assert.That(upgrades.ProjectilesFired,Is.Zero,"Electrical links do not create guided bullets.");
            Assert.That(upgrades.ChainsTriggered,Is.EqualTo(1),"Derived links must not create another chain.");
        }
        [Test]
        public void SiphonRepairsAndOverhealingPowersNova()
        {
            tuning.siphonChance=Vector3.one;Set("BIO_02",3);Set("BIO_04");var e=Enemy();
            player.ForceSetHealthForTesting(90);Hit(e,1,4);Assert.That(player.CurrentHealth,Is.EqualTo(94));
            player.Heal(21);Assert.That(upgrades.OverhealCharge,Is.Zero);upgrades.Step(.01f);
            Assert.That(e.CurrentHealth,Is.EqualTo(919));Assert.That(upgrades.ExplosionsResolved,Is.EqualTo(1));
        }
        [Test]
        public void NanobotsRegenerateAndEmergencyProtectionHasRunCooldown()
        {
            Set("BIO_03",3);player.ForceSetHealthForTesting(50);upgrades.Step(2.5f);Assert.That(player.CurrentHealth,Is.EqualTo(53));
            player.ForceSetHealthForTesting(21);player.TakeContactOrHazardDamage(5,Vector3.forward);
            Assert.That(player.UpgradeProtectionRemaining,Is.EqualTo(2));Assert.That(upgrades.EmergencyCooldownRemaining,Is.EqualTo(60));
            Assert.That(player.TakeContactOrHazardDamage(50,Vector3.forward).WasImmune,Is.True);
            player.StepTimers(3);upgrades.Step(3);player.TakeContactOrHazardDamage(1,Vector3.forward);Assert.That(player.UpgradeProtectionRemaining,Is.Zero);
        }
        [Test]
        public void DashEmitsShockDecoyOriginExplosionAndPiercingBlade()
        {
            Set("DASH_02",2);Set("DASH_03",2);Set("DASH_04");var e=Enemy(0,2);
            Dash(true,false,Vector3.up);Assert.That(upgrades.ActiveDeviceCount,Is.EqualTo(1));
            Assert.That(upgrades.PursuitGoal(e.WorldCenter,new Vector3(9,1,9)),Is.EqualTo(Vector3.up));
            upgrades.Step(.01f);Assert.That(e.CurrentHealth,Is.EqualTo(880));
            Dash(false,true,Vector3.up);upgrades.Step(.01f);Assert.That(e.StunRemaining,Is.GreaterThan(0));Assert.That(e.KnockbackAppliedCount,Is.GreaterThan(0));
            Assert.That(upgrades.ProjectilesFired,Is.EqualTo(1));upgrades.Step(3);
            Assert.That(upgrades.ActiveDeviceCount,Is.Zero);Assert.That(upgrades.ExplosionsResolved,Is.EqualTo(3));
        }
        [Test]
        public void ChainExplosionAndThermobaricEchoResolveOnceWithoutRecursion()
        {
            tuning.chainChance=Vector3.one;Set("DEMO_02");Set("DEMO_04");Set("DEMO_01",1);
            var victim=Enemy(0,3,1);var survivor=Enemy(1,3);Hit(victim,2);
            upgrades.Step(.01f);Assert.That(upgrades.ExplosionsResolved,Is.EqualTo(1));
            Assert.That(survivor.CurrentHealth,Is.EqualTo(1000-35*1.12f).Within(.01));
            upgrades.Step(.39f);Assert.That(upgrades.ExplosionsResolved,Is.EqualTo(1));
            upgrades.Step(.02f);Assert.That(upgrades.ExplosionsResolved,Is.EqualTo(2));
            Assert.That(survivor.CurrentHealth,Is.EqualTo(1000-35*1.12f*1.75f).Within(.01));
            upgrades.Step(2);Assert.That(upgrades.ExplosionsResolved,Is.EqualTo(2));
        }
        [Test]
        public void MinesTriggerOnContactAndTurretsFireThreeShotBursts()
        {
            Set("DEMO_03",3);upgrades.Step(2.5f);Assert.That(upgrades.ActiveDeviceCount,Is.EqualTo(1));
            var e=Enemy(0,1);upgrades.Step(.01f);Assert.That(upgrades.ActiveDeviceCount,Is.Zero);Assert.That(e.CurrentHealth,Is.EqualTo(840));
            stats.SetUpgradeTier("DEMO_03",0);Set("SCRAP_04");upgrades.Step(10);Assert.That(upgrades.ActiveDeviceCount,Is.EqualTo(1));
            int fired=upgrades.ProjectilesFired;for(int i=0;i<9;i++)upgrades.Step(.02f);
            Assert.That(upgrades.ProjectilesFired-fired,Is.EqualTo(2),"The first shot fired on turret placement; two more complete the burst.");
            Assert.That(e.CurrentHealth,Is.LessThan(840));
        }
        [Test]
        public void AssemblerCountsOriginalMergedGemsAndXpKeepsFractionalBonus()
        {
            Set("SCRAP_02",2);Set("GREED_02",1);Enemy();
            for(int i=0;i<15;i++)pickups.SpawnExperience(new Vector3(5,1,5),1);
            Assert.That(pickups.ActiveExperienceCount,Is.EqualTo(1));pickups.CollectAll();
            Assert.That(upgrades.ProjectilesFired,Is.EqualTo(1));Assert.That(upgrades.AssemblerGemRemainder,Is.Zero);Assert.That(xp.TotalXp,Is.EqualTo(17));
            pickups.SpawnExperience(new Vector3(5,1,5),5);pickups.CollectAll();Assert.That(xp.TotalXp,Is.EqualTo(23));
        }
        [Test]
        public void ChoosingCollectorStartsRealFlightsAndAwardsExperienceOnArrival()
        {
            xp.AddExperience(100);
            pickups.SpawnExperience(new Vector3(40, 1, 40), 260);
            player.ForceSetHealthForTesting(50);
            pickups.SpawnMedkit(new Vector3(-40, 1, -40));
            UpgradeCatalog.TryGet("GREED_05", out var collector);
            var offers = (System.Collections.Generic.List<UpgradeOffer>)typeof(UpgradeDraftController)
                .GetField("offers", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(draft);
            offers.Clear(); offers.Add(new UpgradeOffer(collector, stats));
            Assert.That(draft.TryChoose(0), Is.True);
            Assert.That(pickups.GlobalFlightCount, Is.EqualTo(2));
            Assert.That(xp.TotalXp, Is.EqualTo(100), "XP is not awarded before the gems arrive.");
            Assert.That(draft.IsOpen, Is.False);
            pickups.Step(2f);
            Assert.That(xp.TotalXp, Is.EqualTo(360));
            Assert.That(xp.PendingChoices, Is.EqualTo(1));
            Assert.That(draft.IsOpen, Is.True);
            Assert.That(session.IsPaused, Is.True);
            xp.ConsumePendingChoice(); pickups.Step(2f);
            Assert.That(player.CurrentHealth, Is.EqualTo(65));
            Assert.That(pickups.ActivePickupCount, Is.Zero);
            Assert.That(stats.GetUpgradeTier("GREED_05"), Is.EqualTo(1));
        }
        [Test]
        public void GlobalCollectorPreservesNestedLevelRewardsAndTacticalPause()
        {
            Set("GREED_05");pickups.SpawnExperience(new Vector3(40,1,40),260);player.ForceSetHealthForTesting(50);pickups.SpawnMedkit(new Vector3(-40,1,-40));
            int gained=xp.AddExperience(100);
            Assert.That(gained,Is.EqualTo(1));Assert.That(xp.TotalXp,Is.EqualTo(100));Assert.That(xp.PendingChoices,Is.EqualTo(1));
            Assert.That(pickups.GlobalFlightCount, Is.EqualTo(2));
            pickups.Step(2f); Assert.That(xp.TotalXp, Is.EqualTo(100), "Tactical pause also freezes the incoming pickups.");
            xp.ConsumePendingChoice(); pickups.Step(2f);
            Assert.That(xp.TotalXp, Is.EqualTo(360)); Assert.That(xp.PendingChoices, Is.EqualTo(1));
            Assert.That(draft.IsOpen,Is.True);Assert.That(session.IsPaused,Is.True);
            xp.ConsumePendingChoice(); pickups.Step(2f);
            Assert.That(pickups.ActivePickupCount,Is.Zero);Assert.That(player.CurrentHealth,Is.EqualTo(65));
        }
        [Test]
        public void ExecutionExcludesBossAndOjoAmplifiesLowHealthCritsAndStuns()
        {
            Set("CRIT_03",3);var ordinary=Enemy(0,3,100);Assert.That(Hit(ordinary,91).CausedDeath,Is.True);
            var boss=Enemy(0,6,100,EnemyArchetypeId.GolgoraBoss);Hit(boss,81);Assert.That(boss.IsAlive,Is.True);Assert.That(boss.CurrentHealth,Is.EqualTo(19));
            Set("CRIT_05");var e=Enemy(1,3,100);Hit(e,70);var critical=Hit(e,1,1,true);
            Assert.That(critical.ResolvedDamage,Is.EqualTo(stats.CritMultiplier*3).Within(.0001));
            upgrades.Step(.01f);Assert.That(e.StunRemaining,Is.EqualTo(1));
        }
        [TestCase(1, .06f)] [TestCase(2, .08f)] [TestCase(3, .1f)]
        public void ExecutionUsesNerfedThresholdAndReportsOnlyRealExecutionKills(int tier, float threshold)
        {
            Set("CRIT_03", tier);
            Assert.That(UpgradeTuning.At(tuning.executionThreshold, tier), Is.EqualTo(threshold));
            var atBoundary = Enemy(0, 3, 100);
            var boundary = Hit(atBoundary, 100f - threshold * 100f);
            Assert.That(boundary.WasExecuted, Is.False);
            Assert.That(atBoundary.IsAlive, Is.True);
            var execute = Hit(atBoundary, 1);
            Assert.That(execute.CausedDeath, Is.True);
            Assert.That(execute.WasExecuted, Is.True);
            Assert.That(execute.DamageType, Is.EqualTo(DamageType.Supernatural));
            var overkill = Hit(Enemy(2, 3, 100), 200);
            Assert.That(overkill.CausedDeath, Is.True);
            Assert.That(overkill.WasExecuted, Is.False, "Normal lethal damage is not an execution proc.");
            var physical = new DamageRequest(player.TargetId, DamageSourceType.UpgradeProjectile, Vector3.up, Vector3.forward,
                1, damageType: DamageType.Physical);
            var low = Enemy(3, 4, 100); Hit(low, 100f - threshold * 100f);
            var physicalExecution = low.ReceiveDamage(in physical);
            Assert.That(physicalExecution.WasExecuted, Is.True);
            Assert.That(physicalExecution.DamageType, Is.EqualTo(DamageType.Physical));
        }

        [Test]
        public void FourthPulseCriticalQueuesNextBurstAndDoesNotAlterCurrentBurst()
        {
            Set("CRIT_04",2);var e=Enemy();
            var pulse=new PulseResolvedEvent(1,4,false,1,0,1,Vector3.up,Vector3.forward,7,60);session.Events.PublishPulse(in pulse);
            Assert.That((int)typeof(FlashlightWeapon).GetField("guaranteedPending",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(weapon),Is.EqualTo(3));
            Assert.That((int)typeof(FlashlightWeapon).GetField("guaranteedActive",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(weapon),Is.Zero);
        }
        [Test]
        public void GuaranteedCriticalPulsesApplyToActualNextBurstHits()
        {
            Set("CRIT_04",2);Enemy();Physics.SyncTransforms();weapon.UpdateAim(.1f);
            var pulse=new PulseResolvedEvent(1,4,false,1,0,1,Vector3.up,Vector3.forward,7,60);session.Events.PublishPulse(in pulse);
            var hits=new System.Collections.Generic.List<DamageResult>();
            session.Events.OnDamageResolved+=hit=>{if(hit.BurstId==2&&!hit.IsPlayerTarget&&hit.AppliedRealDamage)hits.Add(hit);};
            weapon.StepWeapon(.65f);weapon.StepWeapon(.36f);weapon.StepWeapon(1f);
            Assert.That(hits.Count,Is.EqualTo(4));
            for(int i=0;i<3;i++)Assert.That(hits[i].IsCritical,Is.True);
            Assert.That(hits[3].IsCritical,Is.False);
        }
        [Test]
        public void BossIntroRejectsFreezingAndDecoyDoesNotExtendStompDamageToDistantPlayer()
        {
            var boss=new GameObject("Boss").AddComponent<GolgoraActor>();
            boss.InitializeOrResetFromPool(EnemyArchetypeData.CreateDefault(EnemyArchetypeId.GolgoraBoss),session,player,new Vector3(0,1,3));
            Set("CRYO_03",3);upgrades.Step(4);
            Assert.That(boss.IsFrozen,Is.False,"Intro immunity must reject derived status effects too.");
            Set("DASH_03");Dash(true,false,new Vector3(10,1,0));
            typeof(GolgoraActor).GetMethod("DamagePlayerInRadius",BindingFlags.NonPublic|BindingFlags.Instance)
                .Invoke(boss,new object[]{new Vector3(10,1,0),2f,20f,DamageSourceType.BossStomp});
            Assert.That(player.CurrentHealth,Is.EqualTo(100),"A decoy can redirect aim, but the real player must still be inside the damage radius.");
        }
        [Test]
        public void BlizzardFreezesAndFrozenDeathReleasesSixPiercingShards()
        {
            Set("CRYO_03",3);Set("CRYO_04");var e=Enemy(0,3,1000);upgrades.Step(4);
            Assert.That(e.CurrentHealth,Is.EqualTo(925));Assert.That(e.FrozenRemaining,Is.EqualTo(1.5f));
            var next=Enemy(2,3,1000);Hit(e,2000);Assert.That(upgrades.ProjectilesFired,Is.EqualTo(6));
            for(int i=0;i<15;i++)upgrades.Step(.02f);
            Assert.That(next.CurrentHealth,Is.LessThan(1000));Assert.That(next.IsFrozen,Is.True);
            upgrades.Step(2);Assert.That(upgrades.ActiveProjectileCount,Is.Zero);
        }
        [Test]
        public void OrbitalSawsDealContactDamageWithCooldownAndPush()
        {
            Set("SCRAP_03",3);var e=Enemy(2.5f,0);upgrades.Step(.001f);
            Assert.That(e.CurrentHealth,Is.EqualTo(960));Assert.That(e.KnockbackAppliedCount,Is.EqualTo(1));
            upgrades.Step(.001f);Assert.That(e.CurrentHealth,Is.EqualTo(960));
            Assert.That(upgrades.ActiveVisualCount,Is.GreaterThanOrEqualTo(4));
        }
        [Test]
        public void RerollsAreLimitedPerRunAndDependenciesUnlockDraftCards()
        {
            UpgradeCatalog.TryGet("DEMO_01",out var powder);UpgradeCatalog.TryGet("CRYO_02",out var freeze);UpgradeCatalog.TryGet("CRYO_04",out var shatter);UpgradeCatalog.TryGet("CRIT_04",out var burst);
            Assert.That(draft.IsEligible(powder),Is.False);Assert.That(draft.IsEligible(freeze),Is.False);Assert.That(draft.IsEligible(shatter),Is.False);Assert.That(draft.IsEligible(burst),Is.False);
            Set("DEMO_03");Set("CRYO_01");Set("CRYO_03");Set("CRIT_01");Set("GREED_04",2);
            Assert.That(draft.IsEligible(powder)&&draft.IsEligible(freeze)&&draft.IsEligible(shatter)&&draft.IsEligible(burst),Is.True);
            xp.AddExperience(100);Assert.That(draft.TryReroll(),Is.True);Assert.That(draft.RerollsRemaining,Is.EqualTo(1));Assert.That(draft.TryReroll(),Is.True);Assert.That(draft.TryReroll(),Is.False);
            Assert.That(xp.PendingChoices,Is.EqualTo(1));Assert.That(session.IsPaused,Is.True);session.ResetSession(4921);Assert.That(draft.RerollsRemaining,Is.Zero);
        }
        [Test]
        public void CollectorMovesActualMapPickupsBeforeAwardingAndFreezesWithPause()
        {
            int id = pickups.SpawnExperience(new Vector3(40, 1, 40), 9);
            pickups.TryGetPickupInfo(id, out var before);
            Assert.That(pickups.AttractAll(), Is.EqualTo(1));
            pickups.Step(.2f); pickups.TryGetPickupInfo(id, out var moving);
            Assert.That(Vector3.Distance(moving.Position, player.transform.position), Is.LessThan(Vector3.Distance(before.Position, player.transform.position)));
            Assert.That(xp.TotalXp, Is.Zero);
            session.SetManualPause(true); pickups.Step(5f); pickups.TryGetPickupInfo(id, out var paused);
            Assert.That(paused.Position, Is.EqualTo(moving.Position));
            session.SetManualPause(false); pickups.Step(2f);
            Assert.That(xp.TotalXp, Is.EqualTo(9)); Assert.That(pickups.GlobalFlightCount, Is.Zero);
            Assert.That(pickups.TryGetPickupInfo(id, out _), Is.False);
            pickups.Step(2f); Assert.That(xp.TotalXp, Is.EqualTo(9));
        }
        [Test]
        public void ColdTintSurvivesImpactFlashAndRestoresAfterExpiryAndPoolReuse()
        {
            var enemy = Enemy(); var composer = enemy.GetComponent<ActorVisualComposer>();
            enemy.ApplySlow(.12f, 1.5f); enemy.StepVisuals(.2f);
            Assert.That(composer.StatusTint.b, Is.GreaterThan(composer.StatusTint.r));
            Hit(enemy, 1); enemy.StepVisuals(.2f);
            Assert.That(composer.StatusTint, Is.EqualTo(new Color(.38f,.78f,1f)));
            enemy.StepUpgradeStatus(2f); Assert.That(composer.StatusTint, Is.EqualTo(Color.white));
            enemy.ApplySlow(.2f, 2); enemy.ResetForSpawn(new Vector3(0,1,3));
            Assert.That(composer.StatusTint, Is.EqualTo(Color.white));
        }
        [Test]
        public void FlashlightUpgradesChangePaletteAndBrightnessAndRestoreOnReset()
        {
            var initial = FlashlightUpgradeLook.Resolve(stats);
            foreach (var id in new[]{"FLASH_01","FLASH_02","FLASH_03","FLASH_04","FLASH_05"})
            {
                stats.ResetToInitial(); Set(id);
                var look = FlashlightUpgradeLook.Resolve(stats);
                Assert.That(look.Color, Is.Not.EqualTo(initial.Color), id);
                Assert.That(look.Intensity, Is.GreaterThan(initial.Intensity), id);
            }
            stats.ResetToInitial(); Assert.That(FlashlightUpgradeLook.Resolve(stats).Color, Is.EqualTo(initial.Color));
        }
        [Test]
        public void RealParticleSystemsAndLightsStayBoundedPauseAndClearOnRestart()
        {
            Set("CRYO_01"); Set("CRYO_02"); Set("SCRAP_03"); Set("DASH_02"); Set("DASH_04");
            for(int i=0;i<40;i++) {var e=Enemy((i%8-4)*1.3f,3+i/8); e.ApplyFreeze(3);}
            for(int i=0;i<12;i++) {Dash(true,false,Vector3.up); Dash(false,true,Vector3.up); upgrades.Step(.016f);}
            Assert.That(upgrades.ActiveParticleCount, Is.GreaterThan(0));
            Assert.That(upgrades.ActiveParticleRigCount, Is.LessThanOrEqualTo(32));
            Assert.That(upgrades.ActiveLightCount, Is.LessThanOrEqualTo(6));
            int count=upgrades.ActiveParticleCount;
            session.SetManualPause(true); upgrades.Step(5f); Assert.That(upgrades.ActiveParticleCount, Is.EqualTo(count));
            session.SetManualPause(false); session.Effects.ReducedEffectsMode=true; upgrades.Step(.02f);
            Assert.That(upgrades.ActiveParticleRigCount, Is.Zero); Assert.That(upgrades.ActiveLightCount, Is.Zero);
            session.Effects.ReducedEffectsMode=false; upgrades.Step(.02f); Assert.That(upgrades.ActiveParticleRigCount, Is.GreaterThan(0));
            session.ResetSession(4921); Assert.That(upgrades.ActiveVisualCount, Is.Zero); Assert.That(upgrades.ActiveParticleCount, Is.Zero);
        }
        [TestCase("BIO_04", DamageType.Supernatural)]
        [TestCase("DASH_03", DamageType.Supernatural)]
        [TestCase("SCRAP_02", DamageType.Physical)]
        public void DerivedAttacksPreserveTheirDocumentedDamageChannel(string id, DamageType expected)
        {
            Set(id); Enemy(); DamageResult seen=default;
            session.Events.OnDamageResolved += hit => {if(!hit.IsPlayerTarget) seen=hit;};
            if(id=="BIO_04") {player.Heal(15); upgrades.Step(.01f);}
            if(id=="DASH_03") {Dash(true,false,Vector3.up); upgrades.Step(.01f); upgrades.Step(2.1f);}
            if(id=="SCRAP_02") {for(int i=0;i<15;i++)pickups.SpawnExperience(Vector3.one*5,1);pickups.CollectAll();upgrades.Step(.2f);}
            Assert.That(seen.AppliedRealDamage, Is.True);
            Assert.That(seen.DamageType, Is.EqualTo(expected));
        }

        [Test]
        public void PauseAndResetClearAllUpgradeStateWithoutCreatingMoreObjects()
        {
            foreach(var card in UpgradeCatalog.Cards)Set(card.Id,card.MaxTier);
            Enemy();Dash(true,false,Vector3.up);Dash(false,true,Vector3.up);upgrades.Step(.01f);
            int objects=upgrades.transform.childCount;int shots=upgrades.ProjectilesFired;
            session.SetManualPause(true);upgrades.Step(20);Assert.That(upgrades.ProjectilesFired,Is.EqualTo(shots));
            session.ResetSession(4921);Assert.That(upgrades.ActiveDeviceCount,Is.Zero);Assert.That(upgrades.ActiveProjectileCount,Is.Zero);Assert.That(upgrades.ActiveVisualCount,Is.Zero);
            Assert.That(upgrades.transform.childCount,Is.EqualTo(objects));Assert.That(upgrades.OverhealCharge,Is.Zero);Assert.That(upgrades.EmergencyCooldownRemaining,Is.Zero);
        }
        [Test]
        public void AllUpgradeSimulationStaysBoundedUnderCombatLoad()
        {
            foreach(var card in UpgradeCatalog.Cards)Set(card.Id,card.MaxTier);
            for(int i=0;i<150;i++)Enemy((i%15-7)*1.4f,(i/15+1)*1.4f,100000);
            upgrades.Step(.016f);int objects=upgrades.transform.childCount;
            var timer=Stopwatch.StartNew();
            long allocatedBefore=System.GC.GetAllocatedBytesForCurrentThread();
            for(int frame=0;frame<180;frame++)
            {
                if(frame%30==0){Dash(true,false,Vector3.up);Dash(false,true,Vector3.up);}
                upgrades.Step(1f/60);
            }
            long allocated=System.GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
            timer.Stop();Assert.That(allocated,Is.EqualTo(0),"Upgrade simulation must reuse its reserves.");Assert.That(upgrades.transform.childCount,Is.EqualTo(objects));
            Assert.That(upgrades.ProjectilesFired,Is.GreaterThan(0));
            UnityEngine.Debug.Log($"Upgrade stress: {timer.Elapsed.TotalMilliseconds/180:0.###} ms/step, {upgrades.ActiveVisualCount} views, {upgrades.ActiveProjectileCount} projectiles");
            Assert.That(timer.Elapsed.TotalMilliseconds/180,Is.LessThan(16.7));
        }
    }
}
