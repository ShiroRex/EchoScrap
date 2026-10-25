using System.Collections;
using System.Collections.Generic;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class EnemyLaboratoryTests
    {
        private EnemyLaboratory lab;
        private readonly List<InputDevice> devices = new List<InputDevice>();
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            foreach (var device in new List<InputDevice>(InputSystem.devices))
                if (device.enabled) { devices.Add(device); InputSystem.DisableDevice(device); }
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("EnemyLaboratory");
            yield return null;
            lab = Object.FindAnyObjectByType<EnemyLaboratory>();
            Assert.IsNotNull(lab);
            lab.Session.SimulateFocusChange(true);
            lab.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus", true);
            lab.Session.ResumeAllUserPauses();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            foreach (var device in devices) if (device.added) InputSystem.EnableDevice(device);
            devices.Clear();
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            var oldScene = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Laboratory Test Cleanup"));
            yield return SceneManager.UnloadSceneAsync(oldScene);
            CombatTargetRegistry.Clear();
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void SceneStartsEmptyFlatProtectedAndWithoutSurvivalSystems()
        {
            Assert.IsTrue(lab.IsInitialized);
            Assert.AreEqual(0, lab.ActiveCount);
            Assert.AreEqual(0, lab.ObstacleCount);
            Assert.IsTrue(lab.PlayerProtected);
            Assert.IsTrue(lab.Player.IgnoreIncomingDamage);
            Assert.IsFalse(lab.Weapon.enabled);
            Assert.IsNull(Object.FindAnyObjectByType<ArenaSurface>());
            Assert.IsNull(Object.FindAnyObjectByType<CombatWaveDirector>());
            Assert.IsNull(Object.FindAnyObjectByType<UpgradeDraftController>());
            Assert.IsNull(Object.FindAnyObjectByType<BossEncounter>());
            Assert.AreEqual(0f, lab.Floor.bounds.max.y, 0.001f);
            Assert.IsNotNull(lab.Motor.GetComponent<CollectorSpriteView>());
            Assert.That(Mathf.DeltaAngle(50f, lab.GameplayCamera.transform.eulerAngles.x), Is.EqualTo(0f).Within(0.01f));
            Assert.AreEqual(5, GameObject.Find("Laboratorio · 100 x 100 m").GetComponentsInChildren<BoxCollider>().Length);
        }

        [Test]
        public void AllSixArchetypesSpawnWithTheirRealMechanicsIncludingActiveBoss()
        {
            lab.SetEnemiesFrozen(true);
            var types = new[] { EnemyArchetypeId.Residual, EnemyArchetypeId.Interceptor, EnemyArchetypeId.Colossus,
                EnemyArchetypeId.Mortar, EnemyArchetypeId.ExplosiveMoth, EnemyArchetypeId.GolgoraBoss };
            for (int i = 0; i < types.Length; i++)
            {
                EnemyActor actor = lab.Spawn(types[i], new Vector3(-15 + i * 6, 0, 14));
                Assert.IsNotNull(actor, types[i].ToString());
                Assert.IsTrue(actor.IsAlive);
                Assert.IsTrue(actor.PreventLethalDamage);
                Assert.IsTrue(actor.SimulationSuspended);
                Assert.IsTrue(actor.enabled, "Frozen targets must remain targetable.");
                if (actor is GolgoraActor boss) Assert.IsTrue(boss.IsCombatActive);
            }
            Assert.AreEqual(6, lab.ActiveCount);
            Assert.AreEqual(EnemyArchetypeId.GolgoraBoss, lab.Selected.ArchetypeId);
        }

        [Test]
        public void ImmortalityPreservesHitFeedbackAndCanBeDisabledAndDoesNotLeakThroughPool()
        {
            EnemyActor actor = lab.Spawn(EnemyArchetypeId.Residual, new Vector3(0, 0, 6));
            lab.DamageSelected(999f);
            Assert.IsTrue(actor.IsAlive);
            Assert.AreEqual(1f, actor.CurrentHealth);
            Assert.IsTrue(actor.VisualComposer.IsHitStopActive);
            lab.SetEnemiesFrozen(true);
            lab.SetEnemiesImmortal(false);
            lab.DamageSelected(999f);
            Assert.IsFalse(actor.gameObject.activeSelf);
            EnemyActor recycled = lab.Enemies.Spawn(EnemyArchetypeId.Residual, new Vector3(0, 1, 8));
            Assert.AreSame(actor, recycled);
            Assert.IsFalse(recycled.PreventLethalDamage);
            Assert.IsFalse(recycled.SimulationSuspended);
        }

        [Test]
        public void PlayerProtectionBlocksEveryDamageSourceAndSurvivesDashChanges()
        {
            foreach (var source in new[] { DamageSourceType.Contact, DamageSourceType.MothExplosion, DamageSourceType.BossStomp,
                DamageSourceType.DummyOrHazard })
            {
                lab.Player.SetDashInvulnerable(true);
                lab.Player.SetDashInvulnerable(false);
                lab.Player.TakeContactOrHazardDamage(500f, Vector3.forward, sourceType: source);
                Assert.AreEqual(lab.Player.MaxHealth, lab.Player.CurrentHealth);
            }
            lab.SetPlayerProtected(false);
            lab.Player.TakeContactOrHazardDamage(10f, Vector3.forward);
            Assert.AreEqual(lab.Player.MaxHealth - 10f, lab.Player.CurrentHealth);
        }

        [UnityTest]
        public IEnumerator FrozenAIStopsPursuitAndResumingRestoresItWithoutDisablingTheActor()
        {
            EnemyActor actor = lab.Spawn(EnemyArchetypeId.Residual, new Vector3(0, 0, 7));
            lab.SetEnemiesFrozen(true);
            Vector3 start = actor.WorldCenter;
            yield return new WaitForSeconds(0.25f);
            Assert.That(Vector3.Distance(start, actor.WorldCenter), Is.LessThan(0.05f));
            Assert.AreSame(actor, lab.Weapon.FindNearestEnemyTarget(lab.Weapon.OriginPosition, 10f, out _));
            lab.SetEnemiesFrozen(false);
            yield return new WaitForSeconds(0.25f);
            Assert.Greater(Vector3.Distance(start, actor.WorldCenter), 0.2f);
        }

        [Test]
        public void MortarStillFiresAndAnImmortalMothCanCompleteItsExplosion()
        {
            EnemyActor mortar = lab.Spawn(EnemyArchetypeId.Mortar, new Vector3(0, 0, 6));
            for (int i = 0; i < 150 && mortar.MortarShotsFiredCount == 0; i++)
                mortar.StepPhysics(0.02f);
            Assert.Greater(mortar.MortarShotsFiredCount, 0);
            EnemyActor moth = lab.Spawn(EnemyArchetypeId.ExplosiveMoth, new Vector3(5, 0, 5));
            lab.ArmSelectedMoth();
            Assert.IsTrue(moth.IsFuseActive);
            for (int i = 0; i < 100 && moth.gameObject.activeSelf; i++) moth.StepPhysics(0.02f);
            Assert.IsFalse(moth.gameObject.activeSelf, "Damage immortality must not suppress the moth's actual self-detonation.");
            Assert.IsTrue(lab.Player.IsAlive);
        }

        [UnityTest]
        public IEnumerator SlowMotionPauseAndSingleStepUseTheSessionClock()
        {
            lab.SetSpeed(0.25f);
            Assert.AreEqual(0.25f, Time.timeScale);
            lab.TogglePause();
            Assert.AreEqual(0f, Time.timeScale);
            lab.SetSpeed(2f);
            Assert.AreEqual(0f, Time.timeScale);
            lab.TogglePause();
            Assert.AreEqual(2f, Time.timeScale);
            lab.TogglePause();
            float before = lab.Session.GameplayTime;
            lab.StepOnce();
            float timeout = Time.realtimeSinceStartup + 2f;
            while (!lab.Session.IsPaused && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.IsTrue(lab.Session.IsPaused);
            Assert.That(lab.Session.GameplayTime - before, Is.InRange(0.09f, 0.15f));
            Assert.AreEqual(2f, lab.Session.Clock.SimulationRate);
            Assert.AreEqual(0f, Time.timeScale);
        }

        [Test]
        public void ClearAndResetRemoveActorsHazardsAndOptionalObstacles()
        {
            lab.SetSpawnPoint(new Vector3(8, 0, 8));
            lab.AddObstacle();
            Assert.AreEqual(1, lab.ObstacleCount);
            Assert.IsNull(lab.Spawn(EnemyArchetypeId.Residual, lab.SpawnPoint));
            lab.Spawn(EnemyArchetypeId.Mortar, new Vector3(-7, 0, 6));
            lab.SetAutoFire(true);
            lab.SetSpeed(0.5f);
            lab.ResetLab();
            Assert.AreEqual(0, lab.ActiveCount);
            Assert.AreEqual(0, lab.ObstacleCount);
            Assert.AreEqual(0, lab.Hazards.ActiveProjectileCount);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsTrue(lab.Player.IgnoreIncomingDamage);
            Assert.AreEqual(Vector3.up, lab.Motor.transform.position);
        }

        [UnityTest]
        public IEnumerator LaboratoryOverridesNeverAffectTheSurvivalArena()
        {
            lab.Spawn(EnemyArchetypeId.Residual, new Vector3(0, 0, 6));
            lab.SetSpeed(0.25f);
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            var game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = false;
            game.Encounter.AutoTick = false;
            Assert.IsFalse(game.Player.IgnoreIncomingDamage);
            Assert.AreEqual(1f, Time.timeScale);
            EnemyActor actor = game.Enemies.Spawn(EnemyArchetypeId.Residual, new Vector3(0, 1, 7));
            Assert.IsFalse(actor.PreventLethalDamage);
            Assert.IsFalse(actor.SimulationSuspended);
        }
    }
}
