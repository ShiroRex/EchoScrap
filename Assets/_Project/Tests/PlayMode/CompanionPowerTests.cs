using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class CompanionPowerTests
    {
        private CombatGameBootstrap game;private CompanionPowerRuntime powers;private FusionStation station;
        private void Loaded(Scene s,LoadSceneMode m){var f=Object.FindFirstObjectByType<FusionStation>();if(f!=null)f.PersistProgress=false;}
        [UnitySetUp] public IEnumerator Setup()
        {
            SceneManager.sceneLoaded+=Loaded;yield return SceneManager.LoadSceneAsync("CombatArena");yield return null;SceneManager.sceneLoaded-=Loaded;
            game=Object.FindFirstObjectByType<CombatGameBootstrap>();station=Object.FindFirstObjectByType<FusionStation>();
            game.Director.AutoTick=false;game.Encounter.AutoTick=false;station.GetComponent<CompanionSystem>().AutoTick=false;
            station.ResetCollection();game.ResetRun(false);game.Weapon.enabled=false;game.Session.ResumeAllUserPauses();
            powers=game.Player.GetComponent<CompanionPowerRuntime>();powers.AutoTick=false;powers.SetEquipment(new int[0]);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            SceneManager.sceneLoaded-=Loaded;Time.timeScale=1;var scene=game.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("PowerCleanup"));yield return SceneManager.UnloadSceneAsync(scene);CombatTargetRegistry.Clear();
        }
        private EnemyActor Enemy(Vector3 offset,EnemyArchetypeId kind=EnemyArchetypeId.Residual)
        {
            var data=EnemyArchetypeData.CreateDefault(kind);data.MaxHealth=1000;data.ContactDamage=0;
            var enemy=new GameObject("PowerTarget").AddComponent<EnemyActor>();enemy.InitializeOrResetFromPool(data,game.Session,game.Player,game.Player.WorldCenter+offset);enemy.SimulationSuspended=true;enemy.transform.position=game.Player.WorldCenter+offset;enemy.Body.position=enemy.transform.position;return enemy;
        }
        [Test] public void BonusesStackWithoutOverwritingEarnedCardsOrHealingOnEquip()
        {
            game.Player.ForceSetHealthForTesting(50);game.Stats.SetUpgradeTier("BIO_01",1);
            float baseline=game.Stats.MaxHealth;powers.SetEquipment(new[]{0,0,13});
            Assert.AreEqual(baseline+35,game.Stats.MaxHealth);Assert.AreEqual(50,game.Player.CurrentHealth);
            powers.SetEquipment(new[]{4,5,14});Assert.AreEqual(baseline*2,game.Stats.MaxHealth);
            Assert.AreEqual(1.25f,game.Stats.CompanionMoveMultiplier,.001);Assert.AreEqual(1.15f,game.Stats.AttackSpeedMultiplier,.001);
            powers.SetEquipment(new[]{2,8,7});Assert.AreEqual(1.23f,game.Stats.CompanionDamageMultiplier,.001);Assert.AreEqual(1.2f,game.Stats.AttackSpeedMultiplier,.001);
            powers.SetEquipment(new int[0]);Assert.AreEqual(baseline,game.Stats.MaxHealth);Assert.AreEqual(1,game.Stats.GetUpgradeTier(UpgradeId.BIO_01));
        }
        [Test] public void ShieldBlocksOneRealHitAndCannotBeResetByReequipping()
        {
            powers.SetEquipment(new[]{6});Assert.IsTrue(game.Player.TakeContactOrHazardDamage(20,Vector3.zero).WasImmune);
            Assert.AreEqual(20,powers.ShieldCooldown);powers.SetEquipment(new int[0]);powers.SetEquipment(new[]{6});Assert.IsFalse(powers.ShieldReady);
            Assert.Greater(game.Player.TakeContactOrHazardDamage(20,Vector3.zero).HealthLost,0);
            game.Session.SetManualPause(true);powers.Advance(30);Assert.AreEqual(20,powers.ShieldCooldown);game.Session.SetManualPause(false);
            game.Session.SetWarehouseVisit(true);powers.Advance(30);Assert.AreEqual(20,powers.ShieldCooldown);game.Session.SetWarehouseVisit(false);
            powers.Advance(20);Assert.IsTrue(powers.ShieldReady);
        }
        [Test] public void SoldierReducesDamageAndMythicRescueIsOncePerExpedition()
        {
            powers.SetEquipment(new[]{13});Assert.AreEqual(15.3f,game.Player.TakeContactOrHazardDamage(20,Vector3.zero).HealthLost,.001);
            game.Player.StepTimers(5);powers.SetEquipment(new[]{4});game.Player.ForceSetHealthForTesting(5);
            var hit=game.Player.TakeContactOrHazardDamage(999,Vector3.zero);Assert.IsFalse(hit.CausedDeath);Assert.IsTrue(game.Player.IsAlive);
            Assert.AreEqual(game.Player.MaxHealth*.3f,game.Player.CurrentHealth,.001);Assert.IsTrue(powers.RescueUsed);
            powers.SetEquipment(new int[0]);powers.SetEquipment(new[]{4});game.Player.StepTimers(5);
            Assert.IsTrue(game.Player.TakeContactOrHazardDamage(999,Vector3.zero).CausedDeath);
        }
        [Test] public void HealingAndKillDrainRespectTheirIntervalsAndHealthCap()
        {
            powers.SetEquipment(new[]{3,10,12});game.Player.ForceSetHealthForTesting(50);powers.Advance(5);Assert.AreEqual(52,game.Player.CurrentHealth);
            for(int i=0;i<5;i++){var e=Enemy(Vector3.right*(i+2));var hit=new DamageRequest(game.Player.TargetId,DamageSourceType.FlashlightPulse,Vector3.zero,Vector3.forward,2000);e.ReceiveDamage(in hit);}
            Assert.AreEqual(53,game.Player.CurrentHealth);powers.Advance(10);Assert.AreEqual(60,game.Player.CurrentHealth);
            game.Player.Heal(999);powers.Advance(15);Assert.AreEqual(game.Player.MaxHealth,game.Player.CurrentHealth);
        }
        [Test] public void FrostControlsRegularEnemiesButOnlySlowsBossAndCrystalWaveDamages()
        {
            var normal=Enemy(Vector3.right*3);var boss=Enemy(Vector3.left*3,EnemyArchetypeId.GolgoraBoss);
            powers.SetEquipment(new[]{7,11});powers.Advance(8);Assert.Less(normal.CurrentHealth,1000);Assert.AreEqual(.4f,normal.SlowFraction,.001);
            powers.Advance(4);Assert.Greater(normal.FrozenRemaining,0);Assert.AreEqual(0,boss.FrozenRemaining);Assert.Greater(boss.SlowFraction,0);
        }
        [Test] public void MagicBoltPiercesAndDamageBonusesApplyToRealEnemyHits()
        {
            game.Player.transform.position=new Vector3(0,40,0);game.Player.GetComponent<Rigidbody>().position=game.Player.transform.position;
            var first=Enemy(Vector3.forward*3);var second=Enemy(Vector3.forward*5);powers.SetEquipment(new[]{1,8});powers.Advance(10);powers.Advance(.4f);
            Assert.AreEqual(1000-18*1.08f,first.CurrentHealth,.01);Assert.AreEqual(1000-18*1.08f,second.CurrentHealth,.01);
        }
        [Test] public void PeriodicDamageAndHealingScaleWithCopies()
        {
            var enemy = Enemy(Vector3.forward * 3);
            powers.SetEquipment(new[] { 11, 11, 11 });
            powers.Advance(8);
            Assert.AreEqual(955, enemy.CurrentHealth, .01);
            Assert.AreEqual(1, enemy.SlowFraction, .001);

            game.Player.ForceSetHealthForTesting(50);
            powers.SetEquipment(new[] { 3, 3, 3 });
            powers.Advance(5);
            Assert.AreEqual(56, game.Player.CurrentHealth, .01);
        }

        [Test] public void DuplicateShieldsAndRescuesEachAbsorbOneHit()
        {
            powers.SetEquipment(new[] { 6, 6, 4 });
            Assert.IsTrue(powers.TryBlockHit());
            powers.Advance(3);
            Assert.IsTrue(powers.TryBlockHit());
            Assert.IsFalse(powers.TryBlockHit());
            powers.Advance(17);
            Assert.IsTrue(powers.TryBlockHit());
            Assert.IsFalse(powers.TryBlockHit());

            powers.SetEquipment(new[] { 4, 4, 4 });
            Assert.AreEqual(400, game.Stats.MaxHealth, .01);
            for (int i = 0; i < 3; i++)
            {
                Assert.IsFalse(game.Player.TakeContactOrHazardDamage(999, Vector3.zero).CausedDeath);
                game.Player.StepTimers(5);
            }
            Assert.IsTrue(game.Player.TakeContactOrHazardDamage(999, Vector3.zero).CausedDeath);
        }

        [Test] public void SpeedBurstExpiresAndDoesNotAffectDashCooldown()
        {
            powers.SetEquipment(new[]{5,9});float cooldown=game.Stats.DashCooldown;
            powers.SendMessage("OnDash",new DashStateEvent(false,true,false,false,false,Vector3.zero,Vector3.forward,0,1));
            Assert.AreEqual(2.2f,game.Stats.CompanionMoveMultiplier,.001);powers.Advance(2);Assert.AreEqual(1.1f,game.Stats.CompanionMoveMultiplier,.001);Assert.AreEqual(cooldown,game.Stats.DashCooldown);
        }
        [Test] public void PixelShaderIsAvailableAndEffectsUseABoundedPool()
        {
            var shader=Resources.Load<Shader>("Companions/PixelEffects");Assert.IsNotNull(shader);Assert.IsTrue(shader.isSupported);
            var fx=game.Player.GetComponent<CompanionPixelEffects>();int before=fx.transform.childCount;
            for(int i=0;i<200;i++)fx.Emit(i%8,game.Player.WorldCenter,2,Color.white);
            Assert.AreEqual(before,fx.transform.childCount);fx.Advance(1);Assert.AreEqual(before,fx.transform.childCount);
        }
        [Test] public void BurningTicksWithoutRecursiveProcsAndStopsWhenUnequipped()
        {
            powers.SetEquipment(new[]{2});var enemy=Enemy(Vector3.forward*3);
            for(int i=0;i<100;i++){var hit=new DamageRequest(game.Player.TargetId,DamageSourceType.FlashlightPulse,game.Player.WorldCenter,Vector3.forward,.01f);enemy.ReceiveDamage(in hit);}
            float before=enemy.CurrentHealth;powers.Advance(3);Assert.AreEqual(13.8f,before-enemy.CurrentHealth,.05f);
            before=enemy.CurrentHealth;powers.Advance(3);Assert.AreEqual(before,enemy.CurrentHealth,.01f);
            for(int i=0;i<100;i++){var hit=new DamageRequest(game.Player.TargetId,DamageSourceType.FlashlightPulse,game.Player.WorldCenter,Vector3.forward,.01f);enemy.ReceiveDamage(in hit);}
            powers.SetEquipment(new int[0]);before=enemy.CurrentHealth;powers.Advance(3);Assert.AreEqual(before,enemy.CurrentHealth,.01f);
        }
        [Test] public void DuplicateDemoniosBurnIndependentlyAndUnequippingRemovesTheirStacks()
        {
            powers.SetEquipment(new[] { 2, 2, 2 });
            var enemy = Enemy(Vector3.forward * 3);
            var hit = new DamageRequest(game.Player.TargetId, DamageSourceType.FlashlightPulse,
                game.Player.WorldCenter, Vector3.forward, .01f);
            for (int i = 0; i < 100; i++) enemy.ReceiveDamage(in hit);

            float before = enemy.CurrentHealth;
            powers.Advance(1);
            Assert.AreEqual(3 * 4 * 1.45f, before - enemy.CurrentHealth, .05f);

            powers.SetEquipment(new[] { 2 });
            before = enemy.CurrentHealth;
            powers.Advance(2);
            Assert.AreEqual(2 * 4 * 1.15f, before - enemy.CurrentHealth, .05f);
        }

        [Test] public void NewExpeditionRestoresRescueAndStartsAtEquippedMaximumHealth()
        {
            station.Progress.owned[4]=1;game.Session.SetWarehouseVisit(true);Assert.IsTrue(station.EquipCompanion(0,4));station.GetComponent<CompanionSystem>().RefreshEquipment();game.Session.SetWarehouseVisit(false);
            game.Player.TakeContactOrHazardDamage(999,Vector3.zero);Assert.IsTrue(powers.RescueUsed);
            game.ResetRun(false);Assert.IsFalse(powers.RescueUsed);Assert.AreEqual(game.Player.MaxHealth,game.Player.CurrentHealth);Assert.IsTrue(powers.Has(4));
        }
    }
}
