using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace EchoScrap.Tests
{
    public sealed class BossArenaIntegrationTests
    {
        private CombatGameBootstrap game;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CombatArena");
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick = game.Encounter.AutoTick = false;
            game.ResetRun(false);
            game.Session.ResumeFromFocusLoss();
            game.Weapon.enabled = false;
            game.Player.IgnoreIncomingDamage = true;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            if (game != null)
            {
                Scene old = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("FoundryCleanup"));
                yield return SceneManager.UnloadSceneAsync(old);
            }
            CombatTargetRegistry.Clear();
        }

        private void Begin()
        {
            game.Session.Clock.SetGameplayTimeForTesting(game.WaveSettings.BossStartTime);
            game.Director.Step();
            Assert.IsNotNull(game.Encounter.Boss);
            game.Encounter.Boss.AutoTick = false;
        }

        private void Activate()
        {
            Begin();
            game.Encounter.Step(0.31f);
            game.Encounter.Step(0.31f);
            game.Encounter.Step(BossEncounter.IntroDuration);
            Assert.IsTrue(game.Director.BossActive);
        }

        [Test]
        public void EntranceAnnouncesNameThenRoarsAndLocksBothActorsUntilSoundEnds()
        {
            Begin();game.Encounter.Step(.31f);game.Encounter.Step(.31f);
            var roar=game.Encounter.RoarPresentation;
            var input=game.Motor.GetComponent<EchoScrap.Player.PlayerInputReader>();
            Vector3 playerPosition=game.Player.WorldCenter,bossPosition=game.Encounter.Boss.WorldCenter;
            game.Encounter.Step(.3f);
            Assert.IsTrue(roar.IsTitleVisible);Assert.IsFalse(roar.IsRoaring);
            Assert.IsFalse(input.CanRead);Assert.AreEqual(SessionState.BossIntro,game.Session.State);
            game.Encounter.Step(GolgoraRoarPresentation.TitleDuration);
            Assert.IsFalse(roar.IsTitleVisible);Assert.IsTrue(roar.IsRoaring);Assert.AreEqual(1,roar.RoarsPlayed);
            var feedback=game.CameraRig.GetComponent<CombatCameraFeedback>();feedback.StepFeedback(.01f);
            Assert.AreEqual(ShakePriority.Critical,feedback.CurrentWinningPriority);
            Assert.Greater(feedback.CurrentWinningShakeAmplitude,.3f);
            game.Session.SetManualPause(true);float remaining=game.Encounter.IntroRemaining;
            game.Encounter.Step(10);Assert.AreEqual(remaining,game.Encounter.IntroRemaining);
            game.Session.SetManualPause(false);
            game.Encounter.Step(game.Encounter.IntroRemaining-.01f);
            Assert.AreEqual(GolgoraState.Intro,game.Encounter.Boss.State);
            Assert.AreEqual(playerPosition,game.Player.WorldCenter);Assert.AreEqual(bossPosition,game.Encounter.Boss.WorldCenter);
            Assert.IsTrue(game.Player.IsBossCinematicProtected);
            game.Encounter.Step(.02f);
            Assert.IsFalse(roar.IsRoaring);Assert.IsFalse(game.Player.IsBossCinematicProtected);
            Assert.AreEqual(SessionState.Running,game.Session.State);
        }

        [Test]
        public void HalfHealthRoarsOnceClearsPressureAndReleasesPlayerBeforeTheJump()
        {
            Activate();var boss=game.Encounter.Boss;var roar=game.Encounter.RoarPresentation;
            int before=roar.RoarsPlayed;Vector3 position=boss.WorldCenter;
            game.BossArena.QueueMinionWave(game.WaveSettings,0,8);
            var half=new DamageRequest(1,DamageSourceType.SolarPulse,Vector3.zero,Vector3.forward,boss.MaxHealth*.5f);
            boss.ReceiveDamage(in half);
            Assert.AreEqual(GolgoraState.PhaseTransition,boss.State);Assert.IsTrue(roar.IsRoaring);
            Assert.AreEqual(before+1,roar.RoarsPlayed);Assert.AreEqual(0,game.BossArena.PendingMinions);
            Assert.AreEqual(SessionState.BossIntro,game.Session.State);Assert.IsTrue(game.Player.IsBossCinematicProtected);
            float health=boss.CurrentHealth;boss.ReceiveDamage(in half);Assert.AreEqual(health,boss.CurrentHealth);
            game.Session.Clock.SetSimulationRate(.5f);
            float duration=boss.CurrentPhaseDuration;
            boss.Step((duration-.01f)*.5f);game.Encounter.Step(.01f*.5f);
            Assert.AreEqual(GolgoraState.PhaseTransition,boss.State);Assert.AreEqual(position,boss.WorldCenter);
            game.Session.SetManualPause(true);boss.Step(10);game.Encounter.Step(10);
            Assert.AreEqual(GolgoraState.PhaseTransition,boss.State);
            game.Session.SetManualPause(false);boss.Step(.02f*.5f);
            Assert.AreEqual(GolgoraState.CercoReposition,boss.State);
            Assert.IsFalse(roar.IsRoaring);Assert.IsFalse(game.Player.IsBossCinematicProtected);
            Assert.AreEqual(SessionState.Running,game.Session.State);
            game.ResetRun(false);Assert.IsFalse(roar.IsRoaring);Assert.IsFalse(game.Player.IsBossCinematicProtected);
        }

        [Test]
        public void JumpLeavesCameraThenLandsAtCenterWithOneDamageWave_AndResetRestoresPose()
        {
            Activate();var boss=game.Encounter.Boss;
            boss.Body.position=boss.transform.position=game.BossArena.Center+new Vector3(6,1,0);
            boss.Step(boss.Settings.FirstCercoTime);
            Assert.IsTrue(boss.IsAirborne);Assert.IsFalse(boss.BodyCollider.enabled);
            boss.Step(boss.Settings.CercoJumpDuration*.58f);
            Assert.Greater(boss.JumpHeight,20f);Assert.AreEqual(0,boss.CercoPulsesEmitted);
            var view=boss.GetComponent<MataColeccionistaView>();
            Assert.Greater(view.Model.position.y,boss.WorldCenter.y+18f);
            game.Player.IgnoreIncomingDamage=false;
            game.Player.GetComponent<Rigidbody>().position=game.Player.transform.position=ArenaSurface.OnGround(boss.ArenaCenter,1f);
            float playerHealth=game.Player.CurrentHealth;
            boss.Step(boss.Settings.CercoJumpDuration*.42f);
            Assert.IsFalse(boss.IsAirborne);Assert.IsTrue(boss.BodyCollider.enabled);
            Assert.AreEqual(0,boss.JumpHeight);Assert.AreEqual(1,boss.CercoPulsesEmitted);
            Assert.Less(Vector3.Distance(ArenaSurface.OnGround(boss.WorldCenter),boss.ArenaCenter),.01f);
            Assert.AreEqual(1,boss.RingPool.ActiveCount);
            Assert.AreEqual(playerHealth-boss.Settings.RingDamage,game.Player.CurrentHealth);
            game.ResetRun(false);Assert.IsFalse(game.Player.IsBossCinematicProtected);
            Activate();Assert.AreEqual(0,game.Encounter.Boss.JumpHeight);
            Assert.Less(view.Model.position.y,game.Encounter.Boss.WorldCenter.y+2f);
        }

        [Test]
        public void SuppliedBossArtworkTracksRealHealthAndHidesAfterEncounterReset()
        {
            Activate();game.Hud.RefreshNow();
            var panel=game.Hud.CanvasRoot.transform.Find("BossHealth");
            var fill=panel.Find("BossEmptyTrack/BossFill").GetComponent<Image>();
            Assert.That(Resources.LoadAll<Texture2D>("Hud/Boss"),Has.Length.EqualTo(1));Assert.That(panel.gameObject.activeSelf);
            Assert.That(fill.fillAmount,Is.EqualTo(1f));Assert.That(fill.color,Is.EqualTo(Color.white));
            Assert.That(fill.sprite.texture.filterMode,Is.EqualTo(FilterMode.Point));
            Canvas.ForceUpdateCanvases();
            Assert.That(panel.Find("BossName").GetComponent<Text>().cachedTextGenerator.characterCountVisible,Is.GreaterThan(0),"The boss name and health values must fit their text rectangle.");
            var hit=new DamageRequest(1,DamageSourceType.FlashlightPulse,Vector3.zero,Vector3.forward,game.Encounter.Boss.MaxHealth*.55f);
            game.Encounter.Boss.ReceiveDamage(in hit);game.Hud.RefreshNow();
            Assert.That(fill.fillAmount,Is.EqualTo(game.Encounter.Boss.CurrentHealth/game.Encounter.Boss.MaxHealth).Within(.001f));
            Assert.That(fill.fillAmount,Is.InRange(.01f,.99f));
            game.ResetRun(false);game.Hud.RefreshNow();Assert.That(panel.gameObject.activeSelf,Is.False);
        }

        [UnityTest]
        public IEnumerator BossArtworkKeepsRedSpriteDuringCombatAndManualPause()
        {
            Activate();game.Hud.RefreshNow();var panel=game.Hud.CanvasRoot.transform.Find("BossHealth");
            var artwork=panel.Find("BossArtwork").GetComponent<Image>();
            var fill=panel.Find("BossEmptyTrack/BossFill").GetComponent<Image>();
            var red=artwork.sprite;var redFill=fill.sprite;
            Assert.That(red.texture.name,Is.EqualTo("000"));
            yield return new WaitForSecondsRealtime(.35f);
            Assert.That(artwork.sprite,Is.SameAs(red));Assert.That(fill.sprite,Is.SameAs(redFill));
            game.Session.SetManualPause(true);
            yield return new WaitForSecondsRealtime(.24f);
            Assert.That(artwork.sprite,Is.SameAs(red));Assert.That(fill.sprite,Is.SameAs(redFill));
            game.Session.SetManualPause(false);
        }

        [Test]
        public void KillIconStaysNextToChangingCountAndResetKeepsTheArtwork()
        {
            var root=game.Hud.CanvasRoot.transform;var icon=root.Find("KillsIcon").GetComponent<Image>();
            var text=root.Find("Kills").GetComponent<Text>();Assert.That(icon.sprite,Is.Not.Null);
            Assert.That(icon.sprite.texture.filterMode,Is.EqualTo(FilterMode.Point));Assert.That(icon.raycastTarget,Is.False);
            float initial=icon.rectTransform.anchoredPosition.x;
            for(int i=0;i<123;i++)
            {
                var death=new ActorDeathEvent(i,0,false,false,Vector3.zero,Vector3.zero,DamageSourceType.FlashlightPulse,0,false,false);
                game.Session.Events.PublishDeath(in death);
            }
            Assert.That(game.Hud.KillCount,Is.EqualTo(123));Assert.That(text.text,Does.Contain("123"));
            Assert.That(icon.rectTransform.anchoredPosition.x,Is.LessThan(initial));
            game.ResetRun(false);Assert.That(game.Hud.KillCount,Is.Zero);Assert.That(icon.sprite,Is.Not.Null);
            Assert.That(icon.rectTransform.anchoredPosition.x,Is.EqualTo(initial).Within(.01f));
        }

        [Test]
        public void TransferFromMapEdgePreservesBuildAndHealth_AndManualPauseFreezesFade()
        {
            var body = game.Player.GetComponent<Rigidbody>();
            body.position = new Vector3(80f, 1f, -75f);
            game.Player.transform.position = body.position;
            float health = game.Player.CurrentHealth;
            float damage = game.Weapon.ActiveDamagePerPulse;
            Begin();
            game.Encounter.Step(0.12f);
            float alpha = game.BossArena.TransferAlpha;
            game.Session.SetManualPause(true);
            game.Encounter.Step(3f);
            Assert.AreEqual(alpha, game.BossArena.TransferAlpha);
            Assert.Greater(body.position.x, 70f);
            game.Session.SetManualPause(false);
            game.Encounter.Step(0.2f);
            Assert.Less(Vector3.Distance(body.position, game.BossArena.Center + new Vector3(0f, 1f, -12f)), 0.2f);
            Assert.AreEqual(health, game.Player.CurrentHealth);
            Assert.AreEqual(damage, game.Weapon.ActiveDamagePerPulse);
            Assert.IsTrue(game.BossArena.BarriersEnabled);
            game.Encounter.Step(0.31f);
            Assert.IsFalse(game.BossArena.IsTransferring);
            game.Encounter.Step(BossEncounter.IntroDuration);
            Assert.AreEqual(GolgoraState.Pursuing, game.Encounter.Boss.State);
            game.Hud.RefreshNow();
            StringAssert.Contains("GÓLGORA", game.Hud.DisplayedBossName);
        }

        [UnityTest]
        public IEnumerator TransferOwnsTemporaryProtectionWithoutOverwritingOtherInvulnerability()
        {
            game.Player.IgnoreIncomingDamage = false;
            var lava = Object.FindAnyObjectByType<LavaHazard>(FindObjectsInactive.Include);
            Assert.IsNotNull(lava);
            lava.AutoTick = false;
            lava.transform.parent.gameObject.SetActive(true);
            yield return null;
            var body = game.Player.GetComponent<Rigidbody>();
            Vector3 position = ArenaSurface.OnGround(lava.transform.position, 1f);
            body.position = game.Player.transform.position = position;
            float health = game.Player.CurrentHealth;
            Begin();
            lava.StepHazard(0.02f);
            Assert.IsTrue(game.Player.IsEncounterTransferProtected);
            Assert.AreEqual(health, game.Player.CurrentHealth, "An immobilized transfer must survive the actual lava hazard.");
            game.Encounter.Step(0.61f);
            Assert.IsFalse(game.Player.IsEncounterTransferProtected);
            Assert.IsTrue(game.Player.IsBossCinematicProtected);
            Assert.IsFalse(game.Player.IgnoreIncomingDamage);
            game.Player.TakeContactOrHazardDamage(5f, Vector3.zero, 0, DamageSourceType.Lava);
            Assert.AreEqual(health, game.Player.CurrentHealth, "The entrance roar must keep the immobilized player protected after the fade.");
            game.Encounter.Step(BossEncounter.IntroDuration);
            Assert.IsFalse(game.Player.IsBossCinematicProtected);
            game.Player.TakeContactOrHazardDamage(5f, Vector3.zero, 0, DamageSourceType.Lava);
            Assert.AreEqual(health - 5f, game.Player.CurrentHealth);
            game.Player.IgnoreIncomingDamage = true;
            game.BossArena.EndEncounter();
            Assert.IsTrue(game.Player.IgnoreIncomingDamage, "Releasing transfer ownership must preserve another invulnerability source.");
            game.ResetRun(false);
            Assert.IsFalse(game.Player.IsEncounterTransferProtected);
        }

        [UnityTest]
        public IEnumerator JumpFromLowerArenaEdgeLeavesCameraBeforeLanding()
        {
            Activate();var boss=game.Encounter.Boss;
            boss.Body.position=boss.transform.position=ArenaSurface.OnGround(boss.ArenaCenter+Vector3.back*20,1);
            game.Player.GetComponent<Rigidbody>().position=game.Player.transform.position=ArenaSurface.OnGround(boss.ArenaCenter+Vector3.forward*21,1);
            for(int i=0;i<60;i++)yield return null;
            boss.Step(boss.Settings.FirstCercoTime);boss.Step(boss.Settings.CercoJumpDuration*.58f);
            yield return null;yield return null;
            var point=game.GameplayCamera.WorldToViewportPoint(boss.GetComponent<MataColeccionistaView>().Model.position);
            Assert.IsTrue(point.y>1.05f || point.z<0,"The model must leave the frame even when starting near its lower edge.");
        }

        [UnityTest]
        public IEnumerator FramingKeepsOppositeEdgesVisibleWithoutRotatingCamera()
        {
            Activate();
            Quaternion rotation = game.CameraRig.transform.rotation;
            Vector3 playerPosition = new Vector3(0f, 1f, -21.4f);
            Vector3 bossPosition = new Vector3(0f, 1f, 20f);
            game.Player.GetComponent<Rigidbody>().position = game.Player.transform.position = playerPosition;
            game.Encounter.Boss.Body.position = game.Encounter.Boss.transform.position = bossPosition;
            for (int i = 0; i < 45; i++) yield return null;
            Vector3 playerViewport = game.GameplayCamera.WorldToViewportPoint(playerPosition - Vector3.up);
            Vector3 bossViewport = game.GameplayCamera.WorldToViewportPoint(bossPosition + Vector3.up * 5f);
            Assert.That(playerViewport.y, Is.InRange(0.1f, 0.9f));
            Assert.That(bossViewport.y, Is.InRange(0.1f, 0.9f));
            Assert.Greater(playerViewport.z, 0f);
            Assert.Less(Quaternion.Angle(rotation, game.CameraRig.transform.rotation), 0.01f);
        }

        [Test]
        public void ReinforcementsHaveWarningAndCap_AndCercoClearsPressureWithoutKillingBoss()
        {
            Activate();
            int queued = game.BossArena.QueueMinionWave(game.WaveSettings, 0, 8);
            Assert.Greater(queued, 0);
            Assert.AreEqual(0, game.BossArena.SpawnedMinions);
            game.Encounter.Step(0.4f);
            Assert.AreEqual(0, game.BossArena.SpawnedMinions);
            game.Encounter.Step(0.51f);
            Assert.Greater(game.BossArena.SpawnedMinions, 0);
            game.BossArena.QueueMinionWave(game.WaveSettings, 0, 8);
            game.Encounter.Step(1f);
            int alive = 0;
            foreach (var actor in EnemyActor.ActiveEnemies) if (actor != game.Encounter.Boss && actor.IsAlive) alive++;
            Assert.LessOrEqual(alive + game.BossArena.PendingMinions, 8);
            float bossHealth = game.Encounter.Boss.CurrentHealth;
            game.Encounter.Boss.Step(game.Encounter.Boss.Settings.FirstCercoTime);
            Assert.IsTrue(game.Encounter.Boss.IsCercoSequence);
            Assert.IsTrue(game.Director.BossMinionsSuppressed);
            Assert.AreEqual(0, game.BossArena.PendingMinions);
            Assert.IsTrue(game.Encounter.Boss.IsAlive);
            Assert.AreEqual(bossHealth, game.Encounter.Boss.CurrentHealth);
            Assert.AreEqual(0, game.Director.SpawnBossMinionWave());
            game.Encounter.Boss.Step(game.Encounter.Boss.Settings.CercoJumpDuration);
            game.Encounter.Boss.Step(5f);
            game.Encounter.Boss.Step(game.Encounter.Boss.Settings.CercoRecovery);
            Assert.IsFalse(game.Director.BossMinionsSuppressed);
        }

        [Test]
        public void RepeatedRestartReopensPerimeterClearsWarningsAndRestoresCamera()
        {
            var follow = game.CameraRig.GetComponent<CinemachineFollow>();
            Vector3 offset = follow.FollowOffset;
            Quaternion rotation = game.CameraRig.transform.rotation;
            for (int i = 0; i < 10; i++)
            {
                Activate();
                game.BossArena.QueueMinionWave(game.WaveSettings, 0, 8);
                game.ResetRun(false);
                Assert.IsFalse(game.BossArena.IsClosed);
                Assert.IsFalse(game.BossArena.BarriersEnabled);
                Assert.AreEqual(0, game.BossArena.PendingMinions);
                Assert.AreEqual(0f, game.BossArena.TransferAlpha);
                Assert.IsFalse(game.Player.IsEncounterTransferProtected);
                Assert.AreSame(game.Player.transform, game.CameraRig.Follow);
                Assert.AreEqual(offset, follow.FollowOffset);
                Assert.Less(Quaternion.Angle(rotation, game.CameraRig.transform.rotation), 0.01f);
            }
        }
    }
}
