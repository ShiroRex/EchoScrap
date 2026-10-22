using System.Collections;
using System.Collections.Generic;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class CombatSessionLifecycleTests
    {
        private Scene testScene;
        private CombatSession session;
        private PlayerHealth player;
        private PlayerMotor motor;
        private PlayerInputReader input;
        private FlashlightWeapon weapon;
        private FlashlightPresentation flashlightView;
        private PlayerDashPresentation dashView;
        private PlayerHurtPresentation hurtView;
        private EnemyPool enemies;
        private EnemyDeathPresentationPool deathPool;
        private CorrosivePuddlePool hazards;
        private CombatDummy boss;
        private Keyboard keyboard;
        private readonly List<InputDevice> disabledDevices = new List<InputDevice>();
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldEditorInput;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            CombatTargetRegistry.Clear();
            oldBackground = InputSystem.settings.backgroundBehavior;
            oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            disabledDevices.Clear();
            foreach (InputDevice device in new List<InputDevice>(InputSystem.devices))
            {
                if (!device.enabled) continue;
                disabledDevices.Add(device);
                InputSystem.DisableDevice(device);
            }
            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return SceneManager.LoadSceneAsync("LocomotionSandbox", LoadSceneMode.Single);
            testScene = SceneManager.GetActiveScene();
            motor = Object.FindAnyObjectByType<PlayerMotor>();
            Assert.IsNotNull(motor);
            var playerObject = motor.gameObject;
            playerObject.layer = PhysicsLayers.PlayerLayer;
            playerObject.transform.position = new Vector3(-20f, 1f, -20f);
            motor.GetComponent<Rigidbody>().position = playerObject.transform.position;
            playerObject.AddComponent<ActorVisualComposer>();
            playerObject.AddComponent<PlayerRuntimeStats>();
            player = playerObject.AddComponent<PlayerHealth>();
            session = new GameObject("CombatSession").AddComponent<CombatSession>();
            weapon = playerObject.AddComponent<FlashlightWeapon>();
            weapon.BindSession(session);
            flashlightView = playerObject.AddComponent<FlashlightPresentation>();
            flashlightView.BindSession(session);
            dashView = playerObject.AddComponent<PlayerDashPresentation>();
            dashView.BindSession(session);
            hurtView = playerObject.AddComponent<PlayerHurtPresentation>();
            hurtView.Bind(session, player);
            input = playerObject.GetComponent<PlayerInputReader>();
            input.SendMessage("OnApplicationFocus", true);
            hazards = new GameObject("Hazards").AddComponent<CorrosivePuddlePool>();
            hazards.BindReferences(session, player);
            enemies = new GameObject("Enemies").AddComponent<EnemyPool>();
            enemies.BindReferences(session, player, hazards);
            deathPool = new GameObject("DeathPresentationPool").AddComponent<EnemyDeathPresentationPool>();
            deathPool.BindSession(session);
            var bossObject = new GameObject("BossDamageTarget");
            bossObject.transform.position = new Vector3(30f, 1f, 30f);
            bossObject.AddComponent<CapsuleCollider>();
            boss = bossObject.AddComponent<CombatDummy>();
            boss.Configure(session, player, hp: 10f, enableContact: false, asBossDummy: true);
            session.ResetSession(20261022);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            foreach (InputDevice device in disabledDevices)
                if (device.added) InputSystem.EnableDevice(device);
            InputSystem.settings.backgroundBehavior = oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
            CombatTargetRegistry.Clear();
            var cleanup = SceneManager.CreateScene("SessionLifecycleCleanup");
            SceneManager.SetActiveScene(cleanup);
            if (testScene.IsValid() && testScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(testScene);
        }

        [UnityTest]
        public IEnumerator UpdateBossDeath_AndLateUpdatePlayerDeath_ResolveAsDefeatAfterAllDamage()
        {
            var probe = new GameObject("LateDamageRegression").AddComponent<SessionLifecycleLateDamageProbe>();
            probe.Arm(session, boss, player, killPlayerInLateUpdate: true);
            int bossDeathsPublished = 0;
            int playerDeathsPublished = 0;
            session.Events.OnActorDied += death =>
            {
                if (death.IsBoss) bossDeathsPublished++;
                if (death.IsPlayer) playerDeathsPublished++;
            };
            yield return null;
            yield return null;
            Assert.IsTrue(probe.BossDiedInUpdate);
            Assert.IsTrue(probe.PlayerDiedInLateUpdate);
            Assert.AreEqual(SessionState.Running, probe.StateAfterBossDamage);
            Assert.AreEqual(SessionState.Running, probe.StateAfterPlayerDamage,
                "LateUpdate hazards must finish before the terminal result is published.");
            Assert.AreEqual(SessionState.Defeat, session.State);
            Assert.AreEqual(0, bossDeathsPublished);
            Assert.AreEqual(1, playerDeathsPublished);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsFalse(input.CanRead);
        }

        [UnityTest]
        public IEnumerator VictoryFreezesClockPhysicsAndInput_UntilReset()
        {
            var probe = new GameObject("VictoryRegression").AddComponent<SessionLifecycleLateDamageProbe>();
            probe.Arm(session, boss, player, killPlayerInLateUpdate: false);
            yield return null;
            yield return null;
            Assert.AreEqual(SessionState.Victory, session.State);
            Assert.IsTrue(session.Clock.HasPauseReason(PauseReason.SessionEnded));
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsFalse(input.CanRead);
            Assert.IsFalse(motor.enabled);
            Assert.AreEqual(Vector3.zero, motor.GetComponent<Rigidbody>().linearVelocity);
            float gameplayTime = session.GameplayTime;
            int simulationStep = session.Clock.SimulationStep;
            yield return null;
            yield return null;
            Assert.AreEqual(gameplayTime, session.GameplayTime);
            Assert.AreEqual(simulationStep, session.Clock.SimulationStep);
            session.ResumeAllUserPauses();
            Assert.AreEqual(0f, Time.timeScale, "Resume cannot remove SessionEnded.");
            session.ResetSession();
            Assert.AreEqual(SessionState.Running, session.State);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsTrue(motor.enabled);
            Assert.IsTrue(input.CanRead);
        }

        [Test]
        public void BossIntroSurvivesManualFocusAndDraftPauses_AndResetClearsIt()
        {
            session.SetBossIntroState(true);
            Assert.AreEqual(SessionState.BossIntro, session.State);
            session.SetManualPause(true);
            session.SimulateFocusChange(false);
            session.SetTacticalDraftPause(true);
            session.SetBossIntroState(true);
            Assert.AreEqual(SessionState.TacticalDraft, session.State);
            session.ConfirmDraftSelectionAndResume();
            Assert.AreEqual(SessionState.Paused, session.State);
            session.SetManualPause(false);
            Assert.AreEqual(SessionState.Paused, session.State);
            session.ResumeFromFocusLoss();
            Assert.AreEqual(SessionState.BossIntro, session.State);
            Assert.AreEqual(1f, Time.timeScale);
            session.SetBossIntroState(false);
            Assert.AreEqual(SessionState.Running, session.State);
            session.SetBossIntroState(true);
            session.ResetSession();
            session.SetManualPause(true);
            session.SetManualPause(false);
            Assert.AreEqual(SessionState.Running, session.State);
        }

        [Test]
        public void TenRestartsRestoreWeaponHealthAndPools_WithoutDuplicateSubscriptions()
        {
            for (int cycle = 0; cycle < 10; cycle++)
            {
                enemies.BindReferences(session, player, hazards);
                enemies.BindReferences(session, player, hazards);
                deathPool.BindSession(session);
                deathPool.BindSession(session);
                hurtView.Bind(session, player);
                EnemyActor enemy = enemies.Spawn(EnemyArchetypeId.Residual,
                    player.WorldCenter + Vector3.forward * 3f);
                weapon.ResetWeaponCycle();
                weapon.StepWeapon(0.2f);
                Assert.AreEqual(1, weapon.TotalPulsesEmitted);
                Assert.IsTrue(flashlightView.IsContactDiamondVisible);
                int deathBursts = deathPool.TotalBurstsPlayed;
                var lethal = new DamageRequest(1, DamageSourceType.SolarPulse, player.WorldCenter, Vector3.forward, 1000f);
                enemy.ReceiveDamage(in lethal);
                Assert.AreEqual(deathBursts + 1, deathPool.TotalBurstsPlayed, "One subscribed death handler.");
                Assert.AreEqual(1, deathPool.ActiveBurstCount);
                enemies.Spawn(EnemyArchetypeId.Residual, player.WorldCenter + Vector3.right * 5f);
                hazards.SpawnPuddle(1, new Vector3(40f, 0f, 40f));
                int hurtFeedback = hurtView.HurtFeedbackTriggerCount;
                player.ReceiveDamage(in lethal);
                Assert.AreEqual(hurtFeedback + 1, hurtView.HurtFeedbackTriggerCount);
                Assert.IsFalse(weapon.enabled, "Player death disables the flashlight.");
                session.ResolveSimulationStepOutcome();
                Assert.AreEqual(SessionState.Defeat, session.State);
                session.ResetSession(20261022);
                Assert.IsTrue(weapon.enabled, "Restart must re-enable its death-disabled component.");
                Assert.AreEqual(0, weapon.TotalPulsesEmitted);
                Assert.AreEqual(0f, weapon.WeaponTime);
                Assert.AreEqual(0, flashlightView.DisplayedPulseCount);
                Assert.IsFalse(flashlightView.IsContactDiamondVisible);
                Assert.AreEqual(0f, flashlightView.CurrentEmissionEnvelope);
                Assert.AreEqual(100f, player.CurrentHealth);
                Assert.AreEqual(1f, hurtView.ImmediateHealthRatio);
                Assert.AreEqual(1f, hurtView.RecentDamageRatio);
                Assert.AreEqual(0f, hurtView.ScreenBorderAlpha);
                Assert.AreEqual(0f, hurtView.DirectionalMarkerAlpha);
                Assert.AreEqual(0, deathPool.ActiveBurstCount);
                Assert.AreEqual(0, hazards.ActivePuddleCount);
                Assert.AreEqual(0, EnemyActor.ActiveEnemies.Count);
                Assert.AreEqual(1, enemies.TotalCreatedCount, "The same enemy is reused after each restart.");
                weapon.StepWeapon(0.199f);
                Assert.AreEqual(0, weapon.TotalPulsesEmitted);
                weapon.StepWeapon(0.001f);
                Assert.AreEqual(1, weapon.TotalPulsesEmitted);
                session.ResetSession(20261022);
            }
        }

        [UnityTest]
        public IEnumerator ResetDuringDashRestoresLayerAndClearsAfterimagesAndHurtFeedback()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            InputSystem.Update();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(motor.IsDashing);
            Assert.Greater(dashView.ActiveAfterimageCount, 0);
            Assert.IsTrue(dashView.IsExitRingActive);
            Assert.AreEqual(PhysicsLayers.PlayerDashLayer, player.gameObject.layer);
            session.ResetSession();
            Assert.IsFalse(motor.IsDashing);
            Assert.AreEqual(PhysicsLayers.PlayerLayer, player.gameObject.layer);
            Assert.AreEqual(0, dashView.ActiveAfterimageCount);
            Assert.IsFalse(dashView.IsExitRingActive);
            Assert.IsFalse(dashView.IsWallSparkActive);
            Assert.AreEqual(0f, motor.DashCooldownRemaining);
            Assert.AreEqual(0f, hurtView.ScreenBorderAlpha);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(motor.IsDashing);
        }

        [Test]
        public void RebindingPoolUnsubscribesPreviousSessionReset()
        {
            var otherSession = new GameObject("AnotherSession").AddComponent<CombatSession>();
            enemies.BindReferences(otherSession, player, hazards);
            EnemyActor enemy = enemies.Spawn(EnemyArchetypeId.Residual, new Vector3(40f, 1f, -40f));
            session.ResetSession();
            Assert.IsTrue(enemy.gameObject.activeSelf, "Old session cannot despawn actors owned by the new binding.");
            otherSession.ResetSession();
            Assert.IsFalse(enemy.gameObject.activeSelf);
        }
    }

    [DefaultExecutionOrder(100)]
    public sealed class SessionLifecycleLateDamageProbe : MonoBehaviour
    {
        private CombatSession session;
        private CombatDummy boss;
        private PlayerHealth player;
        private bool killPlayer;
        public bool BossDiedInUpdate { get; private set; }
        public bool PlayerDiedInLateUpdate { get; private set; }
        public SessionState StateAfterBossDamage { get; private set; }
        public SessionState StateAfterPlayerDamage { get; private set; }

        public void Arm(CombatSession combatSession, CombatDummy targetBoss, PlayerHealth targetPlayer, bool killPlayerInLateUpdate)
        {
            session = combatSession;
            boss = targetBoss;
            player = targetPlayer;
            killPlayer = killPlayerInLateUpdate;
        }

        private void Update()
        {
            if (session == null || BossDiedInUpdate) return;
            var lethal = new DamageRequest(1, DamageSourceType.SolarPulse, Vector3.zero, Vector3.forward, 1000f);
            boss.ReceiveDamage(in lethal);
            BossDiedInUpdate = !boss.IsAlive;
            StateAfterBossDamage = session.State;
        }

        private void LateUpdate()
        {
            if (!killPlayer || !BossDiedInUpdate || PlayerDiedInLateUpdate) return;
            var lethal = new DamageRequest(2, DamageSourceType.MothExplosion, Vector3.zero, Vector3.forward, 1000f);
            player.ReceiveDamage(in lethal);
            PlayerDiedInLateUpdate = !player.IsAlive;
            StateAfterPlayerDamage = session.State;
        }
    }
}
