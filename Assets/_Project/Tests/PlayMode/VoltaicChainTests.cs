using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class VoltaicChainTests
    {
        private static int sceneSequence;
        private Scene scene, previousScene;
        private CombatSession session;
        private PlayerRuntimeStats stats;
        private PlayerHealth player;
        private FlashlightWeapon weapon;
        private UpgradeCombatRuntime upgrades;
        private UpgradeTuning tuning;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            Time.timeScale=1f;CombatTargetRegistry.Clear();
            previousScene=SceneManager.GetActiveScene();
            scene=SceneManager.CreateScene("Voltaic chain test "+(++sceneSequence));SceneManager.SetActiveScene(scene);
            session=new GameObject("Session").AddComponent<CombatSession>();
            var actor=new GameObject("Collector");actor.layer=PhysicsLayers.PlayerLayer;actor.transform.position=Vector3.up;
            actor.AddComponent<CapsuleCollider>();
            stats=actor.AddComponent<PlayerRuntimeStats>();tuning=ScriptableObject.CreateInstance<UpgradeTuning>();tuning.voltaicVisualCapacity=8;tuning.projectileCapacity=256;
            typeof(PlayerRuntimeStats).GetField("upgradeTuning",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(stats,tuning);
            player=actor.AddComponent<PlayerHealth>();player.BindSession(session);session.ResolvePlayerReferences();
            weapon=actor.AddComponent<FlashlightWeapon>();weapon.BindSession(session);weapon.enabled=false;
            var progression=new GameObject("Progression").AddComponent<CombatProgression>();progression.BindSession(session);
            var pickups=new GameObject("Pickups").AddComponent<CombatPickupPool>();pickups.AutoTick=false;pickups.BindReferences(session,player,stats,progression);
            upgrades=new GameObject("Upgrade mechanics").AddComponent<UpgradeCombatRuntime>();upgrades.AutoTick=false;
            upgrades.Bind(session,stats,player,weapon,pickups,progression);session.ResetSession(9191);
            yield return null;
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale=1f;CombatTargetRegistry.Clear();
            if(previousScene.IsValid()&&previousScene.isLoaded)SceneManager.SetActiveScene(previousScene);
            else
            {
                var cleanup=SceneManager.GetSceneByName("Voltaic cleanup");
                if(!cleanup.IsValid()||!cleanup.isLoaded)cleanup=SceneManager.CreateScene("Voltaic cleanup");
                SceneManager.SetActiveScene(cleanup);
            }
            if(scene.IsValid()&&scene.isLoaded)yield return SceneManager.UnloadSceneAsync(scene);
            if(tuning!=null)Object.Destroy(tuning);
        }
        private void SetTier(int tier)
        {
            stats.SetUpgradeTier("FLASH_03",tier);weapon.ResetWeaponCycle();
        }
        private EnemyActor Enemy(float x,float z=3f,float health=1000f)
        {
            var data=EnemyArchetypeData.CreateDefault(EnemyArchetypeId.Residual);
            data.MaxHealth=health;data.BaseMoveSpeed=0f;data.ContactDamage=0;data.SpeedVarianceFraction=0;
            var enemy=new GameObject("Chain target").AddComponent<EnemyActor>();
            enemy.InitializeOrResetFromPool(data,session,player,new Vector3(x,1f,z));return enemy;
        }
        private DamageResult Hit(EnemyActor enemy,float damage=10f,bool critical=false,int pulse=4)
        {
            var request=new DamageRequest(player.TargetId,DamageSourceType.FlashlightPulse,player.WorldCenter,
                Vector3.forward,damage,critical,critical?1f:0f,stats.CritMultiplier,1,pulse);
            return enemy.ReceiveDamage(in request);
        }
        private LineRenderer ActiveCore()
        {
            foreach(var line in upgrades.GetComponentsInChildren<LineRenderer>())
                if(line.name=="Electric core"&&line.enabled)return line;
            return null;
        }

        [TestCase(1,2,.4f)] [TestCase(2,3,.6f)] [TestCase(3,4,.8f)]
        public void FourthPulseConnectsNearestUnvisitedTargetsInTimedOrderWithinTheInitialRadius(int tier,int count,float fraction)
        {
            SetTier(tier);var source=Enemy(0f);var targets=new EnemyActor[4];
            for(int i=0;i<targets.Length;i++)targets[i]=Enemy(i+1f);
            var outside=Enemy(5.2f);
            var hits=new List<int>(4);session.Events.OnDamageResolved+=result=>{if(result.SourceType==DamageSourceType.UpgradeProjectile)hits.Add(result.TargetId);};
            Hit(source,pulse:3);Assert.That(upgrades.ChainsTriggered,Is.Zero);
            Hit(source);Assert.That(upgrades.ActiveChainCount,Is.EqualTo(1));Assert.That(upgrades.ProjectilesFired,Is.Zero);
            upgrades.Step(tuning.voltaicHopDelay*.5f);Assert.That(hits,Is.Empty);
            upgrades.Step(tuning.voltaicHopDelay*.51f);
            Assert.That(hits.Count,Is.EqualTo(1));Assert.That(hits[0],Is.EqualTo(targets[0].TargetId));
            for(int i=1;i<count;i++)
            {
                upgrades.Step(tuning.voltaicHopDelay);
                Assert.That(hits.Count,Is.EqualTo(i+1));Assert.That(hits[i],Is.EqualTo(targets[i].TargetId));
            }
            for(int i=0;i<targets.Length;i++)Assert.That(targets[i].CurrentHealth,Is.EqualTo(i<count?1000f-10f*fraction:1000f).Within(.001f));
            Assert.That(outside.CurrentHealth,Is.EqualTo(1000f));Assert.That(upgrades.ChainHitsResolved,Is.EqualTo(count));
            Assert.That(upgrades.ActiveChainCount,Is.Zero);Assert.That(upgrades.ChainsTriggered,Is.EqualTo(1));
        }

        [Test]
        public void DerivedDamageInheritsTheCriticalOnceWithoutNewRandomRollsOrRecursiveChains()
        {
            SetTier(3);stats.SetBonusMitigation(0f,0f,0f,1f);
            var source=Enemy(0f);var targets=new[]{Enemy(1f),Enemy(2f),Enemy(3f),Enemy(4f)};
            var hits=new List<DamageResult>(4);session.Events.OnDamageResolved+=result=>{if(result.SourceType==DamageSourceType.UpgradeProjectile)hits.Add(result);};
            var primary=Hit(source,critical:true);int samples=session.Rng.Combat.SampleCount;
            int presentationSamples=session.Rng.Presentation.SampleCount;
            upgrades.Step(.25f);
            Assert.That(hits.Count,Is.EqualTo(4));
            foreach(var hit in hits)
            {
                Assert.That(hit.HealthLost,Is.EqualTo(primary.ResolvedDamage*.8f).Within(.001f));
                Assert.That(hit.DamageType,Is.EqualTo(DamageType.Supernatural));Assert.That(hit.IsCritical,Is.False);
            }
            Assert.That(session.Rng.Combat.SampleCount,Is.EqualTo(samples));Assert.That(session.Rng.Presentation.SampleCount,Is.EqualTo(presentationSamples));
            Assert.That(upgrades.ChainsTriggered,Is.EqualTo(1));Assert.That(upgrades.ChainHitsResolved,Is.EqualTo(targets.Length));
            upgrades.Step(1f);Assert.That(upgrades.ChainsTriggered,Is.EqualTo(1));
        }

        [Test]
        public void LethalOriginKeepsItsImpactAnchorAndReusedOrOutOfRangeTargetsAreSkipped()
        {
            SetTier(2);var source=Enemy(0f,health:5f);var reused=Enemy(1f);var moved=Enemy(2f);var valid=Enemy(3f);
            Vector3 origin=source.WorldCenter;
            Hit(source);Assert.That(source.IsAlive,Is.False);
            source.ResetForSpawn(new Vector3(-20f,1f,3f));
            reused.ResetForSpawn(new Vector3(1f,1f,3f));
            moved.transform.position=new Vector3(20f,1f,3f);
            upgrades.Step(.2f);
            Assert.That(reused.CurrentHealth,Is.EqualTo(1000f));Assert.That(moved.CurrentHealth,Is.EqualTo(1000f));
            Assert.That(valid.CurrentHealth,Is.EqualTo(994f));Assert.That(upgrades.ChainTargetsInvalidated,Is.EqualTo(2));
            Assert.That(upgrades.ChainHitsResolved,Is.EqualTo(1));
            var core=ActiveCore();Assert.That(core,Is.Not.Null);
            Assert.That(Vector3.Distance(core.GetPosition(0),origin),Is.LessThan(.001f),"The source's new pooled life must not move the old discharge.");
        }

        [Test]
        public void PauseFreezesLinksAndReducedEffectsKeepTheElectricalConnectionReadable()
        {
            SetTier(1);var source=Enemy(0f);Enemy(1f);Enemy(2f);Hit(source);
            upgrades.Step(tuning.voltaicHopDelay*1.01f);
            var core=ActiveCore();Assert.That(core,Is.Not.Null);Vector3 position=core.GetPosition(7);Color color=core.startColor;
            session.SetManualPause(true);upgrades.Step(10f);
            Assert.That(core.GetPosition(7),Is.EqualTo(position));Assert.That(core.startColor,Is.EqualTo(color));
            Assert.That(upgrades.ChainHitsResolved,Is.EqualTo(1));
            session.Effects.ReducedEffectsMode=true;session.SetManualPause(false);upgrades.Step(.001f);
            Assert.That(core.enabled,Is.True);Assert.That(core.startColor.a,Is.GreaterThan(.5f));
            foreach(var line in core.transform.parent.GetComponentsInChildren<LineRenderer>())
                if(line!=core)Assert.That(line.enabled,Is.False,"Reduced effects keep the core and suppress halo/forks.");
            session.ResetSession(9191);
            Assert.That(upgrades.ActiveChainCount,Is.Zero);Assert.That(upgrades.ActiveLightningCount,Is.Zero);
            Assert.That(upgrades.ChainHitsResolved,Is.Zero);
        }

        [Test]
        public void VisualSaturationNeverDiscardsChainDamageAndDisableOrDeathClearsPendingLinks()
        {
            SetTier(1);var source=Enemy(0f,health:100000f);var a=Enemy(1f);var b=Enemy(2f);
            int objects=upgrades.GetComponentsInChildren<Transform>(true).Length;
            for(int i=0;i<16;i++)Hit(source);
            upgrades.Step(.1f);
            Assert.That(upgrades.ChainHitsResolved,Is.EqualTo(32));Assert.That(a.CurrentHealth,Is.EqualTo(936f));Assert.That(b.CurrentHealth,Is.EqualTo(936f));
            Assert.That(upgrades.ActiveLightningCount,Is.EqualTo(8));Assert.That(upgrades.GetComponentsInChildren<Transform>(true).Length,Is.EqualTo(objects));
            Hit(source);upgrades.enabled=false;
            Assert.That(upgrades.ActiveChainCount,Is.Zero);Assert.That(upgrades.ActiveLightningCount,Is.Zero);
            Hit(source);Assert.That(upgrades.ActiveChainCount,Is.Zero);
            upgrades.enabled=true;Hit(source);upgrades.Step(tuning.voltaicHopDelay*1.01f);
            player.TakeContactOrHazardDamage(10000f,Vector3.forward);session.ResolveSimulationStepOutcome();
            Assert.That(upgrades.ActiveChainCount,Is.Zero);Assert.That(upgrades.ActiveLightningCount,Is.Zero);
        }

        [Test]
        public void LogicalReservePressureFinishesOldestHopsInsteadOfDiscardingDamage()
        {
            SetTier(1);var source=Enemy(0f,health:100000f);var a=Enemy(1f,health:100000f);var b=Enemy(2f,health:100000f);
            for(int i=0;i<257;i++)Hit(source);
            Assert.That(upgrades.ChainsTriggered,Is.EqualTo(257));Assert.That(upgrades.ChainOverflowResolutions,Is.EqualTo(1));
            upgrades.Step(.1f);
            Assert.That(upgrades.ChainHitsResolved,Is.EqualTo(514));Assert.That(upgrades.ActiveChainCount,Is.Zero);
            Assert.That(a.CurrentHealth,Is.EqualTo(100000f-257*4f));Assert.That(b.CurrentHealth,Is.EqualTo(100000f-257*4f));
            Assert.That(upgrades.ActiveLightningCount,Is.LessThanOrEqualTo(8));
        }

        [Test]
        public void RestartDuringADerivedHitCancelsRemainingHopsAndDoesNotResurrectOldVisuals()
        {
            SetTier(1);var source=Enemy(0f);var a=Enemy(1f);var b=Enemy(2f);bool restarted=false;
            session.Events.OnDamageResolved+=result=>
            {
                if(result.SourceType!=DamageSourceType.UpgradeProjectile||restarted)return;
                restarted=true;session.ResetSession(9191);
            };
            Hit(source);upgrades.Step(.2f);
            Assert.That(restarted,Is.True);Assert.That(a.CurrentHealth,Is.EqualTo(996f));Assert.That(b.CurrentHealth,Is.EqualTo(1000f));
            Assert.That(upgrades.ActiveChainCount,Is.Zero);Assert.That(upgrades.ActiveLightningCount,Is.Zero);
            Assert.That(upgrades.ChainsTriggered,Is.Zero);Assert.That(upgrades.ChainHitsResolved,Is.Zero);
        }

        [Test]
        public void WarmLinkAnimationHasNoManagedAllocations()
        {
            SetTier(1);var source=Enemy(0f,health:100000f);Enemy(1f);Enemy(2f);
            Hit(source);upgrades.Step(.1f);upgrades.Step(.4f);
            Hit(source);upgrades.Step(tuning.voltaicHopDelay*1.01f);
            for(int i=0;i<8;i++)upgrades.Step(.0001f);
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<100;i++)upgrades.Step(.0001f);
            Assert.That(GC.GetAllocatedBytesForCurrentThread()-before,Is.Zero);
            Assert.That(upgrades.ActiveLightningCount,Is.GreaterThan(0));
        }
    }
}
